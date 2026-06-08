using System;
using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Overrides;

/// <summary>
/// Drop-in replacement for the vanilla <see cref="BlockToolMold"/> with a
/// tweaked right-click flow so filled molds produced by the mold pedestal can
/// be retrieved cleanly:
///
/// <list type="bullet">
///   <item>A hardened, full mold yields the cast item first; the (now empty)
///   mold stays in the world.</item>
///   <item>Any other state — empty, still-liquid, or unfinished — picks up the
///   mold itself and removes the block, carrying any remaining contents along
///   in <c>blockEntityAttributes</c> so they survive replacement.</item>
/// </list>
///
/// Vanilla routes this through the non-virtual
/// <c>BlockEntityToolMold.OnPlayerInteract</c>, which can't be overridden from
/// the block entity, so the logic lives here instead.
/// </summary>
[EntityRegister("BlockToolMold", PrefixModId = false)]
public class CustomBlockToolMold : BlockToolMold
{
  // Cache of baked held-item meshes (mold body + molten metal surface), keyed
  // by block + metal + quantised fill level + quantised glow so the number of
  // uploaded meshes stays bounded.
  private readonly Dictionary<string, MultiTextureMeshRef> _heldContentMeshes =
  [];

  public override void OnBeforeRender(
    ICoreClientAPI capi,
    ItemStack itemstack,
    EnumItemRenderTarget target,
    ref ItemRenderInfo renderinfo
  )
  {
    base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

    var beData = itemstack.Attributes?.GetTreeAttribute(
      "blockEntityAttributes"
    );
    var contents = beData?.GetItemstack("contents");
    int fill = beData?.GetInt("fillLevel") ?? 0;
    if (contents == null || fill <= 0)
      return;
    contents.ResolveBlockOrItem(capi.World);
    if (contents.Collectible == null)
      return;

    int required = Attributes?["requiredUnits"].AsInt(100) ?? 100;
    float fillHeight = Attributes?["fillHeight"].AsFloat(1f) ?? 1f;
    float level = required > 0 ? fill * fillHeight / required : 0f;

    float temp = contents.Collectible.GetTemperature(capi.World, contents);
    int glow = (int)GameMath.Clamp((temp - 550f) / 2f, 0f, 255f);

    string key =
      $"{Code}|{contents.Collectible.Code}|{(int)(level * 16)}|{(int)temp / 50}";

    if (
      !_heldContentMeshes.TryGetValue(key, out var meshRef) || meshRef.Disposed
    )
    {
      meshRef = BuildHeldContentMesh(capi, contents, level, glow, temp);
      if (meshRef == null)
        return;
      _heldContentMeshes[key] = meshRef;
    }

    renderinfo.ModelRef = meshRef;
  }

  private MultiTextureMeshRef? BuildHeldContentMesh(
    ICoreClientAPI capi,
    ItemStack contents,
    float level,
    int glow,
    float temp
  )
  {
    MeshData? mesh = capi.TesselatorManager.GetDefaultBlockMesh(this)?.Clone();
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
      Attributes?["fillQuadsByLevel"]?.AsObject<Cuboidf[]>()
      ?? [new Cuboidf(2f, 0f, 2f, 14f, 0f, 14f)];
    Cuboidf box = boxes[(int)GameMath.Clamp(level, 0, boxes.Length - 1)];

    float shapeRotY = (Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;

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

  public override void OnUnloaded(ICoreAPI api)
  {
    foreach (var meshRef in _heldContentMeshes.Values)
      meshRef?.Dispose();
    _heldContentMeshes.Clear();
    base.OnUnloaded(api);
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    // Vanilla only shows the "pick up" hint when the mold is empty
    // (MetalContent == null). Our interaction also lets the player pick up a
    // filled-but-unfinished mold (still molten / not a hardened full casting),
    // so advertise that case too.
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

    return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
      .Append(pickupFilled);
  }

  /// <summary>
  /// Whether the metal currently in <paramref name="be"/> can be cast into this
  /// mold's tool. Mirrors <c>BlockEntityToolMold.GetMoldedStacks</c>: the casting
  /// drop code carries a <c>{metal}</c> placeholder, so we substitute the metal's
  /// last code part and check the result resolves to a real collectible. Used to
  /// gate the "finished casting" path so non-castable charges (e.g. slag) skip it
  /// without triggering vanilla's resolver — which would log a resolve warning.
  /// </summary>
  private bool CanCastInto(IWorldAccessor world, BlockEntityToolMold be)
  {
    if (Attributes == null || be.MetalContent?.Collectible == null)
      return false;

    string metal = be.MetalContent.Collectible.LastCodePart();

    var templates = new List<JsonItemStack>();
    if (Attributes["drop"].Exists)
    {
      var one = Attributes["drop"].AsObject<JsonItemStack>(null, Code.Domain);
      if (one != null)
        templates.Add(one);
    }
    else
    {
      var many = Attributes["drops"]
        .AsObject<JsonItemStack[]>(null, Code.Domain);
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

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is not BlockEntityToolMold be
      || be.Shattered
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    // Leave shift-clicks and held-item interactions (chiseling, pouring) to vanilla.
    if (
      byPlayer.Entity.Controls.ShiftKey
      || byPlayer.Entity.Controls.HandUse != EnumHandInteract.None
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (world.Side == EnumAppSide.Client)
      return true;

    bool hasMetal = be.MetalContent != null && be.FillLevel > 0;
    var dropPos = blockSel.Position.ToVec3d().Add(0.5, 0.2, 0.5);

    // 1) Finished casting: hand over the molded item(s); keep the empty mold in place.
    //    Only when the held metal can actually be cast into this mold's tool —
    //    otherwise (e.g. a mold full of slag) fall through to picking the mold up,
    //    rather than letting vanilla's resolver build a bogus "...-slag" stack and
    //    log a "could not resolve" warning.
    if (hasMetal && be.IsHardened && be.IsFull && CanCastInto(world, be))
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
          SmexSounds.Ingot,
          blockSel.Position,
          -0.5,
          byPlayer,
          randomizePitch: false
        );
        return true;
      }
    }

    // 2) Otherwise pick up the mold itself, preserving any remaining contents.
    var moldStack = new ItemStack(this);
    if (hasMetal)
    {
      var beData = new TreeAttribute();
      beData.SetItemstack("contents", be.MetalContent!.Clone());
      beData.SetInt("fillLevel", be.FillLevel);
      beData.SetBool("shattered", false);
      beData.SetFloat("meshAngle", be.MeshAngle);
      moldStack.Attributes["blockEntityAttributes"] = beData;
    }

    if (!byPlayer.InventoryManager.TryGiveItemstack(moldStack))
      world.SpawnItemEntity(moldStack, dropPos);
    world.Logger.Audit(
      "{0} took 1x{1} from tool mold at {2}.",
      byPlayer.PlayerName,
      moldStack.Collectible.Code,
      blockSel.Position
    );

    world.BlockAccessor.SetBlock(0, blockSel.Position);
    if (Sounds?.Place != null)
      world.PlaySoundAt(Sounds.Place, blockSel.Position, -0.5, byPlayer);
    return true;
  }
}
