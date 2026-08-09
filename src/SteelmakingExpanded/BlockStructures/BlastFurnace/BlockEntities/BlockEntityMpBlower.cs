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
/// </summary>
[BlockEntityRegister]
public class BlockEntityMpBlower : BlockEntity, IRenderer {
  /// <summary>
  /// Renders nothing. The blower registers itself only to get a per-render-frame callback, which is
  /// the one place the cycle can be pinned to the axle: the 250 ms client tick is four updates a
  /// second and shows as stepping. Runs before the opaque pass so the frame is already in step when
  /// the mesh is drawn.
  /// </summary>
  public double RenderOrder => 0.0;

  public int RenderRange => 64;

  public void OnRenderFrame(float dt, EnumRenderStage stage) =>
    LockCycleToAxle();

  public void Dispose() { }

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

  /// <summary>Air (L/s) the bellows deliver at the speed sampled on the last blow tick.</summary>
  public float OutputPerSecond => OutputAt(_lastSpeed);

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
      _animBlowing = OutputPerSecond > 0f;
      ApplyAnim(_animBlowing);
      _clientTickId = RegisterGameTickListener(OnClientTick, 250);
      (api as ICoreClientAPI)?.Event.RegisterRenderer(
        this,
        EnumRenderStage.Before,
        "smex-mpblower-cycle"
      );
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

    UpdateShaftLoad();

    float speed = PortSpeed();
    if (speed != _lastSpeed) {
      _lastSpeed = speed;
      MarkDirty();
    }
    if (ProduceAir(speed, dt) > 0f)
      ExSounds.PlayLocal(Api.World, Pos, ExSounds.Bellows, 0.5f, 16f);
  }

  /// <summary>
  /// Tells the shaft what the bellows currently cost to turn: their own friction plus the torque it
  /// takes to push against the pressure already in the main. This is the whole of the drive-size
  /// relationship - the network settles where the drive's torque meets this load, so a weak drive is
  /// dragged slower as the main fills and its output falls away, while a strong one holds speed to
  /// the pressure ceiling. Nothing here caps the pressure; the ceiling emerges from the shaft
  /// stopping, and <see cref="SmexValues.MpBlowerMaxPressure"/> is only the seal's own limit.
  /// </summary>
  private void UpdateShaftLoad() =>
    Port?.SetLoad(ShaftLoadAt(BlastNetwork()?.State?.Pressure ?? 0f));

  /// <summary>
  /// Shaft load at <paramref name="pressure"/> atm of back-pressure. Public so the balance can be
  /// checked without a mechanical network or a pipe run.
  /// </summary>
  public static float ShaftLoadAt(float pressure) =>
    SmexValues.MpBlowerBaseLoad
    + SmexValues.MpBlowerLoadPerAtm * GameMath.Max(0f, pressure);

  /// <summary>
  /// Pushes <paramref name="dt"/> seconds of air into the attached blast main at axle speed
  /// <paramref name="speed"/>, and returns the litres that actually landed - 0 with the shaft
  /// stopped, with no main attached, or once the run is at the pressure ceiling. Public so the
  /// balance can be driven without a mechanical network.
  /// </summary>
  public float ProduceAir(float speed, float dt) {
    float output = OutputAt(speed);
    if (output <= 0f || dt <= 0f)
      return 0f;

    PipeNetwork? net = BlastNetwork();
    if (net == null) {
      // Working bellows with nothing to feed still move air - it just blows out of the open
      // outlet. Showing that is the only way a player can tell a driven blower from a stalled one
      // when the main is missing or has come off.
      VentToOpenAir();
      return 0f;
    }

    // Cold blast: the air enters at ambient temperature, preheating is the cowper's job.
    // TryProduceGas reports only whether it accepted anything and clamps at the pressure ceiling,
    // so the litres that landed are the change in the pool, not the amount asked for.
    float before = net.State?.Volume ?? 0f;
    net.TryProduceGas(
      output * dt,
      ExClimate.AmbientAt(this),
      "Air",
      Api.World.BlockAccessor,
      maxOutputPressure: SmexValues.MpBlowerMaxPressure
    );
    return GameMath.Max(0f, (net.State?.Volume ?? 0f) - before);
  }

  /// <summary>
  /// Blasts the air out of the bare outlet, with the same bellows note the working blower makes.
  /// Server-side: particles replicate to clients, and this runs on the blow tick.
  /// <see cref="ExParticles.GasLeak"/> rather than <c>GasVent</c> - the latter deliberately draws
  /// nothing for plain air, which is right for a valve venting steam and wrong here, where the air
  /// blast IS the thing to show.
  /// </summary>
  private void VentToOpenAir() {
    if (Api is not { Side: EnumAppSide.Server } || BlowerBlock is not { } block)
      return;

    ExParticles.GasLeak(
      Api.World,
      block.BlastOutletWorldPos(Pos),
      block.OutletFace
    );
    ExSounds.PlayLocal(Api.World, Pos, ExSounds.Bellows, 0.5f, 16f);
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

  /// <summary>The mechanical-power port hosted on the footprint cell, or null when it is absent.</summary>
  private BEBehaviorMPFillerPort? Port =>
    BlowerBlock is { } block
      ? Api
        ?.World?.BlockAccessor?.GetBlockEntity(block.MpPortWorldPos(Pos))
        ?.GetBehavior<BEBehaviorMPFillerPort>()
      : null;

  /// <summary>The driving axle's speed, or 0 when no axle is coupled to the port cell.</summary>
  private float PortSpeed() => Port is { IsTurning: true } p ? p.Speed : 0f;

  /// <summary>
  /// Pins the running <c>cycle</c> clip to the axle's angle, so the crank rod, the walking beam and
  /// the two tub pistons all move in step with the shaft the player can see, at any speed, instead
  /// of free-running at a fixed rate. The axle lives on a footprint cell, cells away from this
  /// block, so the angle comes back through the hosted port. A no-op while the blower is idle -
  /// <c>idle</c> is the clip running then, and it has no cycle to lock.
  /// </summary>
  private void LockCycleToAxle() {
    if (!_animBlowing || _animatable == null || !_animatorReady)
      return;
    if (Port is not { } port)
      return;
    // Reversed: the clip turns its Axle element through +360 over the cycle, which runs against the
    // vanilla axle for a rising angle - the blower's shaft span the wrong way beside the axle
    // driving it.
    MPAnim.LockFrameToAngle(
      _animatable.animUtil,
      "cycle",
      port.CurrentAngleRad,
      reverse: true
    );
  }

  /// <summary>
  /// Air (L/s) the bellows deliver at axle <paramref name="speed"/>. Proportional near zero - a
  /// shaft barely turning moves a little air, which is what a bellows does - then flattening toward
  /// <see cref="SmexConfig.MpBlowerMaxLitres"/>, the volume the tubs can sweep. Driven hard they
  /// cannot refill between strokes, so speed past
  /// <see cref="SmexConfig.MpBlowerHalfOutputSpeed"/> buys steadily less air and the ceiling is
  /// never reached. Still no threshold and no band: the curve is smooth and monotonic, and what
  /// limits the pressure reached is <see cref="ShaftLoadAt"/>.
  /// </summary>
  public static float OutputAt(float speed) {
    float s = GameMath.Max(0f, speed);
    return SmexValues.MpBlowerMaxLitres
      * s
      / (s + SmexValues.MpBlowerHalfOutputSpeed);
  }

  #endregion

  #region Client animation

  private void OnClientTick(float dt) {
    bool blowing = OutputPerSecond > 0f;
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
  /// <remarks>
  /// The ease-in has to be fast. Its factor is what the animator weights every pose by, and it
  /// starts at zero on each <c>StartAnimation</c>, so until it climbs the machine is drawn part way
  /// back toward its unposed shape. At the vanilla default of 10 that takes a moment; at 1 it takes
  /// seconds, and every animator rebuild - every construction stage - restarts it.
  /// </remarks>
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
        EaseInSpeed = 10f,
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

    float output = OutputPerSecond;
    dsc.AppendLine(
      output <= 0f
        ? Lang.Get("smex:mpblower-info-idle")
        : Lang.Get(
          "smex:mpblower-info-blowing",
          ExMeasure.FlowRate(output),
          ExMeasure.Pressure(SmexValues.MpBlowerMaxPressure)
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
    // The renderer holds a reference to this block entity; leaving it registered keeps a removed
    // blower alive and still writing frames.
    (Api as ICoreClientAPI)?.Event.UnregisterRenderer(
      this,
      EnumRenderStage.Before
    );
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
