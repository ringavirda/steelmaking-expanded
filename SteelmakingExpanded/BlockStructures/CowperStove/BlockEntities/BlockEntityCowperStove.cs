using System.Text;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Structures.Metalworking.CowperStove.BlockEntities;

/// <summary>
/// Block entity for the cowper-stove multiblock — a regenerative heat exchanger.
/// It absorbs heat from the furnace exhaust into its brick core (faster with a
/// burning coal pile below), then reheats air passing through into hot blast that
/// boosts the blast furnace.
/// </summary>
[EntityRegister]
public class BlockEntityCowperStove : BlockEntityMultiblockStructure
{
  private float _internalTemperature = 20f;
  private string _lastStatus = Lang.Get("smex:cowperstove-status-idle");
  private long _lastHeatSoundMs;

  // Cached config tunables (see SmexValues) — read once at init instead of
  // re-reading the static config every production tick.
  private float _factorAnthracite;
  private float _factorOtherCoal;
  private float _factorDefault;
  private float _coolingSpeedExhaust;
  private float _coolingSpeedAir;
  private float _maxTemperature;

  #region Gas-network registration
  // The cowper intake block is a gas-network node type, but this block entity is
  // a multiblock structure (not a BlockEntityNetworkNode), so nothing registers
  // its position in the gas graph automatically. Do it manually — exactly like
  // BlockEntitySmokeStack — otherwise GetNetworkAt(Pos) is always null and the
  // stove never consumes exhaust (so it never heats up or produces hot blast).

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);

    _factorAnthracite = SmexValues.CowperHeatingSpeedAnthracite;
    _factorOtherCoal = SmexValues.CowperHeatingSpeedOtherCoal;
    _factorDefault = SmexValues.CowperHeatingSpeedDefault;
    _coolingSpeedExhaust = SmexValues.CowperCoolingSpeedExhaust;
    _coolingSpeedAir = SmexValues.CowperCoolingSpeedAir;
    _maxTemperature = SmexValues.CowperMaxTemperature;

    if (api.Side == EnumAppSide.Server)
    {
      var system = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
      if (system.GetNetworkAt(Pos) == null)
        system.AddNode(api.World.BlockAccessor, Pos, "pipe");
    }
  }

  public override void OnBlockRemoved()
  {
    // Safety fallback — BlockNetworkNode.OnBlockBroken already removes the node
    // at break time, but this covers chunk-unload edge cases.
    if (Api?.Side == EnumAppSide.Server)
      Api.ModLoader.GetModSystem<BlockNetworkModSystem>()
        ?.RemoveNode(Api.World.BlockAccessor, Pos);
    base.OnBlockRemoved();
  }

  #endregion

  #region Abstract implementations

  protected override void UpdateStructureRotation()
  {
    if (Block == null)
      return;

    string orientation = Block.Variant["orientation"];
    int angle = orientation switch
    {
      "n" => 0,
      "w" => 90,
      "s" => 180,
      "e" => 270,
      _ => 0,
    };

    if (_structure == null || _currentAngle != angle)
    {
      _structure = Block.Attributes?[
        "multiblockStructure"
      ]?.AsObject<MultiblockStructure>();
      _structure?.InitForUse(angle);
      // The base GetGlobalPos switch applies the *exact* same Y-rotation that
      // MultiblockStructure.InitForUse(angle) uses to lay out (and validate) the
      // structure, so peripheral lookups only line up with the placed blocks when
      // _currentAngle equals the angle handed to InitForUse. The previous
      // (angle + 180) offset rotated every lookup 180° off, so the stove validated
      // as complete but then never found its outlet/passthrough/heatsink blocks.
      _currentAngle = angle;

      if (Api is ICoreClientAPI capi && _highlightedStructure != null)
      {
        _highlightedStructure.ClearHighlights(Api.World, capi.World.Player);
        _highlightedStructure = null;
      }
    }
  }

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("smex:structure-incomplete-count", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("smex:cowperstove-complete");

  #endregion

  #region Gas network helpers
  // The cowper stove is not itself a network node but sits adjacent to the
  // exhaust network.  It queries the network at its own position through the
  // manager and delegates all state changes to the typed GasNetwork.

  /// <summary>
  /// Consumes up to <paramref name="amount"/> m³ from the gas network at this
  /// block's position.  Returns the actual amount consumed.
  /// </summary>
  private float TryConsumeGas(float amount)
  {
    var netManager = Api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    if (netManager.GetNetworkAt(Pos) is not PipeNetwork gasNet)
      return 0f;
    return gasNet.TryConsumeGas(amount, Api.World.BlockAccessor);
  }

  /// <summary>Returns the gas state at this block's position, or <c>null</c>.</summary>
  private PipeNetworkState? GetGasState()
  {
    var netManager = Api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    return (netManager.GetNetworkAt(Pos) as PipeNetwork)?.State;
  }

  #endregion

  #region Production tick

  protected override void OnProductionTick(float dt)
  {
    if (!StructureComplete)
      return;

    var ownGasState = GetGasState();
    float consumedExhaustVol = TryConsumeGas(2.0f);
    float inputExhaustTemp = ownGasState?.SourceTemperature ?? 20f;
    bool isReceivingExhaust = consumedExhaustVol > 0;

    bool isAnthracite = false;
    bool hasOtherCoal = false;

    Block blockBelow = Api.World.BlockAccessor.GetBlock(Pos.DownCopy());
    if (blockBelow.Code?.Path.StartsWith("coalpile") == true)
    {
      if (
        Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy())
          is BlockEntityItemPile pile
        && pile.inventory != null
        && !pile.inventory[0].Empty
      )
      {
        string? path = pile.inventory[0]?.Itemstack?.Collectible?.Code?.Path;
        if (path != null)
        {
          if (path.Contains("anthracite"))
            isAnthracite = true;
          else
            hasOtherCoal = true;
        }
      }
    }

    float airVol = 0f;
    float airTemp = 20f;
    string inGasType = "Air";

    BlockPos passthroughPos = GetGlobalPos(0, 1, 2);
    if (
      Api.World.BlockAccessor.GetBlockEntity(passthroughPos)
      is IGasConsumer passthrough
    )
    {
      airVol = passthrough.TryConsumeGas(2.0f);
      if (
        airVol > 0
        && Api.World.BlockAccessor.GetBlockEntity(passthroughPos)
          is BlockEntityPipe pipe
      )
      {
        airTemp = pipe.LocalTemperature;
        inGasType = pipe.GasType;
      }
    }

    string newStatus = Lang.Get("smex:cowperstove-status-idle");

    if (isReceivingExhaust && airVol > 0)
    {
      newStatus = Lang.Get("smex:cowperstove-status-exhaustmix");
    }
    else if (isReceivingExhaust)
    {
      newStatus = Lang.Get("smex:cowperstove-status-heatingup");
      float tempDiff = inputExhaustTemp - _internalTemperature;
      if (tempDiff > 0)
      {
        float factor = isAnthracite
          ? _factorAnthracite
          : (hasOtherCoal ? _factorOtherCoal : _factorDefault);
        // Heat-transfer rates are per-second; scale by dt for tick-independence.
        _internalTemperature += tempDiff * factor * dt;
        _internalTemperature = System.Math.Min(
          _internalTemperature,
          _maxTemperature
        );
        inputExhaustTemp -= tempDiff * _coolingSpeedExhaust * dt;
      }

      SpawnHeatingParticles();
      // Low roar of the regenerator soaking up furnace exhaust.
      SmexSounds.PlayThrottled(
        Api,
        Pos,
        SmexSounds.Fire,
        ref _lastHeatSoundMs,
        5000,
        0.4f
      );

      BlockPos exhaustOutletPos2 = GetGlobalPos(0, 0, 2);
      if (
        Api.World.BlockAccessor.GetBlockEntity(exhaustOutletPos2)
        is IGasProducer outlet2
      )
        outlet2.TryProduceGas(
          consumedExhaustVol,
          System.Math.Max(20f, inputExhaustTemp * 0.4f),
          "Exhaust"
        );
    }
    else if (airVol > 0)
    {
      newStatus = Lang.Get("smex:cowperstove-status-heating", inGasType);
      float tempDiff = _internalTemperature - airTemp;
      if (tempDiff > 0)
      {
        airTemp = _internalTemperature;
        _internalTemperature -= tempDiff * _coolingSpeedAir * dt;
      }

      BlockPos hotAirOutletPos = GetGlobalPos(0, 1, 0);
      if (
        Api.World.BlockAccessor.GetBlockEntity(hotAirOutletPos)
        is IGasProducer hotOutlet
      )
        hotOutlet.TryProduceGas(airVol, airTemp, inGasType);
    }

    if (_lastStatus != newStatus)
    {
      _lastStatus = newStatus;
      MarkDirty(true);
    }

    UpdateHeatsinks();
  }

  private void SpawnHeatingParticles()
  {
    // Spawn the heat column over the central interior column (structure-local
    // (0, *, 1) — the heatsink stack), rotated the same way GetGlobalPos resolves
    // it. Mirrors InitForUse(_currentAngle) applied to (x:0, z:1).
    var (dx, dz) = _currentAngle switch
    {
      90 => (1, 0), // West
      180 => (0, -1), // South
      270 => (-1, 0), // East
      _ => (0, 1), // North (Default for 0)
    };

    Vec3d minPos = new(Pos.X + dx + 0.1, Pos.Y + 0.1, Pos.Z + dz + 0.1);
    Vec3d maxPos = new(Pos.X + dx + 0.9, Pos.Y + 3.9, Pos.Z + dz + 0.9);

    var particles = new SimpleParticleProperties(
      4,
      8,
      ColorUtil.ToRgba(200, 255, 150, 50),
      minPos,
      maxPos,
      new Vec3f(-0.2f, 0.5f, -0.2f),
      new Vec3f(0.2f, 1.5f, 0.2f),
      1f,
      1f,
      0.2f,
      0.5f,
      EnumParticleModel.Quad
    )
    {
      OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -150f),
      SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.2f),
      GravityEffect = -0.05f,
    };
    Api.World.SpawnParticles(particles);
  }

  private void UpdateHeatsinks()
  {
    for (int y = 0; y <= 3; y++)
    {
      BlockPos hsPos = GetGlobalPos(0, y, 1);
      if (
        Api.World.BlockAccessor.GetBlockEntity(hsPos) is BlockEntityHeatSink hs
        && System.Math.Abs(hs.Temperature - _internalTemperature) > 1f
      )
      {
        hs.Temperature = _internalTemperature;
        hs.MarkDirty(true);
      }
    }
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    if (!StructureComplete)
    {
      dsc.AppendLine(Lang.Get("smex:structure-incomplete"));
      return;
    }
    dsc.AppendLine(Lang.Get("smex:cowperstove-info-status", _lastStatus));
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetFloat("internalTemperature", _internalTemperature);
    tree.SetString("lastStatus", _lastStatus);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _internalTemperature = tree.GetFloat("internalTemperature");
    _lastStatus = tree.GetString(
      "lastStatus",
      Lang.Get("smex:cowperstove-status-idle")
    );
  }

  #endregion
}
