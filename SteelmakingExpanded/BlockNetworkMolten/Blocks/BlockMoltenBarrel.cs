using System;
using System.Collections.Generic;
using System.Text;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SteelmakingExpanded.BlockNetworkMolten.Blocks;

/// <summary>
/// A portable barrel that stores liquid metal. Poured into from a canal tap (or a
/// crucible), it renders a glowing fill level; once full and hardened the metal can
/// be chiselled out. Ctrl + right-click picks the barrel up with its contents.
/// </summary>
[EntityRegister]
public class BlockMoltenBarrel : Block
{
  private MeshData? _barrelBaseMesh;

  // Cached item lists for the interaction help, populated on load.
  private ItemStack[] _smeltedCrucibles = [];
  private ItemStack[] _chisels = [];

  public override void OnLoaded(ICoreAPI api)
  {
    base.OnLoaded(api);

    // Cache all smelted crucibles
    var crucibleList = new List<ItemStack>();
    foreach (var block in api.World.Blocks)
    {
      if (
        block.Code != null
        && block.Code.Path.StartsWith("crucible-")
        && block.Code.Path.EndsWith("-smelted")
      )
      {
        crucibleList.Add(new ItemStack(block));
      }
    }
    _smeltedCrucibles = crucibleList.ToArray();

    var chiselList = new List<ItemStack>();
    foreach (var item in api.World.Items)
    {
      if (item.Tool == EnumTool.Chisel)
      {
        chiselList.Add(new ItemStack(item));
      }
    }
    _chisels = chiselList.ToArray();
  }

  /// <summary>
  /// Emits incandescent block light scaled to the stored metal's temperature, so a
  /// hot barrel lights its surroundings (same scheme as the canals and the cowper
  /// heat sink). The block entity owns the threshold/scaling via
  /// <see cref="BlockEntityMoltenBarrel.GlowLightLevel"/> and re-lights the block as
  /// that level shifts. Held/inventory barrels (null pos) fall back to base and glow
  /// via <see cref="OnBeforeRender"/> instead.
  /// </summary>
  public override byte[] GetLightHsv(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    ItemStack? stack = null
  )
  {
    if (
      pos != null
      && blockAccessor.GetBlockEntity(pos) is BlockEntityMoltenBarrel be
    )
    {
      byte val = be.GlowLightLevel;
      if (val > 0)
        return [8, 7, val];
    }
    return base.GetLightHsv(blockAccessor, pos, stack);
  }

  #region Held / inventory rendering
  public override void OnBeforeRender(
    ICoreClientAPI capi,
    ItemStack itemstack,
    EnumItemRenderTarget target,
    ref ItemRenderInfo renderinfo
  )
  {
    base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

    var (metal, units) = MoltenContents.Read(
      itemstack,
      MoltenContents.BarrelUnitsKey,
      capi.World
    );
    if (units <= 0 || metal?.Collectible == null)
      return;

    int maxUnits =
      Attributes?["maxUnits"].AsInt(SmexValues.BarrelDefaultMaxUnits)
      ?? SmexValues.BarrelDefaultMaxUnits;
    float fillRatio =
      maxUnits > 0 ? GameMath.Clamp((float)units / maxUnits, 0f, 1f) : 0f;
    float temp = metal.Collectible.GetTemperature(capi.World, metal);
    int glow = (int)GameMath.Clamp((temp - 550f) / 2f, 0f, 255f);

    var cache = GetMeshRefCache(capi);
    int fillStep = (int)GameMath.Clamp(fillRatio * 16f, 0f, 16f);
    string key = $"{metal.Collectible.Code}|{fillStep}|{glow / 16}";
    if (!cache.TryGetValue(key, out var meshRef))
    {
      MeshData mesh = GenMeshWithContent(capi, metal, fillRatio, glow);
      meshRef = cache[key] = capi.Render.UploadMultiTextureMesh(mesh);
    }
    renderinfo.ModelRef = meshRef;
  }

  private Dictionary<string, MultiTextureMeshRef> GetMeshRefCache(
    ICoreClientAPI capi
  )
  {
    string cacheKey = "moltenBarrelMeshRefs:" + Code;
    if (
      capi.ObjectCache.TryGetValue(cacheKey, out var existing)
      && existing is Dictionary<string, MultiTextureMeshRef> dict
    )
      return dict;
    var created = new Dictionary<string, MultiTextureMeshRef>();
    capi.ObjectCache[cacheKey] = created;
    return created;
  }

  private MeshData GenMeshWithContent(
    ICoreClientAPI capi,
    ItemStack metal,
    float fillRatio,
    int glow
  )
  {
    _barrelBaseMesh ??= TesselateBaseMesh(capi);
    MeshData combined = _barrelBaseMesh.Clone();

    Cuboidf[] boxes = FillQuads.ReadBoxes(
      this,
      "fillQuadsByLevel",
      new Cuboidf(4f, 0f, 4f, 12f, 16f, 12f)
    );
    float fillStartY = FillQuads.ReadStartY(this, "fillStart", 2f);
    float fillHeightLevels = FillQuads.ReadHeightLevels(this, "fillHeight", 8f);
    float yLevel = fillStartY + fillRatio * fillHeightLevels / 16f;

    var tex = metal.Item?.FirstTexture ?? metal.Block?.FirstTextureInventory;
    if (tex == null)
      return combined;
    capi.BlockTextureAtlas.GetOrInsertTexture(
      tex,
      out _,
      out TextureAtlasPosition texPos,
      0.005f
    );
    if (texPos == null)
      return combined;

    foreach (Cuboidf box in boxes)
    {
      MeshData quad = QuadMeshUtil.GetQuad();
      quad.Rgba = new byte[16];
      quad.Rgba.Fill(byte.MaxValue);
      quad.Flags = new int[4];
      for (int i = 0; i < 4; i++)
        quad.Flags[i] = glow & VertexFlags.GlowLevelBitMask;

      quad.Uv =
      [
        texPos.x1,
        texPos.y1,
        texPos.x2,
        texPos.y1,
        texPos.x2,
        texPos.y2,
        texPos.x1,
        texPos.y2,
      ];
      quad.TextureIds = [texPos.atlasTextureId];
      quad.TextureIndices = [0];
      quad.TextureIndicesCount = 1;

      float[] matrix = new Matrixf()
        .Translate((box.X1 + box.X2) / 32f, yLevel, (box.Z1 + box.Z2) / 32f)
        .RotateX((float)Math.PI / 2f)
        .Scale((box.X2 - box.X1) / 32f, (box.Z2 - box.Z1) / 32f, 1f)
        .Values;
      quad.MatrixTransform(matrix);
      combined.AddMeshData(quad);
    }

    return combined;
  }

  private MeshData TesselateBaseMesh(ICoreClientAPI capi)
  {
    capi.Tesselator.TesselateBlock(this, out MeshData mesh);
    return mesh;
  }

  public override void OnUnloaded(ICoreAPI api)
  {
    if (api is ICoreClientAPI capi)
    {
      string cacheKey = "moltenBarrelMeshRefs:" + Code;
      if (
        capi.ObjectCache.TryGetValue(cacheKey, out var existing)
        && existing is Dictionary<string, MultiTextureMeshRef> dict
      )
      {
        foreach (var meshRef in dict.Values)
          meshRef.Dispose();
        capi.ObjectCache.Remove(cacheKey);
      }
    }
    base.OnUnloaded(api);
  }
  #endregion

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityMoltenBarrel be
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    var heldItem = byPlayer
      .InventoryManager
      .ActiveHotbarSlot
      ?.Itemstack
      ?.Collectible;
    if (heldItem?.Tool == EnumTool.Chisel)
    {
      var offhandItem = byPlayer
        .Entity
        .LeftHandItemSlot
        ?.Itemstack
        ?.Collectible;
      if (offhandItem?.Tool != EnumTool.Hammer)
        return false;

      if (world.Side == EnumAppSide.Client)
        return true;

      return be.TryChiselOut(byPlayer);
    }

    if (byPlayer.Entity.Controls.CtrlKey)
    {
      if (world.Side == EnumAppSide.Client)
        return true;

      var stack = new ItemStack(this);
      MoltenContents.Write(
        stack,
        MoltenContents.BarrelUnitsKey,
        be.MetalContent,
        be.CurrentUnitAmount
      );
      if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
        world.SpawnItemEntity(
          stack,
          blockSel.Position.ToVec3d().Add(0.5, 1.0, 0.5)
        );
      world.BlockAccessor.SetBlock(0, blockSel.Position);
      return true;
    }

    return be.OnPlayerInteract(byPlayer);
  }

  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack byItemStack
  )
  {
    base.OnBlockPlaced(world, blockPos, byItemStack);

    if (byItemStack == null)
      return;

    if (
      world.BlockAccessor.GetBlockEntity(blockPos) is BlockEntityMoltenBarrel be
    )
    {
      // Assign fields directly instead of FromTreeAttributes: the base
      // deserializer rebuilds Pos from posx/posy/posz, which this partial tree
      // lacks, corrupting the block entity position to (0,0,0) on reload.
      (be.MetalContent, be.CurrentUnitAmount) = MoltenContents.Read(
        byItemStack,
        MoltenContents.BarrelUnitsKey,
        world
      );
      be.MarkDirty(true);
    }
  }

  public override void GetHeldItemInfo(
    ItemSlot inSlot,
    StringBuilder dsc,
    IWorldAccessor world,
    bool withDebugInfo
  )
  {
    base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

    if (inSlot.Itemstack?.Attributes?["blockEntityAttributes"] == null)
      return;

    var (metalContent, currentUnits) = MoltenContents.Read(
      inSlot.Itemstack,
      MoltenContents.BarrelUnitsKey,
      world
    );
    int maxUnits =
      Attributes?["maxUnits"].AsInt(SmexValues.BarrelDefaultMaxUnits)
      ?? SmexValues.BarrelDefaultMaxUnits;

    if (currentUnits <= 0)
    {
      dsc.AppendLine(Lang.Get("smex:moltenbarrel-info-empty", maxUnits));
      return;
    }

    if (metalContent == null)
    {
      dsc.AppendLine(
        Lang.Get("smex:moltenbarrel-info-units", currentUnits, maxUnits)
      );
      return;
    }

    string state = Lang.Get(
      MoltenMetal.StateOf(world, metalContent) switch
      {
        MoltenState.Liquid => "smex:metalstate-liquid",
        MoltenState.Hardened => "smex:metalstate-hardened",
        _ => "smex:metalstate-soft",
      }
    );

    dsc.AppendLine(
      Lang.Get(
        "smex:moltenbarrel-info-content",
        currentUnits,
        maxUnits,
        MoltenMetal.DisplayName(metalContent.Collectible.Code.ToString()),
        state,
        MoltenMetal.FormatTemperature(
          MoltenMetal.GetTemperature(world, metalContent)
        )
      )
    );
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var interactions =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    var be =
      world.BlockAccessor.GetBlockEntity(selection.Position)
      as BlockEntityMoltenBarrel;
    bool isHardened = be?.IsHardened ?? false;
    bool isFull = be?.IsFull ?? false;

    var result = new List<WorldInteraction>(interactions)
    {
      new()
      {
        ActionLangCode = "smex:blockhelp-barrel-pickup",
        MouseButton = EnumMouseButton.Right,
        HotKeyCode = "ctrl",
      },
    };

    if (!isHardened && !isFull)
    {
      result.Add(
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-barrel-pour",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = _smeltedCrucibles,
        }
      );
    }

    if (isHardened)
    {
      result.Add(
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-barrel-chisel",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = _chisels,
        }
      );
    }

    return result.ToArray();
  }

  public override bool CanBePlacedInto(ItemStack stack, ItemSlot slot)
  {
    return slot.Inventory?.ClassName == "backpack";
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1
  )
  {
    // Molten metal hits the ground and sizzles when a still-liquid barrel breaks.
    if (
      world.Side == EnumAppSide.Server
      && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenBarrel be
      && be.MetalContent != null
      && be.CurrentUnitAmount > 0
      && !be.IsHardened
    )
      world.PlaySoundAt(
        ExSounds.Sizzle,
        pos.X + 0.5,
        pos.Y + 0.5,
        pos.Z + 0.5,
        null,
        true,
        24f
      );

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    var drops = new List<ItemStack> { new ItemStack(this) };

    if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenBarrel be)
      drops.AddRange(be.GetMetalDrops());

    return drops.ToArray();
  }
}
