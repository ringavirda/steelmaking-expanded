using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// Base for the modular Cornish-engine sub-machines (fluid pump, air blower, MP
/// generator). Each is placed at the engine's sub-machine cell and reads the master
/// engine's <see cref="BlockEntityEngineCornish.AvailablePower"/> to scale its output;
/// the engine, not the sub-machine, is the source of truth for the cycle animation.
/// </summary>
public abstract class BlockEntityEngineSubmachine
  : BlockEntity,
    IEngineSubmachine
{
  protected BlockNetworkModSystem? NetSystem;
  private BEBehaviorAnimatable? _animatable;
  private long _tickId;
  private bool _animRunning;

  /// <summary>World position of the master engine, located on initialize.</summary>
  protected BlockPos? EnginePos;

  /// <summary>The master engine block entity (Cornish or Watt), or <c>null</c> if not found.</summary>
  public BlockEntityEngineBase? Engine =>
    EnginePos != null
    && Api.World.BlockAccessor.GetBlockEntity(EnginePos)
      is BlockEntityEngineBase e
      ? e
      : null;

  /// <summary>
  /// Fraction of engine power (0..1) this sub-machine can actually use this tick.
  /// The engine reads it to consume only as much steam as is demanded (no waste).
  /// </summary>
  public virtual float PowerDemand => Engine != null ? 1f : 0f;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    EnginePos = FindEngine();

    if (api.Side == EnumAppSide.Server)
    {
      NetSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
      _tickId = RegisterGameTickListener(OnServerTick, 1000);
    }
    else
    {
      _animatable = GetBehavior<BEBehaviorAnimatable>();
      _tickId = RegisterGameTickListener(OnClientAnimTick, 500);
    }
  }

  protected PipeNetwork? NetworkAt(BlockPos pos) =>
    NetSystem?.GetNetworkAt(pos) as PipeNetwork;

  /// <summary>Locates the engine that owns this sub-machine cell (assumes aligned orientation).</summary>
  private BlockPos? FindEngine()
  {
    // Engine places the sub-machine at engine + rotate({0,0,2}, engineAngle).
    int angle =
      (StructureFillers.AngleFromSide(Block.Variant["side"]) + 180) % 360;
    Vec3i r = StructureFillers.RotateOffset(new Vec3i(0, 0, 2), angle);
    BlockPos cand = Pos.AddCopy(-r.X, -r.Y, -r.Z);
    if (
      Api.World.BlockAccessor.GetBlockEntity(cand) is BlockEntityEngineCornish
    )
      return cand;

    // Fallback: the engine sits two cells away along a horizontal axis.
    foreach (var f in BlockFacing.HORIZONTALS)
    {
      BlockPos p = Pos.AddCopy(f.Normali.X * 2, 0, f.Normali.Z * 2);
      if (Api.World.BlockAccessor.GetBlockEntity(p) is BlockEntityEngineBase)
        return p;
    }
    return null;
  }

  /// <summary>Per-second server work, scaled by <paramref name="power"/> (0..max).</summary>
  protected abstract void DoWork(float power, float dt);

  private void OnServerTick(float dt)
  {
    if (EnginePos == null)
      EnginePos = FindEngine();
    var engine = Engine;
    if (engine == null)
      return;
    DoWork(engine.AvailablePower, dt);
  }

  // Mirror the engine's cycle animation (engine sets the tempo).
  private void OnClientAnimTick(float dt)
  {
    var engine = Engine;
    bool run = engine?.IsRunning ?? false;
    if (run != _animRunning)
    {
      _animRunning = run;
      ApplyAnim(run, engine?.AnimationSpeed ?? 1f);
    }
  }

  protected virtual void ApplyAnim(bool running, float speed)
  {
    if (_animatable == null)
      return;
    if (running)
      _animatable.animUtil.StartAnimation(
        new AnimationMetaData
        {
          Animation = "cycle",
          Code = "cycle",
          AnimationSpeed = speed,
          EaseInSpeed = 3f,
          EaseOutSpeed = 3f,
        }.Init()
      );
    else
      _animatable.animUtil.StopAnimation("cycle");
  }

  /// <summary>Left face in north orientation, rotated to this block's placement.</summary>
  protected BlockFacing LeftFace =>
    StructureFillers.RotateFacing(
      BlockFacing.WEST,
      StructureFillers.AngleFromSide(Block.Variant["side"])
    );

  public override void OnBlockRemoved()
  {
    if (_tickId != 0)
      UnregisterGameTickListener(_tickId);
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    if (_tickId != 0)
      UnregisterGameTickListener(_tickId);
    base.OnBlockUnloaded();
  }
}
