using System;
using System.Text;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// Block entity for all gas-pipe blocks.  Implements <see cref="IPipeNode"/> by
/// delegating entirely to the owning <see cref="PipeNetwork"/> — the BE holds no
/// internal network state and never calls <c>BroadcastUpdate</c> directly.
/// </summary>
[EntityRegister]
public class BlockEntityPipe : BlockEntityNetworkNode, IPipeNode
{
  public sealed override string NetworkType
  {
    get => "pipe";
    set { }
  }

  /// <summary>Gas temperature (°C) of this pipe's network, cached from the last broadcast.
  /// Every pipe in a run reports the same value — the network has no spatial gradient.</summary>
  public float Temperature { get; protected set; }

  /// <summary>Current medium of this pipe's network ("Air"/"Steam"/"Exhaust"/"Water", or
  /// "" when empty), cached from the last network broadcast.</summary>
  public string Medium { get; protected set; } = "";

  /// <summary>Whether this pipe's network currently carries water rather than a gas.</summary>
  public bool IsLiquid => Medium == "Water";

  /// <summary>Pressure (atm) of this pipe's network, cached from the last broadcast — the
  /// volume ratio for a gas, or the pump-set pressure for a water line. Synced from the
  /// server <see cref="PipeNetworkState.Pressure"/> so it reads correctly on the client.</summary>
  public float Pressure { get; protected set; }

  /// <summary>Client-synced gas volume (L) in this pipe's network; used by external
  /// look-at info such as the vanilla-chimney venting patch.</summary>
  public float Volume => _clientVolume;

  /// <summary>Client-synced maximum volume (L) of this pipe's network at its current node count.</summary>
  public float MaxVolume => _clientMaxVolume;

  // Client-side display data populated from network broadcasts.
  protected float _clientVolume;
  protected float _clientMaxVolume;
  protected int _openingsCount;
  protected float _clientFlowRate;

  // Throttle-check fields — skip MarkDirty when nothing meaningful changed.
  private float _lastSyncVol = -1;
  private float _lastSyncTemp = -1;
  private string _lastSyncMedium = "";
  private float _lastSyncFlow = -1;
  private float _lastSyncPressure = -1;

  #region IPipeNode

  /// <inheritdoc/>
  public virtual bool TryProduce(
    float volume,
    float temperature,
    string gasType = "Air",
    float maxOutputPressure = 1.0f,
    bool bypassLeakCap = false
  )
  {
    if (NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork gasNet)
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

  /// <inheritdoc/>
  public virtual float TryConsume(float requestedVolume) =>
    NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork pipeNet
      ? 0f
      : pipeNet.TryConsumeGas(requestedVolume, Api.World.BlockAccessor);

  #endregion

  #region Ambient sound

  private long _ambientTickId;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    // Client-side ambience for pipes carrying pressurised gas (a lava-like bubble) or water
    // (a creek-like trickle).
    if (api.Side == EnumAppSide.Client)
      _ambientTickId = RegisterGameTickListener(OnAmbientTick, 1000);
  }

  /// <summary>
  /// Sparse, low-volume per-pipe ambience: each pipe only has a small chance to sound each
  /// second and only carries a short range, so a whole network murmurs faintly near the
  /// player instead of roaring. All driver fields are already client-synced.
  /// </summary>
  private void OnAmbientTick(float dt)
  {
    var world = Api.World;
    if (!IsLiquid && Pressure > 1f)
      ExSounds.PlayChance(
        world,
        Pos,
        ExSounds.Lava,
        0.08,
        range: 7f,
        volume: 0.25f
      );
    if (IsLiquid && (_clientVolume > 0.01f || _clientFlowRate > 0.01f))
      ExSounds.PlayChance(
        world,
        Pos,
        ExSounds.Creek,
        0.08,
        range: 7f,
        volume: 0.25f
      );
  }

  public override void OnBlockRemoved()
  {
    if (_ambientTickId != 0)
      UnregisterGameTickListener(_ambientTickId);
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    if (_ambientTickId != 0)
      UnregisterGameTickListener(_ambientTickId);
    base.OnBlockUnloaded();
  }

  #endregion

  #region Leak particles

  /// <summary>
  /// Wisps pressurised gas out of each open-ended connector while the gas pool is bleeding —
  /// <paramref name="intensity"/> (0..1) scales the particle density with the leak rate, plus
  /// an airy swoosh. Called from the network tick (server) so the particles broadcast.
  /// </summary>
  public void SpawnGasLeak(BlockFacing[] openFaces, float intensity)
  {
    if (openFaces.Length == 0)
      return;

    foreach (var face in openFaces)
      ExParticles.GasLeak(Api.World, Pos, face, intensity);

    // Pressurised gas escaping an open pipe end gives an airy swoosh.
    ExSounds.PlayAt(Api.World, Pos, ExSounds.Swoosh, range: 24f, volume: 0.6f);
  }

  /// <summary>
  /// Sprays water out of each open-ended connector while the liquid pool is leaking —
  /// falling blue droplets (density scaled by <paramref name="intensity"/> 0..1) plus an
  /// occasional splash sound. Called from the network tick (server) so particles broadcast.
  /// </summary>
  public void SpawnLiquidLeak(BlockFacing[] openFaces, float intensity = 1f)
  {
    if (openFaces.Length == 0)
      return;

    foreach (var face in openFaces)
      ExParticles.WaterJet(Api.World, Pos, face, intensity);

    ExSounds.SplashSound(Api.World, Pos);
  }

  #endregion

  #region Network updates

  public override void OnNetworkUpdate(object? state)
  {
    base.OnNetworkUpdate(state);

    float newTemp = 20f;
    float newVol = 0;
    string newMedium = "";
    int newOpenings = 0;
    float newFlow = 0;
    float newPressure = 0;
    float newMaxVol = 0;

    if (state is PipeNetworkState netState)
    {
      newTemp = netState.Temperature;
      newVol = netState.Volume;
      newMedium = netState.MediumType;
      newOpenings = netState.OpeningsCount;
      newFlow = netState.FlowRate;
      newPressure = netState.Pressure;
      newMaxVol = netState.MaxVolume;
    }

    Temperature = newTemp;
    Medium = newMedium;
    Pressure = newPressure;

    if (
      Math.Abs(_lastSyncVol - newVol) > 0.1f
      || Math.Abs(_lastSyncTemp - newTemp) > 1f
      || _lastSyncMedium != newMedium
      || _openingsCount != newOpenings
      || Math.Abs(_lastSyncFlow - newFlow) > 0.05f
      || Math.Abs(_lastSyncPressure - newPressure) > 0.02f
    )
    {
      _clientVolume = newVol;
      _clientMaxVolume = newMaxVol;
      _openingsCount = newOpenings;
      _clientFlowRate = newFlow;
      _lastSyncVol = newVol;
      _lastSyncTemp = newTemp;
      _lastSyncMedium = newMedium;
      _lastSyncFlow = newFlow;
      _lastSyncPressure = newPressure;
      MarkDirty(true);
    }
  }

  #endregion

  #region Persistence hooks

  protected override bool IsNetworkStateMeaningful(object? state) =>
    state is PipeNetworkState s && (s.Volume > 0f || s.FlowRate > 0f);

  protected override object? DeserializeNetworkState(ITreeAttribute tree)
  {
    float vol = tree.GetFloat("vol");
    if (vol <= 0f)
      return null;
    return new PipeNetworkState
    {
      Volume = vol,
      MaxVolume = tree.GetFloat("max"),
      Temperature = tree.GetFloat("temp", 20f),
      MediumType = tree.GetString("medium", ""),
      OpeningsCount = tree.GetInt("openings"),
      FlowRate = tree.GetFloat("flow"),
      Pressure = tree.GetFloat("pressure"),
    };
  }

  protected override void SerializeNetworkState(
    ITreeAttribute tree,
    object? state
  )
  {
    if (state is PipeNetworkState s)
    {
      tree.SetFloat("vol", s.Volume);
      tree.SetFloat("max", s.MaxVolume);
      tree.SetFloat("temp", s.Temperature);
      tree.SetString("medium", s.MediumType);
      tree.SetInt("openings", s.OpeningsCount);
      tree.SetFloat("flow", s.FlowRate);
      tree.SetFloat("pressure", s.Pressure);
    }
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    // Ensure the synced display fields are present even when _savedNetworkState is null
    // (empty network), so the client display/glow always has a value.
    tree.SetFloat("temp", Temperature);
    tree.SetString("medium", Medium);
    tree.SetFloat("pressure", Pressure);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving); // calls DeserializeNetworkState → _savedNetworkState

    // Populate client display fields from the same tree keys for rendering.
    _clientVolume = tree.GetFloat("vol");
    _clientMaxVolume = tree.GetFloat("max");
    Temperature = tree.GetFloat("temp", 20f);
    Medium = tree.GetString("medium", "");
    _openingsCount = tree.GetInt("openings");
    _clientFlowRate = tree.GetFloat("flow");
    Pressure = tree.GetFloat("pressure");
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
      dsc.AppendLine(Lang.Get("ppex:pipe-info-leaking"));

    // A water line (pump → boiler) reads as water; a gas line shows its gas type. Show
    // throughput (L/s moving through) rather than fill level: a line pushed and drained at
    // once carries plenty yet sits near 0 L stored, which would otherwise read as "empty".
    if (IsLiquid)
    {
      dsc.AppendLine(
        Lang.Get(
          "ppex:pipe-info-flow",
          _clientFlowRate,
          Lang.Get("ppex:pipe-medium-water"),
          Temperature
        )
      );
      dsc.AppendLine(Lang.Get("ppex:pipe-info-pressure", Pressure));
    }
    else if (_clientMaxVolume > 0 && Medium.Length > 0)
    {
      dsc.AppendLine(
        Lang.Get("ppex:pipe-info-flow", _clientFlowRate, Medium, Temperature)
      );
      dsc.AppendLine(Lang.Get("ppex:pipe-info-pressure", Pressure));

      // Only the weakest pipes in a run ever reach their own burst rating (production
      // is capped there), so this warning lights up exactly on the cells at risk.
      if (Block is BlockPipe bp && Pressure >= bp.BurstPressure - 0.001f)
        dsc.AppendLine(Lang.Get("ppex:pipe-info-overpressure"));
    }
    else
      dsc.AppendLine(Lang.Get("ppex:pipe-info-empty"));
  }

  #endregion
}
