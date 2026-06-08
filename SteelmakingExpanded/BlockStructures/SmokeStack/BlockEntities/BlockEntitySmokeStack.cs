using System.Text;
using System.Text.Json;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.SmokeStack.BlockEntities;

/// <summary>
/// Block entity for the smoke-stack multiblock. Acts as a gas-network sink:
/// each production tick it consumes exhaust gas from the connected network and
/// vents it as smoke particles.
/// </summary>
/// <summary>
/// Block entity for the smoke-stack multiblock. Registers itself as a gas-network
/// node and vents surplus exhaust from the network to the sky each tick, preventing
/// the network from choking the blast furnace.
/// </summary>
[EntityRegister]
public class BlockEntitySmokeStack
  : BlockEntityMultiblockStructure,
    INetworkNode,
    IGasConsumer
{
  private float _lastConsumedAmount;
  private BlockNetworkModSystem? _system;
  private long _lastVentSoundMs;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _system = api.ModLoader.GetModSystem<BlockNetworkModSystem>();

    // Register this position in the gas network graph so GetNetworkAt(Pos)
    // returns the connected network.  BlockEntityNetworkNode would do this
    // automatically, but this class inherits from BlockEntityMultiblockStructure
    // instead, so we must do it explicitly.
    if (api.Side == EnumAppSide.Server && _system.GetNetworkAt(Pos) == null)
      _system.AddNode(api.World.BlockAccessor, Pos, "pipe");
  }

  public override void OnBlockRemoved()
  {
    // Safety fallback — BlockNetworkNode.OnBlockBroken already calls RemoveNode
    // at break time, but this covers chunk-unload edge cases.
    if (Api?.Side == EnumAppSide.Server)
      _system?.RemoveNode(Api.World.BlockAccessor, Pos);
    base.OnBlockRemoved();
  }

  #region INetworkNode

  /// <inheritdoc/>
  public string NetworkType => "pipe";

  /// <inheritdoc/>
  public string? Orientation { get; set; }

  /// <inheritdoc/>
  public string[] PossibleOrientations { get; set; } = [];

  /// <inheritdoc/>
  public bool HasConnectorAt(BlockFacing face) =>
    face.Code.StartsWith(Orientation ?? "n");

  /// <inheritdoc/>
  public void OnOpenConnectorsChanged(BlockFacing[] openFaces) { }

  /// <summary>
  /// Receives network state broadcasts.  The smoke stack uses the network API
  /// for consumption and does not cache a local state reference.
  /// </summary>
  public void OnNetworkUpdate(object? state) { }

  #endregion

  #region IGasConsumer

  /// <summary>
  /// Consumes up to <paramref name="requestedVolume"/> m³ from the gas network
  /// at this block's position.  Returns the actual amount consumed.
  /// </summary>
  public float TryConsumeGas(float requestedVolume)
  {
    if (_system?.GetNetworkAt(Pos) is not PipeNetwork gasNet)
      return 0f;
    return gasNet.TryConsumeGas(requestedVolume, Api.World.BlockAccessor);
  }

  #endregion

  #region Structure orientation

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
    Lang.Get("smex:smokestack-complete");

  #endregion

  #region Production tick

  protected override void OnProductionTick(float dt)
  {
    if (!StructureComplete)
      return;

    var gasIntakeVolume = SmexValues.SmokestackGasIntakeVolume;

    // Delegates to IGasConsumer; GasNetwork updates state and broadcasts.
    float consumed = TryConsumeGas(gasIntakeVolume);

    if (System.Math.Abs(_lastConsumedAmount - consumed) > 0.001f)
    {
      _lastConsumedAmount = consumed;
      MarkDirty(true);
    }
    else
    {
      _lastConsumedAmount = consumed;
    }

    if (_lastConsumedAmount > 0)
    {
      SpawnSmokeParticles();
      // Soft draught of exhaust venting up the stack.
      SmexSounds.PlayThrottled(
        Api,
        Pos,
        SmexSounds.Fire,
        ref _lastVentSoundMs,
        6000,
        0.3f,
        32f
      );
    }
  }

  private void SpawnSmokeParticles()
  {
    int dx = 0,
      dz = 1;
    if (_currentAngle == 90)
    {
      dx = 1;
      dz = 0;
    }
    else if (_currentAngle == 180)
    {
      dx = 0;
      dz = -1;
    }
    else if (_currentAngle == 270)
    {
      dx = -1;
      dz = 0;
    }

    Vec3d minPos = new(Pos.X + dx + 0.1, Pos.Y + 1.0, Pos.Z + dz + 0.1);
    Vec3d maxPos = new(Pos.X + dx + 0.9, Pos.Y + 13.0, Pos.Z + dz + 0.9);

    var particles = new SimpleParticleProperties(
      _lastConsumedAmount * 15f,
      _lastConsumedAmount * 25f,
      ColorUtil.ToRgba(200, 80, 80, 80),
      minPos,
      maxPos,
      new Vec3f(-0.5f, 1f, -0.5f),
      new Vec3f(0.5f, 3f, 0.5f),
      1.5f,
      3f,
      0.5f,
      1.5f,
      EnumParticleModel.Quad
    )
    {
      OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -200f),
      SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 2),
      GravityEffect = -0.1f,
    };
    Api.World.SpawnParticles(particles);
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
    dsc.AppendLine(
      Lang.Get("smex:smokestack-info-consuming", _lastConsumedAmount)
    );
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetFloat("lastConsumedAmount", _lastConsumedAmount);
    tree.SetString("orientation", Orientation);
    tree.SetString(
      "possibleOrientations",
      JsonSerializer.Serialize(PossibleOrientations)
    );
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _lastConsumedAmount = tree.GetFloat("lastConsumedAmount");
    Orientation = tree.GetString("orientation");
    string? json = tree.GetString("possibleOrientations");
    if (json != null)
      PossibleOrientations = JsonSerializer.Deserialize<string[]>(json) ?? [];
  }

  #endregion
}
