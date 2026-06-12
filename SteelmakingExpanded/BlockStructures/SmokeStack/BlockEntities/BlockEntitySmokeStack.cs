using System.Text;
using System.Text.Json;
using ExpandedLib;
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
    IPipeNode
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

  #region IPipeNode

  /// <summary>
  /// Injects gas into the network at this block's position. The smoke stack only ever
  /// vents the network, so this delegates to the network for completeness but is unused.
  /// </summary>
  public bool TryProduce(
    float volume,
    float temperature,
    string gasType = "Air",
    float maxOutputPressure = 1.0f,
    bool bypassLeakCap = false
  )
  {
    if (_system?.GetNetworkAt(Pos) is not PipeNetwork gasNet)
      return false;
    return gasNet.TryProduceGas(
      volume,
      temperature,
      gasType,
      Api.World.BlockAccessor,
      maxOutputPressure: maxOutputPressure,
      bypassLeakCap: bypassLeakCap
    );
  }

  /// <summary>
  /// Consumes up to <paramref name="requestedVolume"/> litres from the gas network
  /// at this block's position.  Returns the actual amount consumed.
  /// </summary>
  public float TryConsume(float requestedVolume)
  {
    if (_system?.GetNetworkAt(Pos) is not PipeNetwork gasNet)
      return 0f;
    return gasNet.TryConsumeGas(requestedVolume, Api.World.BlockAccessor);
  }

  /// <inheritdoc/>
  public float Temperature =>
    _system?.GetNetworkAt(Pos) is PipeNetwork gasNet
      ? gasNet.State?.Temperature ?? 20f
      : 20f;

  /// <inheritdoc/>
  public string Medium =>
    _system?.GetNetworkAt(Pos) is PipeNetwork gasNet
      ? gasNet.State?.MediumType ?? ""
      : "";

  /// <inheritdoc/>
  public bool IsLiquid => Medium == "Water";

  /// <inheritdoc/>
  public float Pressure =>
    _system?.GetNetworkAt(Pos) is PipeNetwork gasNet
      ? gasNet.State?.Pressure ?? 0f
      : 0f;

  /// <inheritdoc/>
  public float Volume =>
    _system?.GetNetworkAt(Pos) is PipeNetwork gasNet
      ? gasNet.State?.Volume ?? 0f
      : 0f;

  /// <inheritdoc/>
  public float MaxVolume =>
    _system?.GetNetworkAt(Pos) is PipeNetwork gasNet
      ? gasNet.State?.MaxVolume ?? 0f
      : 0f;

  #endregion

  #region Structure orientation

  protected override void UpdateStructureRotation()
  {
    if (Block == null)
      return;

    // Single-letter orientation codes share the side-angle convention.
    SetStructureAngle(
      ExOrientation.AngleFromSide(Block.Variant["orientation"])
    );
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

    // Read the medium before drawing — TryConsume can empty the pool and clear its label.
    // Water is never pulled: TryConsume refuses a liquid run, so this only ever vents
    // exhaust, steam or plain air.
    string medium = Medium;

    // Delegates to IGasConsumer; GasNetwork updates state and broadcasts.
    float consumed = TryConsume(gasIntakeVolume);

    if (System.Math.Abs(_lastConsumedAmount - consumed) > 0.001f)
    {
      _lastConsumedAmount = consumed;
      MarkDirty(true);
    }
    else
    {
      _lastConsumedAmount = consumed;
    }

    if (_lastConsumedAmount <= 0)
      return;

    SpawnSmokeParticles(medium);
    // Soft draught of exhaust venting up the stack.
    ExSounds.PlayThrottled(
      Api,
      Pos,
      ExSounds.Fire,
      ref _lastVentSoundMs,
      6000,
      0.3f,
      32f
    );
  }

  private void SpawnSmokeParticles(string medium)
  {
    // Colour the plume by what's venting: dark soot for exhaust, white vapour for steam,
    // and nothing at all for plain air.
    if (ExParticles.GasColor(medium, ventAir: false) is not int color)
      return;

    // The smoke column sits over the cell in front of the stack — structure-local
    // (0, *, 1) rotated by the placed orientation, same as GetGlobalPos resolves it.
    Vec3i d = ExOrientation.RotateOffset(0, 0, 1, _currentAngle);
    int dx = d.X,
      dz = d.Z;

    Vec3d minPos = new(Pos.X + dx + 0.1, Pos.Y + 1.0, Pos.Z + dz + 0.1);
    Vec3d maxPos = new(Pos.X + dx + 0.9, Pos.Y + 13.0, Pos.Z + dz + 0.9);

    ExParticles.RisingPlume(
      Api.World,
      color,
      minPos,
      maxPos,
      new Vec3f(-0.5f, 1f, -0.5f),
      new Vec3f(0.5f, 3f, 0.5f),
      _lastConsumedAmount * 15f,
      _lastConsumedAmount * 25f,
      1.5f,
      -0.1f,
      0.5f,
      1.5f,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -200f),
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, 2)
    );
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
