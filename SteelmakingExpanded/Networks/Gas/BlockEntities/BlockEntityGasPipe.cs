using System;
using System.Text;
using BlockNetworkLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas.BlockEntities;

/// <summary>
/// Block entity for all gas-pipe blocks.  Implements <see cref="IGasProducer"/> and
/// <see cref="IGasConsumer"/> by delegating entirely to the owning
/// <see cref="GasNetwork"/> — the BE holds no internal network state and never
/// calls <c>BroadcastUpdate</c> directly.
/// </summary>
public class BlockEntityGasPipe
  : BlockEntityNetworkNode,
    IGasProducer,
    IGasConsumer
{
  public sealed override string NetworkType
  {
    get => "gas";
    set { }
  }

  /// <summary>Local temperature cached from the last network broadcast.</summary>
  public float LocalTemperature { get; protected set; }

  /// <summary>Gas type cached from the last network broadcast.</summary>
  public string GasType { get; protected set; } = "Air";

  // Client-side display data populated from network broadcasts.
  protected float _clientVolume;
  protected float _clientMaxVolume;
  protected string _clientGasType = "Air";
  protected int _openingsCount;
  protected float _clientFlowRate;

  // Throttle-check fields — skip MarkDirty when nothing meaningful changed.
  private float _lastSyncVol = -1;
  private float _lastSyncTemp = -1;
  private string _lastSyncType = "Air";
  private float _lastSyncFlow = -1;

  #region IGasProducer / IGasConsumer

  /// <inheritdoc/>
  public virtual bool TryProduceGas(
    float volume,
    float temperature,
    string gasType = "Air"
  )
  {
    if (NetworkSystem?.GetNetworkAt(Pos) is not GasNetwork gasNet)
      return false;
    return gasNet.TryProduceGas(
      volume,
      temperature,
      gasType,
      Api.World.BlockAccessor
    );
  }

  /// <inheritdoc/>
  public virtual float TryConsumeGas(float requestedVolume)
  {
    if (NetworkSystem?.GetNetworkAt(Pos) is not GasNetwork gasNet)
      return 0f;
    return gasNet.TryConsumeGas(requestedVolume, Api.World.BlockAccessor);
  }

  #endregion

  #region Leak particles

  public override void OnOpenConnectorsChanged(BlockFacing[] openFaces)
  {
    _openingsCount = openFaces.Length;
    if (_openingsCount == 0)
      return;

    foreach (var face in openFaces)
    {
      Vec3d center = new(
        Pos.X + 0.5 + face.Normali.X * 0.5,
        Pos.Y + 0.5 + face.Normali.Y * 0.5,
        Pos.Z + 0.5 + face.Normali.Z * 0.5
      );
      var particles = new SimpleParticleProperties(
        1,
        2,
        ColorUtil.ToRgba(150, 200, 200, 200),
        center.AddCopy(-0.1, -0.1, -0.1),
        center.AddCopy(0.1, 0.1, 0.1),
        new Vec3f(
          face.Normali.X * 1.5f - 0.2f,
          face.Normali.Y * 1.5f - 0.2f,
          face.Normali.Z * 1.5f - 0.2f
        ),
        new Vec3f(
          face.Normali.X * 2.5f + 0.2f,
          face.Normali.Y * 2.5f + 0.2f,
          face.Normali.Z * 2.5f + 0.2f
        ),
        0.5f,
        1f,
        0.2f,
        0.5f,
        EnumParticleModel.Quad
      )
      {
        OpacityEvolve = new EvolvingNatFloat(
          EnumTransformFunction.LINEAR,
          -150f
        ),
        GravityEffect = -0.05f,
      };
      Api.World.SpawnParticles(particles);
    }
  }

  #endregion

  #region Network updates

  public override void OnNetworkUpdate(object? state)
  {
    base.OnNetworkUpdate(state);

    float newTemp = 0;
    float newVol = 0;
    string newGasType = "Air";
    int newOpenings = 0;
    float newFlow = 0;

    if (state is GasNetworkState netState)
    {
      newTemp = netState.SourceTemperature;
      newVol = netState.CurrentVolume;
      newGasType = netState.GasType;
      newOpenings = netState.OpeningsCount;
      newFlow = netState.FlowRate;
    }

    LocalTemperature = newTemp;
    GasType = newGasType;

    if (
      Math.Abs(_lastSyncVol - newVol) > 0.1f
      || Math.Abs(_lastSyncTemp - newTemp) > 1f
      || _lastSyncType != newGasType
      || _openingsCount != newOpenings
      || Math.Abs(_lastSyncFlow - newFlow) > 0.05f
    )
    {
      _clientVolume = newVol;
      _clientMaxVolume = state is GasNetworkState ns ? ns.MaxVolume : 0f;
      _clientGasType = newGasType;
      _openingsCount = newOpenings;
      _clientFlowRate = newFlow;
      _lastSyncVol = newVol;
      _lastSyncTemp = newTemp;
      _lastSyncType = newGasType;
      _lastSyncFlow = newFlow;
      MarkDirty(true);
    }
  }

  #endregion

  #region Persistence hooks

  protected override bool IsNetworkStateMeaningful(object? state) =>
    state is GasNetworkState s && (s.CurrentVolume > 0f || s.FlowRate > 0f);

  protected override object? DeserializeNetworkState(ITreeAttribute tree)
  {
    float vol = tree.GetFloat("gasVol");
    if (vol <= 0f)
      return null;
    return new GasNetworkState
    {
      CurrentVolume = vol,
      MaxVolume = tree.GetFloat("gasMax"),
      SourceTemperature = tree.GetFloat("gasTemp", 20f),
      GasType = tree.GetString("gasType", "Air"),
      OpeningsCount = tree.GetInt("gasOpenings"),
      FlowRate = tree.GetFloat("gasFlow"),
    };
  }

  protected override void SerializeNetworkState(
    ITreeAttribute tree,
    object? state
  )
  {
    if (state is GasNetworkState s)
    {
      tree.SetFloat("gasVol", s.CurrentVolume);
      tree.SetFloat("gasMax", s.MaxVolume);
      tree.SetFloat("gasTemp", s.SourceTemperature);
      tree.SetString("gasType", s.GasType);
      tree.SetInt("gasOpenings", s.OpeningsCount);
      tree.SetFloat("gasFlow", s.FlowRate);
    }
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving); // calls DeserializeNetworkState → _savedNetworkState

    // Populate client display fields from the same tree keys for rendering.
    _clientVolume = tree.GetFloat("gasVol");
    _clientMaxVolume = tree.GetFloat("gasMax");
    LocalTemperature = tree.GetFloat("gasTemp", 20f);
    _clientGasType = tree.GetString("gasType", "Air");
    _openingsCount = tree.GetInt("gasOpenings");
    _clientFlowRate = tree.GetFloat("gasFlow");
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    foreach (var behavior in Behaviors)
    {
      try
      {
        behavior.GetBlockInfo(forPlayer, dsc);
      }
      catch { }
    }

    if (_openingsCount > 0)
      dsc.AppendLine(Lang.Get("smex:gaspipe-info-leaking", _openingsCount));

    // Show throughput (m³/s moving through) rather than fill level: a line being
    // pushed and drained at once carries plenty of gas yet sits near 0 m³ stored,
    // which would otherwise read as "empty".
    if (_clientMaxVolume > 0)
      dsc.AppendLine(
        Lang.Get(
          "smex:gaspipe-info-flow",
          _clientFlowRate,
          _clientGasType,
          LocalTemperature
        )
      );
    else
      dsc.AppendLine(Lang.Get("smex:gaspipe-info-empty"));
  }

  #endregion
}
