using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Networks.Molten.BlockEntities;

/// <summary>
/// Block entity for the molten barrel. Stores up to <see cref="MaxUnitAmount"/> units
/// of a single liquid metal as an <see cref="ILiquidMetalSink"/>, tracks its
/// temperature/hardening, and yields cast or chiselled metal when emptied.
/// </summary>
public class BlockEntityMoltenBarrel : BlockEntity, ILiquidMetalSink
{
  /// <summary>Default capacity in units when the block defines no <c>maxUnits</c> attribute.</summary>
  public const int DefaultMaxUnits = 1200;

  /// <summary>The metal currently stored, or <c>null</c> when empty.</summary>
  public ItemStack? MetalContent;

  /// <summary>Units of metal currently held.</summary>
  public int CurrentUnitAmount = 0;

  /// <summary>Maximum units this barrel can hold.</summary>
  public int MaxUnitAmount = DefaultMaxUnits;

  private MoltenRenderer? _renderer;

  /// <summary>Temperature (°C) of the stored metal, or 0 when empty.</summary>
  public float Temperature =>
    MetalContent?.Collectible.GetTemperature(Api.World, MetalContent) ?? 0f;

  /// <summary>Whether the stored metal has cooled below its liquid threshold.</summary>
  public bool IsHardened =>
    MetalContent != null
    && Temperature
      < 0.3f
        * MetalContent.Collectible.GetMeltingPoint(
          Api.World,
          null,
          new DummySlot(MetalContent)
        );

  /// <summary>Whether the barrel is filled to capacity.</summary>
  public bool IsFull => CurrentUnitAmount >= MaxUnitAmount;

  /// <inheritdoc/>
  public bool CanReceiveAny => !IsFull && (MetalContent == null || !IsHardened);

  /// <inheritdoc/>
  public bool CanReceive(ItemStack metal)
  {
    if (IsFull || IsHardened)
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
  )
  {
    if (IsFull || IsHardened)
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

    if (MetalContent == null)
    {
      MetalContent = metal.Clone();
      MetalContent.ResolveBlockOrItem(Api.World);
      MetalContent.Collectible.SetTemperature(
        Api.World,
        MetalContent,
        temperature,
        delayCooldown: false
      );
      MetalContent.StackSize = 1;
      (MetalContent.Attributes["temperature"] as ITreeAttribute)?.SetFloat(
        "cooldownSpeed",
        40f
      );
    }
    else
    {
      MetalContent.Collectible.SetTemperature(
        Api.World,
        MetalContent,
        temperature,
        delayCooldown: false
      );
    }

    int accepted = Math.Min(amount, MaxUnitAmount - CurrentUnitAmount);
    CurrentUnitAmount += accepted;
    amount -= accepted;
    UpdateRenderer();
    MarkDirty(true);
  }

  /// <inheritdoc/>
  public void OnPourOver()
  {
    MarkDirty(true);
  }

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (Block?.Attributes != null)
      MaxUnitAmount = Block.Attributes["maxUnits"].AsInt(DefaultMaxUnits);

    if (api.Side == EnumAppSide.Client)
    {
      InitRenderer((ICoreClientAPI)api);
      UpdateRenderer();
    }
  }

  private void InitRenderer(ICoreClientAPI capi)
  {
    var quadDefs = Block?.Attributes?[
      "fillQuadsByLevel"
    ]?.AsObject<FillQuadDef[]>();
    Cuboidf[] boxes;
    if (quadDefs != null && quadDefs.Length > 0)
    {
      boxes = new Cuboidf[quadDefs.Length];
      for (int i = 0; i < quadDefs.Length; i++)
        boxes[i] = new Cuboidf(
          quadDefs[i].x1,
          0f,
          quadDefs[i].z1,
          quadDefs[i].x2,
          16f,
          quadDefs[i].z2
        );
    }
    else
      boxes = [new Cuboidf(4f, 0f, 4f, 12f, 16f, 12f)];

    float fillStartY = (Block?.Attributes?["fillStart"]?.AsInt(2) ?? 2) / 16f;
    int fillHeightLevels = Block?.Attributes?["fillHeight"]?.AsInt(8) ?? 8;

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

  private void UpdateRenderer()
  {
    if (_renderer == null)
      return;

    if (MetalContent == null || CurrentUnitAmount <= 0)
    {
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

  public override void OnBlockRemoved()
  {
    _renderer?.Dispose();
    _renderer = null;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    _renderer?.Dispose();
    _renderer = null;
    base.OnBlockUnloaded();
  }

  /// <summary>No-op: the barrel handles interaction in its block, not here.</summary>
  public bool OnPlayerInteract(IPlayer byPlayer) => false;

  /// <summary>Chisels hardened metal out as solid bits (5 units each), emptying the barrel.</summary>
  public bool TryChiselOut(IPlayer byPlayer)
  {
    if (MetalContent == null || CurrentUnitAmount <= 0 || !IsHardened)
      return false;

    if (Api.Side == EnumAppSide.Server)
    {
      int count = Math.Max(1, CurrentUnitAmount / 10);
      var solidLoc = MoltenNetwork.SolidDropLocation(
        MetalContent.Collectible.Code
      );
      var item = Api.World.GetItem(solidLoc);
      if (item != null)
      {
        var drop = new ItemStack(item, count);
        drop.Collectible.SetTemperature(
          Api.World,
          drop,
          Temperature,
          delayCooldown: false
        );
        if (!byPlayer.InventoryManager.TryGiveItemstack(drop))
          Api.World.SpawnItemEntity(drop, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
      }
      MetalContent = null;
      CurrentUnitAmount = 0;
      SmexSounds.Play(Api, Pos, SmexSounds.AnvilHit, 0.8f);
      UpdateRenderer();
      MarkDirty(true);
    }

    return true;
  }

  /// <summary>Resolves the block-defined drop(s) for a full, hardened barrel of <paramref name="fromMetal"/>.</summary>
  public ItemStack[] GetMoldedStacks(ItemStack fromMetal)
  {
    try
    {
      if (Block.Attributes["drop"].Exists)
      {
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
      foreach (var jstack in jstacks)
      {
        var stack = StackFromCode(jstack, fromMetal);
        if (stack != null)
        {
          if (MetalContent != null)
            stack.Collectible.SetTemperature(Api.World, stack, Temperature);
          list.Add(stack);
        }
      }
      return list.ToArray();
    }
    catch (Exception e)
    {
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
  public ItemStack[] GetMetalDrops()
  {
    if (MetalContent == null || CurrentUnitAmount <= 0)
      return [];

    if (IsFull && IsHardened)
      return GetMoldedStacks(MetalContent);

    int count = Math.Max(1, CurrentUnitAmount / 5);
    var solidLoc = MoltenNetwork.SolidDropLocation(
      MetalContent.Collectible.Code
    );
    var item = Api.World.GetItem(solidLoc);
    if (item == null)
      return [];
    var drop = new ItemStack(item, count);
    drop.Collectible.SetTemperature(
      Api.World,
      drop,
      Temperature,
      delayCooldown: false
    );
    return [drop];
  }

  private ItemStack? StackFromCode(JsonItemStack jstack, ItemStack fromMetal)
  {
    jstack.Code.Path = jstack.Code.Path.Replace(
      "{metal}",
      fromMetal.Collectible.LastCodePart()
    );
    jstack.Resolve(Api.World, "molten barrel drop for " + Block.Code);
    return jstack.ResolvedItemstack;
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetItemstack("contents", MetalContent);
    tree.SetInt("currentUnitAmount", CurrentUnitAmount);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolve
  )
  {
    base.FromTreeAttributes(tree, worldForResolve);
    MetalContent = tree.GetItemstack("contents");
    CurrentUnitAmount = tree.GetInt("currentUnitAmount");
    MetalContent?.ResolveBlockOrItem(worldForResolve);
    if (Api?.Side == EnumAppSide.Client)
      UpdateRenderer();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);

    if (MetalContent == null || CurrentUnitAmount <= 0)
    {
      dsc.AppendLine(Lang.Get("smex:moltenbarrel-info-empty", MaxUnitAmount));
      return;
    }

    float meltPoint = MetalContent.Collectible.GetMeltingPoint(
      Api.World,
      null,
      new DummySlot(MetalContent)
    );
    string state = Lang.Get(
      Temperature > 0.8f * meltPoint ? "smex:metalstate-liquid"
      : IsHardened ? "smex:metalstate-hardened"
      : "smex:metalstate-soft"
    );
    string tempStr =
      Temperature < 21f
        ? Lang.Get("smex:metalstate-cold")
        : $"{Temperature:F0}°C";
    dsc.AppendLine(
      Lang.Get(
        "smex:moltenbarrel-info-units-state",
        CurrentUnitAmount,
        MaxUnitAmount,
        state,
        tempStr
      )
    );
  }

  public override void OnStoreCollectibleMappings(
    Dictionary<int, AssetLocation> blockIdMapping,
    Dictionary<int, AssetLocation> itemIdMapping
  )
  {
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
  )
  {
    if (MetalContent != null)
    {
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
