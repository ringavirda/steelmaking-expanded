using System;
using System.Collections.Generic;
using System.Text;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockNetworkMolten.BlockEntities;

/// <summary>
/// Block entity for the molten barrel. Stores up to <see cref="MaxUnitAmount"/> units
/// of a single liquid metal as an <see cref="ILiquidMetalSink"/>, tracks its
/// temperature/hardening, and yields cast or chiselled metal when emptied.
/// </summary>
[BlockEntityRegister]
public class BlockEntityMoltenBarrel
  : BlockEntity,
    ILiquidMetalSink,
    IChiselableMolten {
  /// <summary>The metal currently stored, or <c>null</c> when empty.</summary>
  public ItemStack? MetalContent;

  /// <summary>Units of metal currently held.</summary>
  public int CurrentUnitAmount = 0;

  /// <summary>Maximum units this barrel can hold. Baked at compile time from the block JSON's
  /// <c>maxUnits</c> attribute by the attribute source generator - no runtime JSON read.</summary>
  public int MaxUnitAmount = BlockMoltenBarrel.MaxUnits;

  private MoltenRenderer? _renderer;

  // Cooldown rate for the stored metal: the molten-system base scaled by the barrel's coefficient
  // (mirrors the converter's charge cooldown). Read live so a config change applies immediately.
  private static float ContentCooldownSpeed =>
    SmexValues.MoltenCooldownSpeed * SmexValues.BarrelCooldownCoefficient;

  /// <summary>Temperature (°C) of the stored metal, or 0 when empty.</summary>
  public float Temperature =>
    MetalContent?.Collectible.GetTemperature(Api.World, MetalContent) ?? 0f;

  /// <summary>Whether the stored metal has cooled below its liquid threshold.</summary>
  public bool IsHardened =>
    MetalContent != null && MoltenMetal.IsHardened(Api.World, MetalContent);

  /// <summary>Whether the barrel is filled to capacity.</summary>
  public bool IsFull => CurrentUnitAmount >= MaxUnitAmount;

  #region Incandescent block light
  private byte _lastGlow;

  /// <summary>
  /// Block-light value (0-24) emitted from the hot metal (the shared
  /// <see cref="MoltenMetal.GlowLevel"/> scale). Read by
  /// <see cref="Blocks.BlockMoltenBarrel.GetLightHsv"/>; 0 when empty or cool.
  /// </summary>
  public byte GlowLightLevel {
    get {
      if (Api?.World == null || MetalContent == null || CurrentUnitAmount <= 0)
        return 0;
      return MoltenMetal.GlowLevel(
        MoltenMetal.GetTemperature(Api.World, MetalContent)
      );
    }
  }

  /// <summary>
  /// Re-lights the block via <c>MarkBlockDirty</c> when the glow level shifts (the block id never
  /// changes, so the engine won't on its own). Driven by a dedicated tick since the barrel has no other.
  /// </summary>
  private void UpdateGlow() {
    byte g = GlowLightLevel;
    if (g != _lastGlow) {
      _lastGlow = g;
      Api?.World.BlockAccessor.MarkBlockDirty(Pos);
    }
  }
  #endregion

  /// <inheritdoc/>
  // Hardened contents are allowed - fresh molten metal of the same type re-melts
  // and tops up the barrel rather than being rejected.
  public bool CanReceiveAny => !IsFull;

  /// <inheritdoc/>
  public bool CanReceive(ItemStack metal) {
    if (IsFull)
      return false;
    if (
      MetalContent != null
      && !MetalContent.Collectible.Equals(
        MetalContent,
        metal,
        GlobalConstants.IgnoredStackAttributes
      )
    )
      return false;
    var stacks = GetMoldedStacks(metal);
    return stacks is { Length: > 0 };
  }

  /// <inheritdoc/>
  public void BeginFill(Vec3d hitPosition) { }

  /// <inheritdoc/>
  public void ReceiveLiquidMetal(
    ItemStack metal,
    ref int amount,
    float temperature
  ) {
    if (IsFull)
      return;
    if (
      MetalContent != null
      && !MetalContent.Collectible.Equals(
        MetalContent,
        metal,
        GlobalConstants.IgnoredStackAttributes
      )
    )
      return;

    if (MetalContent == null) {
      MetalContent = metal.Clone();
      MetalContent.ResolveBlockOrItem(Api.World);
      MoltenMetal.SetTemperature(Api.World, MetalContent, temperature);
      MetalContent.StackSize = 1;
      MoltenMetal.SetCooldownSpeed(MetalContent, ContentCooldownSpeed);
    } else {
      MoltenMetal.SetTemperature(Api.World, MetalContent, temperature);
    }

    int accepted = Math.Min(amount, MaxUnitAmount - CurrentUnitAmount);
    CurrentUnitAmount += accepted;
    amount -= accepted;
    UpdateRenderer();
    UpdateGlow();
    MarkDirty(true);
  }

  /// <inheritdoc/>
  public void OnPourOver() {
    MarkDirty(true);
  }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    // MaxUnitAmount is the generated BlockMoltenBarrel.MaxUnits const (from the JSON) - no read here.

    if (api.Side == EnumAppSide.Client) {
      InitRenderer((ICoreClientAPI)api);
      UpdateRenderer();
      // Metal cools after the last broadcast (the barrel only syncs on fill/chisel), so refresh
      // the surface glow from the stack's live temperature or it snaps cold on interaction.
      RegisterGameTickListener(_ => UpdateRenderer(), 1000);
    } else {
      // No other server tick exists, so drive the cooling glow fade from here.
      RegisterGameTickListener(_ => OnServerTick(), 1000);
    }
  }

  // Server tick: keep the stored metal's cooldown rate in step with the live config so a
  // `/exmod config smex MoltenCooldownSpeed ...` change affects metal already in the barrel,
  // then fade the incandescent glow as it cools.
  private void OnServerTick() {
    if (MetalContent != null && CurrentUnitAmount > 0)
      MoltenMetal.SyncCooldownSpeed(
        Api.World,
        MetalContent,
        ContentCooldownSpeed
      );
    UpdateGlow();
  }

  private void InitRenderer(ICoreClientAPI capi) {
    var barrel = (BlockMoltenBarrel)Block;
    Cuboidf[] boxes = FillQuads.BoxesFrom(
      barrel.FillQuadsByLevel,
      new Cuboidf(4f, 0f, 4f, 12f, 16f, 12f)
    );
    float fillStartY = BlockMoltenBarrel.FillStart / 16f;
    float fillHeightLevels = BlockMoltenBarrel.FillHeight;

    _renderer = new MoltenRenderer(
      Pos,
      capi,
      boxes,
      0f,
      fillStartY,
      fillHeightLevels
    );
    capi.Event.RegisterRenderer(_renderer, EnumRenderStage.Opaque);
  }

  private void UpdateRenderer() {
    if (_renderer == null)
      return;

    if (MetalContent == null || CurrentUnitAmount <= 0) {
      _renderer.FillRatio = 0f;
      return;
    }

    _renderer.FillRatio =
      MaxUnitAmount > 0 ? (float)CurrentUnitAmount / MaxUnitAmount : 0f;
    _renderer.Temperature = MetalContent.Collectible.GetTemperature(
      Api.World,
      MetalContent
    );
    _renderer.MetalStack = MetalContent;
  }

  public override void OnBlockRemoved() {
    _renderer?.Dispose();
    _renderer = null;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    _renderer?.Dispose();
    _renderer = null;
    base.OnBlockUnloaded();
  }

  /// <summary>No-op: the barrel handles interaction in its block, not here.</summary>
  public bool OnPlayerInteract(IPlayer byPlayer) => false;

  #region Chisel-out (IChiselableMolten)

  // The barrel is chiselable once its stored metal hardens; there's no size cap, so the hardened state
  // is both the claim gate and the can-chisel gate (and there is no "too hot" feedback - a chisel + hammer
  // click on a still-soft barrel falls through to its other interactions). The shared MoltenChisel ritual
  // (give/spawn, sound) runs from the block; here we only clear and recover, at the shared
  // MoltenUnitsPerBit rate.
  bool IChiselableMolten.HasChiselableContent =>
    MetalContent != null && CurrentUnitAmount > 0 && IsHardened;

  bool IChiselableMolten.CanChiselOut =>
    ((IChiselableMolten)this).HasChiselableContent;

  string? IChiselableMolten.ChiselBlockedError => null;

  /// <summary>Server-side: empties the barrel and returns the recovered metal bits (10 units each).</summary>
  public ItemStack? ChiselOut() {
    if (
      Api?.Side != EnumAppSide.Server
      || MetalContent == null
      || CurrentUnitAmount <= 0
      || !IsHardened
    )
      return null;

    ItemStack? recovered = MoltenChisel.BuildRecovery(
      Api.World,
      MetalContent.Collectible.Code,
      Temperature,
      CurrentUnitAmount
    );
    MetalContent = null;
    CurrentUnitAmount = 0;
    UpdateRenderer();
    UpdateGlow();
    MarkDirty(true);
    return recovered;
  }

  #endregion

  /// <summary>Resolves the block-defined drop(s) for a full, hardened barrel of <paramref name="fromMetal"/>.</summary>
  public ItemStack[] GetMoldedStacks(ItemStack fromMetal) {
    try {
      if (Block.Attributes["drop"].Exists) {
        var jstack = Block
          .Attributes["drop"]
          .AsObject<JsonItemStack>(null, Block.Code.Domain);
        if (jstack == null)
          return Array.Empty<ItemStack>();
        var stack = StackFromCode(jstack, fromMetal);
        if (stack == null)
          return Array.Empty<ItemStack>();
        if (MetalContent != null)
          stack.Collectible.SetTemperature(Api.World, stack, Temperature);
        return [stack];
      }

      var jstacks = Block
        .Attributes["drops"]
        .AsObject<JsonItemStack[]>(null, Block.Code.Domain);
      if (jstacks == null)
        return Array.Empty<ItemStack>();
      var list = new List<ItemStack>();
      foreach (var jstack in jstacks) {
        var stack = StackFromCode(jstack, fromMetal);
        if (stack != null) {
          if (MetalContent != null)
            stack.Collectible.SetTemperature(Api.World, stack, Temperature);
          list.Add(stack);
        }
      }
      return list.ToArray();
    } catch (Exception e) {
      Api.World.Logger.Error(
        "Failed to parse drop/drops attribute for molten barrel {0}: {1}",
        Block.Code,
        e.Message
      );
      throw;
    }
  }

  /// <summary>
  /// Returns the drops for the metal inside the barrel when it is broken: the
  /// block-defined drop(s) when full and hardened, otherwise metal bits at 5 units each.
  /// </summary>
  public ItemStack[] GetMetalDrops() {
    if (MetalContent == null || CurrentUnitAmount <= 0)
      return [];

    if (IsFull && IsHardened)
      return GetMoldedStacks(MetalContent);

    // Breaking a not-yet-cast barrel scatters metal bits at 5 units each (vs the chisel-out's 10).
    ItemStack? drop = MoltenChisel.BuildRecovery(
      Api.World,
      MetalContent.Collectible.Code,
      Temperature,
      CurrentUnitAmount
    );
    return drop != null ? [drop] : [];
  }

  private ItemStack? StackFromCode(JsonItemStack jstack, ItemStack fromMetal) {
    jstack.Code.Path = jstack.Code.Path.Replace(
      "{metal}",
      fromMetal.Collectible.LastCodePart()
    );
    jstack.Resolve(Api.World, "molten barrel drop for " + Block.Code);
    return jstack.ResolvedItemstack;
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetItemstack("contents", MetalContent);
    tree.SetInt("currentUnitAmount", CurrentUnitAmount);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolve
  ) {
    base.FromTreeAttributes(tree, worldForResolve);
    MetalContent = tree.GetItemstack("contents");
    CurrentUnitAmount = tree.GetInt("currentUnitAmount");
    MetalContent?.ResolveBlockOrItem(worldForResolve);
    if (Api?.Side == EnumAppSide.Client) {
      UpdateRenderer();
      UpdateGlow();
    }
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);

    if (MetalContent == null || CurrentUnitAmount <= 0) {
      dsc.AppendLine(Lang.Get("smex:moltenbarrel-info-empty", MaxUnitAmount));
      return;
    }

    // The barrel labels its in-between state "soft" (re-meltable) rather than "cooling".
    string state = Lang.Get(
      MoltenMetal.StateOf(Api.World, MetalContent) switch {
        MoltenState.Liquid => "smex:metalstate-liquid",
        MoltenState.Hardened => "smex:metalstate-hardened",
        _ => "smex:metalstate-soft",
      }
    );
    dsc.AppendLine(
      Lang.Get(
        "smex:moltenbarrel-info-units-state",
        CurrentUnitAmount,
        MaxUnitAmount,
        state,
        MoltenMetal.FormatTemperature(Temperature)
      )
    );
  }

  public override void OnStoreCollectibleMappings(
    Dictionary<int, AssetLocation> blockIdMapping,
    Dictionary<int, AssetLocation> itemIdMapping
  ) {
    MetalContent?.Collectible.OnStoreCollectibleMappings(
      Api.World,
      new DummySlot(MetalContent),
      blockIdMapping,
      itemIdMapping
    );
  }

  public override void OnLoadCollectibleMappings(
    IWorldAccessor worldForResolve,
    Dictionary<int, AssetLocation> oldBlockIdMapping,
    Dictionary<int, AssetLocation> oldItemIdMapping,
    int schematicSeed,
    bool resolveImports
  ) {
    if (MetalContent != null) {
      MetalContent.FixMapping(
        oldBlockIdMapping,
        oldItemIdMapping,
        worldForResolve
      );
      var tempTree = MetalContent.Attributes["temperature"] as ITreeAttribute;
      if (tempTree?.HasAttribute("temperatureLastUpdate") == true)
        tempTree.SetDouble(
          "temperatureLastUpdate",
          worldForResolve.Calendar.TotalHours
        );
    }
  }
}
