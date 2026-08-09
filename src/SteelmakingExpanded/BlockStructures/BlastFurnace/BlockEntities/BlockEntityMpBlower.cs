using System.Text;
using ExpandedLib.Blocks.Construction;
using ExpandedLib.Blocks.Machines;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.Helpers;
using SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;

/// <summary>
/// The twin-tub mechanical blower. Once a second it samples the axle coupled to its
/// mechanical-power port cell and pushes that second's air into the pipe network attached at its
/// outlet cell, at ambient temperature and clamped at <see cref="SmexValues.MpBlowerMaxPressure"/>.
/// It is the only air source that needs no steam, and its pressure ceiling is what keeps it to
/// iron: it clears the blast furnace's gate and never reaches the converter's.
/// </summary>
[BlockEntityRegister]
public class BlockEntityMpBlower : BlockEntity {
  /// <summary>Server-side blow interval; matches the pipe network's own per-second tick.</summary>
  private const int BlowIntervalMs = 1000;

  private long _blowTickId;
  private long _clientTickId;

  /// <summary>Axle speed sampled on the last blow tick; synced so the client can read it.</summary>
  private float _lastSpeed;

  private BEBehaviorAnimatable? _animatable;
  private ExRightClickConstructable? _rcc;
  private bool _animatorReady;
  private bool _animBlowing;

  private BlockMpBlower? BlowerBlock => Block as BlockMpBlower;

  /// <summary>True once every construction stage is built; an unfinished frame does no work.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>Fraction of rated output the bellows are currently delivering.</summary>
  public float OutputFraction => SpeedFraction(_lastSpeed);

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _rcc = GetBehavior<ExRightClickConstructable>();

    if (api.Side == EnumAppSide.Server) {
      _blowTickId = RegisterGameTickListener(OnBlowTick, BlowIntervalMs);
    } else {
      _animatable = GetBehavior<BEBehaviorAnimatable>();
      if (_rcc != null)
        _rcc.OnShapeChanged += OnConstructShapeChanged;
      RebuildAnimator(_rcc?.shape?.SelectiveElements);
      _animBlowing = OutputFraction > 0f;
      ApplyAnim(_animBlowing);
      _clientTickId = RegisterGameTickListener(OnClientTick, 250);
    }
  }

  #region Server work

  /// <summary>
  /// Samples the axle and pushes one interval of air into the blast main. Marks dirty only when the
  /// sampled speed changed, so an idle blower costs no sync traffic.
  /// </summary>
  private void OnBlowTick(float dt) {
    if (!IsConstructed)
      return;

    float speed = PortSpeed();
    if (speed != _lastSpeed) {
      _lastSpeed = speed;
      MarkDirty();
    }
    if (ProduceAir(speed, dt) > 0f)
      ExSounds.PlayLocal(Api.World, Pos, ExSounds.Bellows, 0.5f, 16f);
  }

  /// <summary>
  /// Pushes <paramref name="dt"/> seconds of air into the attached blast main at axle speed
  /// <paramref name="speed"/>, and returns the litres that actually landed - 0 below
  /// <see cref="SmexValues.MpBlowerMinSpeed"/>, with no main attached, or once the run is at the
  /// pressure ceiling. Public so the balance can be driven without a mechanical network.
  /// </summary>
  public float ProduceAir(float speed, float dt) {
    float fraction = SpeedFraction(speed);
    if (fraction <= 0f || dt <= 0f)
      return 0f;

    PipeNetwork? net = BlastNetwork();
    if (net == null)
      return 0f;

    // Cold blast: the air enters at ambient temperature, preheating is the cowper's job.
    // TryProduceGas reports only whether it accepted anything and clamps at the pressure ceiling,
    // so the litres that landed are the change in the pool, not the amount asked for.
    float before = net.State?.Volume ?? 0f;
    net.TryProduceGas(
      SmexValues.MpBlowerOutputPerSecond * fraction * dt,
      AmbientTemperature,
      "Air",
      Api.World.BlockAccessor,
      maxOutputPressure: SmexValues.MpBlowerMaxPressure
    );
    return GameMath.Max(0f, (net.State?.Volume ?? 0f) - before);
  }

  /// <summary>
  /// The pipe network the blast main forms, found across the outlet face from the blower's outlet
  /// filler cell. Null unless a pipe there presents a connector facing back.
  /// </summary>
  private PipeNetwork? BlastNetwork() {
    if (BlowerBlock is not { } block)
      return null;
    BlockFacing outFace = block.OutletFace;
    BlockPos pipePos = block.BlastOutletWorldPos(Pos).AddCopy(outFace);

    return
      Api.World.BlockAccessor.GetBlock(pipePos) is BlockNetworkNode pipe
      && pipe.HasConnectorAt(outFace.Opposite)
      ? this.NetworkAt<PipeNetwork>(pipePos)
      : null;
  }

  /// <summary>The driving axle's speed, or 0 when no axle is coupled to the port cell.</summary>
  private float PortSpeed() {
    if (BlowerBlock is not { } block)
      return 0f;
    var port = Api
      .World.BlockAccessor.GetBlockEntity(block.MpPortWorldPos(Pos))
      ?.GetBehavior<BEBehaviorMPFillerPort>();
    return port is { IsTurning: true } ? port.Speed : 0f;
  }

  /// <summary>Ambient air temperature at the bellows, falling back to 20 °C with no climate data.</summary>
  private float AmbientTemperature =>
    Api?.World?.BlockAccessor?.GetClimateAt(Pos)?.Temperature ?? 20f;

  /// <summary>
  /// Fraction of rated output the bellows deliver at <paramref name="speed"/>: 0 at or below
  /// <see cref="SmexValues.MpBlowerMinSpeed"/>, 1 at or above
  /// <see cref="SmexValues.MpBlowerMaxSpeed"/>, linear between.
  /// </summary>
  public static float SpeedFraction(float speed) {
    float min = SmexValues.MpBlowerMinSpeed;
    float max = SmexValues.MpBlowerMaxSpeed;
    if (speed <= min)
      return 0f;
    if (max <= min)
      return 1f;
    return GameMath.Clamp((speed - min) / (max - min), 0f, 1f);
  }

  #endregion

  #region Client animation

  private void OnClientTick(float dt) {
    bool blowing = OutputFraction > 0f;
    if (blowing == _animBlowing)
      return;
    _animBlowing = blowing;
    ApplyAnim(blowing);
  }

  private void OnConstructShapeChanged(CompositeShape cs) {
    RebuildAnimator(cs?.SelectiveElements);
    ApplyAnim(_animBlowing);
  }

  /// <summary>
  /// (Re)builds the animator to render exactly the construction stages built so far. A fresh shape
  /// is loaded each call - reusing one re-maps UVs into atlas space and stretches the textures.
  /// Leaves <see cref="_animatorReady"/> false if the shape fails to resolve, so a pose is never
  /// queued against a null animator.
  /// </summary>
  private void RebuildAnimator(string[]? selectiveElements) {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    MeshData meshData = _animatable.animUtil.CreateMesh(
      Block.Code.Path,
      null,
      out Shape resolvedShape,
      null,
      new TesselationMetaData { SelectiveElements = selectiveElements }
    );
    _animatable.animUtil.InitializeAnimator(
      Block.Code.Path,
      meshData,
      resolvedShape,
      new Vec3f(0, Block.Shape.rotateY, 0)
    );
    _animatorReady = _animatable.animUtil.animator != null;
  }

  /// <summary>
  /// Holds one animation at a time - <c>cycle</c> while the bellows work, <c>idle</c> otherwise.
  /// Keeping one active stops the animator mesh vanishing.
  /// </summary>
  private void ApplyAnim(bool blowing) {
    if (_animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    util.StopAnimation(blowing ? "idle" : "cycle");
    util.StartAnimation(
      new AnimationMetaData {
        Animation = blowing ? "cycle" : "idle",
        Code = blowing ? "cycle" : "idle",
        AnimationSpeed = 1f,
        EaseInSpeed = 1f,
        EaseOutSpeed = 5f,
      }.Init()
    );
  }

  #endregion

  #region Persistence + HUD

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetFloat("blowerSpeed", _lastSpeed);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _lastSpeed = tree.GetFloat("blowerSpeed");
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed)
      return;

    float fraction = OutputFraction;
    dsc.AppendLine(
      fraction <= 0f
        ? Lang.Get("smex:mpblower-info-idle")
        : Lang.Get(
          "smex:mpblower-info-blowing",
          ExMeasure.FlowRate(SmexValues.MpBlowerOutputPerSecond * fraction),
          (int)(fraction * 100f)
        )
    );
  }

  public override void OnBlockRemoved() {
    UnregisterTicks();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    UnregisterTicks();
    base.OnBlockUnloaded();
  }

  private void UnregisterTicks() {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    if (_blowTickId != 0) {
      UnregisterGameTickListener(_blowTickId);
      _blowTickId = 0;
    }
    if (_clientTickId != 0) {
      UnregisterGameTickListener(_clientTickId);
      _clientTickId = 0;
    }
  }

  #endregion
}
