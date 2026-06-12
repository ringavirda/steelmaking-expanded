using System;
using System.Collections.Generic;
using ExpandedLib;
using HarmonyLib;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Patches;

/// <summary>
/// Harmony patches on the vanilla tool mold that adjust its right-click flow so
/// filled molds produced by the mold pedestal can be retrieved cleanly:
///
/// <list type="bullet">
///   <item>A hardened, full mold yields the cast item first; the (now empty)
///   mold stays in the world.</item>
///   <item>Any other state — empty, still-liquid, or unfinished — picks up the
///   mold itself and removes the block, carrying any remaining contents along
///   in <c>blockEntityAttributes</c> so they survive replacement. A mold whose
///   metal is still liquid may only be taken into an empty hand (anywhere else
///   it would instantly spill).</item>
/// </list>
///
/// Implemented as patches (not a re-registered block class) so other mods that
/// touch the tool mold can coexist.
/// </summary>
[HarmonyPatch]
public static class ToolMoldPatches
{
  // Cache of baked held-item meshes (mold body + molten metal surface), keyed
  // by block + metal + quantised fill level + quantised glow so the number of
  // uploaded meshes stays bounded. Cleared when the mod system is disposed.
  private static readonly Dictionary<
    string,
    MultiTextureMeshRef
  > _heldContentMeshes = [];

  /// <summary>Disposes and clears the cached held-content meshes (client shutdown).</summary>
  public static void ClearMeshCache()
  {
    foreach (var meshRef in _heldContentMeshes.Values)
      meshRef?.Dispose();
    _heldContentMeshes.Clear();
  }

  /// <summary>
  /// Renders the molten-metal surface inside a held filled mold. Declared on
  /// <see cref="CollectibleObject"/> (the mold does not override it), so the
  /// patch guards on the instance type before doing any work.
  /// </summary>
  [HarmonyPostfix]
  [HarmonyPatch(
    typeof(CollectibleObject),
    nameof(CollectibleObject.OnBeforeRender)
  )]
  public static void OnBeforeRenderPostfix(
    CollectibleObject __instance,
    ICoreClientAPI capi,
    ItemStack itemstack,
    ref ItemRenderInfo renderinfo
  )
  {
    if (__instance is not BlockToolMold mold)
      return;

    var (contents, fill) = MoltenContents.Read(
      itemstack,
      MoltenContents.MoldUnitsKey,
      capi.World
    );
    if (contents?.Collectible == null || fill <= 0)
      return;

    int required = mold.Attributes?["requiredUnits"].AsInt(100) ?? 100;
    float fillHeight = mold.Attributes?["fillHeight"].AsFloat(1f) ?? 1f;
    float level = required > 0 ? fill * fillHeight / required : 0f;

    float temp = contents.Collectible.GetTemperature(capi.World, contents);
    int glow = (int)GameMath.Clamp((temp - 550f) / 2f, 0f, 255f);

    string key =
      $"{mold.Code}|{contents.Collectible.Code}|{(int)(level * 16)}|{(int)temp / 50}";

    if (
      !_heldContentMeshes.TryGetValue(key, out var meshRef) || meshRef.Disposed
    )
    {
      meshRef = BuildHeldContentMesh(mold, capi, contents, level, glow, temp);
      if (meshRef == null)
        return;
      _heldContentMeshes[key] = meshRef;
    }

    renderinfo.ModelRef = meshRef;
  }

  private static MultiTextureMeshRef? BuildHeldContentMesh(
    BlockToolMold mold,
    ICoreClientAPI capi,
    ItemStack contents,
    float level,
    int glow,
    float temp
  )
  {
    MeshData? mesh = capi.TesselatorManager.GetDefaultBlockMesh(mold)?.Clone();
    if (mesh == null)
      return null;

    var firstTex =
      contents.Item?.FirstTexture ?? contents.Block?.FirstTextureInventory;
    if (firstTex == null)
      return capi.Render.UploadMultiTextureMesh(mesh);

    capi.BlockTextureAtlas.GetOrInsertTexture(
      firstTex.Base.Clone(),
      out _,
      out var texPos
    );
    if (texPos == null)
      return capi.Render.UploadMultiTextureMesh(mesh);

    Cuboidf[] boxes =
      mold.Attributes?["fillQuadsByLevel"]?.AsObject<Cuboidf[]>()
      ?? [new Cuboidf(2f, 0f, 2f, 14f, 0f, 14f)];
    Cuboidf box = boxes[(int)GameMath.Clamp(level, 0, boxes.Length - 1)];

    float shapeRotY = (mold.Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;

    // GetQuad() yields a unit quad; vanilla's ToolMoldRenderer transform places
    // it as the liquid surface at the right height. We additionally apply the
    // shape's rotateY so it lines up with the (already rotated) default mesh.
    MeshData quad = QuadMeshUtil.GetQuad();

    // Tint the surface toward the metal's incandescence colour so hot metal
    // reads as glowing red/orange instead of plain bright white. Below ~500°C
    // there is no incandescence, so fall back to white (show the metal texture
    // as-is). The glow flag below adds the self-illumination.
    byte cr = 255,
      cg = 255,
      cb = 255;
    if (temp >= 500f)
    {
      float[] inc = ColorUtil.GetIncandescenceColorAsColor4f((int)temp);
      cr = (byte)(GameMath.Clamp(inc[0], 0f, 1f) * 255f);
      cg = (byte)(GameMath.Clamp(inc[1], 0f, 1f) * 255f);
      cb = (byte)(GameMath.Clamp(inc[2], 0f, 1f) * 255f);
    }
    quad.Rgba = new byte[16];
    for (int i = 0; i < 4; i++)
    {
      quad.Rgba[i * 4] = cr;
      quad.Rgba[i * 4 + 1] = cg;
      quad.Rgba[i * 4 + 2] = cb;
      quad.Rgba[i * 4 + 3] = 255;
    }
    quad.Flags = new int[4];
    for (int i = 0; i < 4; i++)
      quad.Flags[i] = glow & VertexFlags.GlowLevelBitMask;

    float[] matrix = new Matrixf()
      .Translate(0.5f, 0f, 0.5f)
      .RotateY(shapeRotY)
      .Translate(-0.5f, 0f, -0.5f)
      .Translate(
        1f - box.X1 / 16f,
        0.063125f + Math.Max(0f, level / 16f - 1f / 48f),
        1f - box.Z1 / 16f
      )
      .RotateX((float)Math.PI / 2f)
      .Scale(0.5f * box.Width / 16f, 0.5f * box.Length / 16f, 0.5f)
      .Translate(-1f, -1f, 0f)
      .Values;
    quad.MatrixTransform(matrix);

    // Map the unit UVs into the metal texture's slot in the block atlas.
    for (int i = 0; i < quad.Uv.Length; i++)
      quad.Uv[i] =
        i % 2 == 0
          ? quad.Uv[i] * (texPos.x2 - texPos.x1) + texPos.x1
          : quad.Uv[i] * (texPos.y2 - texPos.y1) + texPos.y1;
    quad.TextureIds = [texPos.atlasTextureId];
    quad.TextureIndices = [0];
    quad.TextureIndicesCount = 1;

    mesh.AddMeshData(quad);
    return capi.Render.UploadMultiTextureMesh(mesh);
  }

  /// <summary>
  /// Vanilla only shows the "pick up" hint when the mold is empty
  /// (MetalContent == null). Our interaction also lets the player pick up a
  /// filled-but-unfinished mold (still molten / not a hardened full casting),
  /// so advertise that case too.
  /// </summary>
  [HarmonyPostfix]
  [HarmonyPatch(
    typeof(BlockToolMold),
    nameof(BlockToolMold.GetPlacedBlockInteractionHelp)
  )]
  public static void InteractionHelpPostfix(
    IWorldAccessor world,
    IPlayer forPlayer,
    ref WorldInteraction[] __result
  )
  {
    var pickupFilled = new WorldInteraction
    {
      ActionLangCode = "blockhelp-toolmold-pickup",
      MouseButton = EnumMouseButton.Right,
      // Pickup needs an empty hand. Gate the hint on that rather than
      // RequireFreeHand, which would draw an empty slot next to the hint.
      ShouldApply = (wi, bs, es) =>
        forPlayer.Entity.RightHandItemSlot.Empty
        && world.BlockAccessor.GetBlockEntity(bs.Position)
          is BlockEntityToolMold { Shattered: false } be
        && be.MetalContent != null
        && !(be.IsHardened && be.IsFull),
    };

    __result = __result.Append(pickupFilled);
  }

  /// <summary>
  /// Whether the metal currently in <paramref name="be"/> can be cast into this
  /// mold's tool. Mirrors <c>BlockEntityToolMold.GetMoldedStacks</c>: the casting
  /// drop code carries a <c>{metal}</c> placeholder, so we substitute the metal's
  /// last code part and check the result resolves to a real collectible. Used to
  /// gate the "finished casting" path so non-castable charges (e.g. slag) skip it
  /// without triggering vanilla's resolver — which would log a resolve warning.
  /// </summary>
  private static bool CanCastInto(
    BlockToolMold mold,
    IWorldAccessor world,
    BlockEntityToolMold be
  )
  {
    if (mold.Attributes == null || be.MetalContent?.Collectible == null)
      return false;

    string metal = be.MetalContent.Collectible.LastCodePart();

    var templates = new List<JsonItemStack>();
    if (mold.Attributes["drop"].Exists)
    {
      var one = mold.Attributes["drop"]
        .AsObject<JsonItemStack>(null, mold.Code.Domain);
      if (one != null)
        templates.Add(one);
    }
    else
    {
      var many = mold.Attributes["drops"]
        .AsObject<JsonItemStack[]>(null, mold.Code.Domain);
      if (many != null)
        templates.AddRange(many);
    }

    foreach (var tmpl in templates)
    {
      if (tmpl?.Code == null)
        continue;
      AssetLocation loc = tmpl.Code.Clone();
      loc.Path = loc.Path.Replace("{metal}", metal);
      bool exists =
        tmpl.Type == EnumItemClass.Block
          ? world.GetBlock(loc) != null
          : world.GetItem(loc) != null;
      if (exists)
        return true;
    }
    return false;
  }

  /// <summary>
  /// Replaces the vanilla right-click flow for plain clicks (no sneak, no held
  /// interaction): hand over a finished casting, otherwise pick the mold up with
  /// its contents. Returns true (run vanilla) for every case this flow does not
  /// claim.
  /// </summary>
  [HarmonyPrefix]
  [HarmonyPatch(
    typeof(BlockToolMold),
    nameof(BlockToolMold.OnBlockInteractStart)
  )]
  public static bool OnBlockInteractStartPrefix(
    BlockToolMold __instance,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref bool __result
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is not BlockEntityToolMold be
      || be.Shattered
    )
      return true;

    // Leave shift-clicks and held-item interactions (chiseling, pouring) to vanilla.
    if (
      byPlayer.Entity.Controls.ShiftKey
      || byPlayer.Entity.Controls.HandUse != EnumHandInteract.None
    )
      return true;

    if (world.Side == EnumAppSide.Client)
    {
      __result = true;
      return false;
    }

    bool hasMetal = be.MetalContent != null && be.FillLevel > 0;
    var dropPos = blockSel.Position.ToVec3d().Add(0.5, 0.2, 0.5);

    // 1) Finished casting: hand over the molded item(s); keep the empty mold in place.
    //    Only when the held metal can actually be cast into this mold's tool —
    //    otherwise (e.g. a mold full of slag) fall through to picking the mold up,
    //    rather than letting vanilla's resolver build a bogus "...-slag" stack and
    //    log a "could not resolve" warning.
    if (
      hasMetal
      && be.IsHardened
      && be.IsFull
      && CanCastInto(__instance, world, be)
    )
    {
      ItemStack[]? molded = be.GetStateAwareMoldedStacks();
      if (molded is { Length: > 0 })
      {
        foreach (var stack in molded)
        {
          int stackSize = stack.StackSize;
          if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
            world.SpawnItemEntity(stack, dropPos);
          world.Logger.Audit(
            "{0} took {1}x{2} from tool mold at {3}.",
            byPlayer.PlayerName,
            stackSize,
            stack.Collectible.Code,
            blockSel.Position
          );
        }

        be.MetalContent = null;
        be.FillLevel = 0;
        be.UpdateRenderer();
        be.MarkDirty(true);
        world.PlaySoundAt(
          ExSounds.Ingot,
          blockSel.Position,
          -0.5,
          byPlayer,
          randomizePitch: false
        );
        __result = true;
        return false;
      }
    }

    // 2) Otherwise pick up the mold itself, preserving any remaining contents.
    //    Still-liquid metal may only travel in an empty hand — anywhere else in
    //    the inventory it instantly spills, so refuse the pickup outright.
    bool liquid =
      hasMetal
      && MoltenMoldSpill.IsLiquidContent(world, be.MetalContent, be.FillLevel);
    if (
      liquid
      && MoltenMoldSpill.DenyLiquidPickup(
        world,
        byPlayer,
        be.MetalContent,
        be.FillLevel
      )
    )
    {
      __result = true;
      return false;
    }

    var moldStack = new ItemStack(__instance);
    if (hasMetal)
    {
      var beData = new TreeAttribute();
      beData.SetItemstack("contents", be.MetalContent!.Clone());
      beData.SetInt("fillLevel", be.FillLevel);
      beData.SetBool("shattered", false);
      beData.SetFloat("meshAngle", be.MeshAngle);
      moldStack.Attributes["blockEntityAttributes"] = beData;
    }

    MoltenMoldSpill.GiveMoldStack(world, byPlayer, moldStack, liquid, dropPos);
    world.Logger.Audit(
      "{0} took 1x{1} from tool mold at {2}.",
      byPlayer.PlayerName,
      moldStack.Collectible.Code,
      blockSel.Position
    );

    world.BlockAccessor.SetBlock(0, blockSel.Position);
    if (__instance.Sounds?.Place != null)
      world.PlaySoundAt(
        __instance.Sounds.Place,
        blockSel.Position,
        -0.5,
        byPlayer
      );
    __result = true;
    return false;
  }
}
