using System;
using System.Text;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// Block entity for all gas-pipe blocks.  Implements <see cref="IGasProducer"/> and
/// <see cref="IGasConsumer"/> by delegating entirely to the owning
/// <see cref="PipeNetwork"/> — the BE holds no internal network state and never
/// calls <c>BroadcastUpdate</c> directly.
/// </summary>
[EntityRegister]
public class BlockEntityPipe
  : BlockEntityNetworkNode,
    IGasProducer,
    IGasConsumer
{
  public sealed override string NetworkType
  {
    get => "pipe";
    set { }
  }

  /// <summary>Local temperature cached from the last network broadcast.</summary>
  public float LocalTemperature { get; protected set; }

  /// <summary>Gas type cached from the last network broadcast.</summary>
  public string GasType { get; protected set; } = "Air";

  /// <summary>Gas pressure (atm) cached from the last network broadcast.</summary>
  public float Pressure { get; protected set; }

  /// <summary>Temperature (°C) at or above which a pipe begins to glow.</summary>
  private const float GlowMinTemp = 250f;

  /// <summary>
  /// Block-light value (0–24) this pipe emits from its local content temperature.
  /// Ferric pipes glow dimmer (<see cref="PpexValues.FerricGlowFactor"/>); copper /
  /// bronze / brass glow directly from temperature. Read by
  /// <see cref="Blocks.BlockPipe.GetLightHsv"/>; 0 when cool.
  /// </summary>
  public byte GlowLightLevel
  {
    get
    {
      if (Block is not Blocks.BlockPipe pipe || LocalTemperature <= GlowMinTemp)
        return 0;
      float glowTemp = pipe.IsFerric
        ? LocalTemperature * PpexValues.FerricGlowFactor
        : LocalTemperature;
      return (byte)Math.Clamp((int)((glowTemp - GlowMinTemp) / 40f), 0, 24);
    }
  }

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
    if (NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork gasNet)
      return false;
    return gasNet.TryProduceGas(
      volume,
      temperature,
      gasType,
      Api.World.BlockAccessor,
      sourcePos: Pos
    );
  }

  /// <inheritdoc/>
  public virtual float TryConsumeGas(float requestedVolume)
  {
    if (NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork gasNet)
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

  /// <summary>
  /// Sprays water out of each open-ended connector while the liquid pool is leaking —
  /// falling blue droplets plus an occasional splash sound, mirroring a poured bucket.
  /// Called from the network tick (server) so the particles broadcast to clients.
  /// </summary>
  public void SpawnLiquidLeak(BlockFacing[] openFaces)
  {
    if (openFaces.Length == 0)
      return;

    foreach (var face in openFaces)
    {
      Vec3d center = new(
        Pos.X + 0.5 + face.Normali.X * 0.5,
        Pos.Y + 0.5 + face.Normali.Y * 0.5,
        Pos.Z + 0.5 + face.Normali.Z * 0.5
      );
      var droplets = new SimpleParticleProperties(
        2,
        4,
        ColorUtil.ToRgba(180, 200, 120, 60),
        center.AddCopy(-0.1, -0.1, -0.1),
        center.AddCopy(0.1, 0.1, 0.1),
        new Vec3f(
          face.Normali.X * 1.2f - 0.15f,
          face.Normali.Y * 1.2f - 0.15f,
          face.Normali.Z * 1.2f - 0.15f
        ),
        new Vec3f(
          face.Normali.X * 2.0f + 0.15f,
          face.Normali.Y * 2.0f + 0.15f,
          face.Normali.Z * 2.0f + 0.15f
        ),
        0.4f,
        1.0f,
        0.15f,
        0.35f,
        EnumParticleModel.Cube
      );
      Api.World.SpawnParticles(droplets);
    }

    // Occasional splash so a leaking line is audible without becoming a roar.
    if (Api.World.Rand.NextDouble() < 0.3)
      Api.World.PlaySoundAt(
        new AssetLocation("game:sounds/environment/smallsplash"),
        Pos.X + 0.5,
        Pos.Y + 0.5,
        Pos.Z + 0.5,
        null
      );
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

    if (state is PipeNetworkState netState)
    {
      // Prefer this cell's per-node temperature (spatial gradient) over the
      // network-wide injected temperature when the field has been computed.
      newTemp = netState.SourceTemperature;
      if (
        netState.NodeTemperatures != null
        && netState.NodeTemperatures.TryGetValue(Pos, out float localT)
      )
        newTemp = localT;

      newVol = netState.CurrentVolume;
      newGasType = netState.GasType;
      newOpenings = netState.OpeningsCount;
      newFlow = netState.FlowRate;
      Pressure = netState.Pressure;
    }

    byte oldGlow = GlowLightLevel;
    LocalTemperature = newTemp;
    GasType = newGasType;

    // The block id doesn't change with temperature, so the engine won't relight on
    // its own — nudge it when the emitted glow level shifts (same as molten canals).
    if (GlowLightLevel != oldGlow)
      Api?.World.BlockAccessor.MarkBlockDirty(Pos);

    if (
      Math.Abs(_lastSyncVol - newVol) > 0.1f
      || Math.Abs(_lastSyncTemp - newTemp) > 1f
      || _lastSyncType != newGasType
      || _openingsCount != newOpenings
      || Math.Abs(_lastSyncFlow - newFlow) > 0.05f
    )
    {
      _clientVolume = newVol;
      _clientMaxVolume = state is PipeNetworkState ns ? ns.MaxVolume : 0f;
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
    state is PipeNetworkState s && (s.CurrentVolume > 0f || s.FlowRate > 0f);

  protected override object? DeserializeNetworkState(ITreeAttribute tree)
  {
    float vol = tree.GetFloat("gasVol");
    if (vol <= 0f)
      return null;
    return new PipeNetworkState
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
    if (state is PipeNetworkState s)
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
      dsc.AppendLine(Lang.Get("ppex:pipe-info-leaking", _openingsCount));

    // Show throughput (m³/s moving through) rather than fill level: a line being
    // pushed and drained at once carries plenty of gas yet sits near 0 m³ stored,
    // which would otherwise read as "empty".
    if (_clientMaxVolume > 0)
    {
      // A steam cell that has cooled below the boiling point reads as condensed
      // water — the spatial condensation made visible per pipe.
      string medium =
        _clientGasType == "Steam" && LocalTemperature < PpexValues.BoilingPoint
          ? Lang.Get("ppex:pipe-medium-condensed")
          : _clientGasType;
      dsc.AppendLine(
        Lang.Get(
          "ppex:pipe-info-flow",
          _clientFlowRate,
          medium,
          LocalTemperature
        )
      );
      dsc.AppendLine(Lang.Get("ppex:pipe-info-pressure", Pressure));
    }
    else
      dsc.AppendLine(Lang.Get("ppex:pipe-info-empty"));
  }

  #endregion
}
