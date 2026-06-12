using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib;

/// <summary>
/// Shared catalogue of particle effects for the mod family (ppex + smex) — gas plumes, leak
/// wisps, water splashes, exhaust smoke, glow sparks — along with their colours. Block
/// entities and networks call these instead of building
/// <see cref="SimpleParticleProperties"/> inline, so the look and tuning live in one place.
/// <para>
/// The named presets are thin wrappers over the configurable <see cref="Spawn"/> primitive;
/// new effects should reuse it (or <see cref="RisingPlume"/>) rather than constructing
/// particles by hand. Every method spawns through
/// <see cref="IWorldAccessor.SpawnParticles(IParticlePropertiesProvider, IPlayer)"/>, so the
/// caller picks the side: spawn on the server to broadcast to nearby clients, or on the
/// client to show locally. The methods do no side-checking themselves.
/// </para>
/// </summary>
public static class ExParticles
{
  /// <summary>White vapour (steam / pressurised air venting).</summary>
  public static readonly int Vapor = ColorUtil.ToRgba(130, 235, 235, 240);

  /// <summary>Dark soot (combustion exhaust plume out of a chimney neck).</summary>
  public static readonly int Exhaust = ColorUtil.ToRgba(150, 45, 45, 45);

  /// <summary>Pale grey wisp (pressurised gas hissing out of a leak).</summary>
  public static readonly int GasLeakTint = ColorUtil.ToRgba(150, 200, 200, 200);

  /// <summary>Translucent blue (water droplets / splashes).</summary>
  public static readonly int Water = ColorUtil.ToRgba(210, 60, 110, 190);

  /// <summary>Grey furnace/refining smoke (smoke stack, bessemer, cowper exhaust).</summary>
  public static readonly int Smoke = ColorUtil.ToRgba(200, 80, 80, 80);

  /// <summary>Warm orange glow spark (cowper stove combustion).</summary>
  public static readonly int GlowSpark = ColorUtil.ToRgba(200, 255, 150, 50);

  /// <summary>Dark falling dust (hopper bell dumping its charge).</summary>
  public static readonly int Dust = ColorUtil.ToRgba(255, 60, 60, 60);

  /// <summary>Faint, semi-transparent pale wisp — ambient air drawn into the air blower's cylinder.</summary>
  public static readonly int AirTint = ColorUtil.ToRgba(70, 225, 225, 230);

  /// <summary>
  /// Configurable core: builds a <see cref="SimpleParticleProperties"/> from the given
  /// bounds/velocity/timing and spawns it. Every preset below funnels through here.
  /// </summary>
  public static void Spawn(
    IWorldAccessor world,
    int color,
    Vec3d minPos,
    Vec3d maxPos,
    Vec3f minVelocity,
    Vec3f maxVelocity,
    float minQuantity,
    float maxQuantity,
    float lifeLength,
    float gravityEffect,
    float minSize,
    float maxSize,
    EnumParticleModel model = EnumParticleModel.Quad,
    EvolvingNatFloat? opacityEvolve = null,
    EvolvingNatFloat? sizeEvolve = null,
    bool shouldDieInLiquid = false
  )
  {
    var particles = new SimpleParticleProperties(
      minQuantity,
      maxQuantity,
      color,
      minPos,
      maxPos,
      minVelocity,
      maxVelocity,
      lifeLength,
      gravityEffect,
      minSize,
      maxSize,
      model
    )
    {
      ShouldDieInLiquid = shouldDieInLiquid,
    };
    // The evolve properties are non-nullable on SimpleParticleProperties; only set them
    // when the caller supplied one so presets without evolves keep the engine default.
    if (opacityEvolve.HasValue)
      particles.OpacityEvolve = opacityEvolve.Value;
    if (sizeEvolve.HasValue)
      particles.SizeEvolve = sizeEvolve.Value;
    world.SpawnParticles(particles);
  }

  /// <summary>World-space centre of <paramref name="face"/> on the block at <paramref name="pos"/>.</summary>
  public static Vec3d FaceCenter(BlockPos pos, BlockFacing face) =>
    new(
      pos.X + 0.5 + face.Normali.X * 0.5,
      pos.Y + 0.5 + face.Normali.Y * 0.5,
      pos.Z + 0.5 + face.Normali.Z * 0.5
    );

  /// <summary>Velocity directed out of <paramref name="face"/> at <paramref name="speed"/>, offset on every axis by <paramref name="spread"/>.</summary>
  public static Vec3f OutVel(BlockFacing face, float speed, float spread) =>
    new(
      face.Normali.X * speed + spread,
      face.Normali.Y * speed + spread,
      face.Normali.Z * speed + spread
    );

  /// <summary>
  /// Plume colour for a gas type: dark for exhaust, white for steam / pressurised air.
  /// Returns <c>null</c> for plain air when <paramref name="ventAir"/> is <c>false</c>
  /// (a pressure valve shows nothing when only air spills).
  /// </summary>
  public static int? GasColor(string gasType, bool ventAir = true) =>
    gasType == "Exhaust" ? Exhaust
    : gasType == "Air" && !ventAir ? null
    : Vapor;

  /// <summary>
  /// Generic box-bounded rising plume — the workhorse for furnace/refining smoke and glow.
  /// Callers pass their own colour, world-space box, velocity range, density, life and
  /// optional evolves; this is just a readable alias over <see cref="Spawn"/>.
  /// </summary>
  public static void RisingPlume(
    IWorldAccessor world,
    int color,
    Vec3d minPos,
    Vec3d maxPos,
    Vec3f minVelocity,
    Vec3f maxVelocity,
    float minQuantity,
    float maxQuantity,
    float lifeLength,
    float gravityEffect,
    float minSize,
    float maxSize,
    EvolvingNatFloat? opacityEvolve = null,
    EvolvingNatFloat? sizeEvolve = null
  ) =>
    Spawn(
      world,
      color,
      minPos,
      maxPos,
      minVelocity,
      maxVelocity,
      minQuantity,
      maxQuantity,
      lifeLength,
      gravityEffect,
      minSize,
      maxSize,
      EnumParticleModel.Quad,
      opacityEvolve,
      sizeEvolve
    );

  /// <summary>
  /// Rising smoke plume out of the top of a chimney that is drawing gas from a network —
  /// dark for exhaust, white for air / steam vapour.
  /// </summary>
  public static void ChimneySmoke(
    IWorldAccessor world,
    BlockPos chimneyPos,
    string gasType
  ) =>
    Spawn(
      world,
      gasType == "Exhaust" ? Exhaust : Vapor,
      new Vec3d(chimneyPos.X + 0.35, chimneyPos.Y + 0.85, chimneyPos.Z + 0.35),
      new Vec3d(chimneyPos.X + 0.65, chimneyPos.Y + 1.0, chimneyPos.Z + 0.65),
      new Vec3f(-0.05f, 0.2f, -0.05f),
      new Vec3f(0.05f, 0.5f, 0.05f),
      2,
      3,
      2.5f,
      -0.05f,
      0.5f,
      1.5f,
      EnumParticleModel.Quad,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -80f),
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1.5f)
    );

  /// <summary>
  /// Short-lived white steam plume rising out of the top of <paramref name="cell"/>
  /// (an open boiler lid or steam-outlet neck). <paramref name="count"/> sets the density.
  /// </summary>
  public static void SteamPlume(IWorldAccessor world, BlockPos cell, int count)
  {
    var rnd = world.Rand;
    for (int i = 0; i < count; i++)
    {
      Vec3d p = new(
        cell.X + 0.5 + (rnd.NextDouble() - 0.5) * 0.5,
        cell.Y + 0.9 + rnd.NextDouble() * 0.25,
        cell.Z + 0.5 + (rnd.NextDouble() - 0.5) * 0.5
      );
      Spawn(
        world,
        Vapor,
        p,
        p.AddCopy(0.0, 0.5, 0.0),
        new Vec3f(-0.05f, 0.1f, -0.05f),
        new Vec3f(0.05f, 0.4f, 0.05f),
        1,
        1,
        2.0f,
        -0.02f,
        0.3f,
        0.8f,
        EnumParticleModel.Quad,
        new EvolvingNatFloat(EnumTransformFunction.LINEAR, -180f),
        new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1.5f),
        shouldDieInLiquid: true
      );
    }
  }

  /// <summary>
  /// A small, tight white steam burst rising from a precise world point — the puff vented out
  /// of an engine cylinder top on a power stroke (or a constant hard vent while it strains over
  /// pressure). <paramref name="count"/> sets the density.
  /// </summary>
  public static void SteamPuff(IWorldAccessor world, Vec3d pos, int count) =>
    Spawn(
      world,
      Vapor,
      pos.AddCopy(-0.1, 0.0, -0.1),
      pos.AddCopy(0.1, 0.12, 0.1),
      new Vec3f(-0.1f, 0.35f, -0.1f),
      new Vec3f(0.1f, 0.8f, 0.1f),
      count,
      count,
      0.8f,
      -0.03f,
      0.25f,
      0.6f,
      EnumParticleModel.Quad,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -200f),
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1.5f),
      shouldDieInLiquid: true
    );

  /// <summary>
  /// A short downdraft of faint, semi-transparent air wisps sucked down into the open top of the
  /// air blower's cylinder as the piston tops out and starts its intake stroke — the visible
  /// "inhale" of ambient air. Spawns just above <paramref name="mouth"/> (the cylinder's top
  /// centre) and pulls the wisps downward, shrinking as they vanish into the bore.
  /// <paramref name="count"/> sets the density.
  /// </summary>
  public static void AirInhale(IWorldAccessor world, Vec3d mouth, int count) =>
    Spawn(
      world,
      AirTint,
      mouth.AddCopy(-0.22, 0.0, -0.22),
      mouth.AddCopy(0.22, 0.18, 0.22),
      new Vec3f(-0.05f, -0.55f, -0.05f),
      new Vec3f(0.05f, -0.95f, 0.05f),
      count,
      count,
      0.6f,
      0.04f,
      0.25f,
      0.55f,
      EnumParticleModel.Quad,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -160f),
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.9f)
    );

  /// <summary>
  /// A dense, expanding-then-fading cloud of dark smoke bursting from a world point — the sooty
  /// blast of a machine exploding (engine burst), thrown outward on every axis with a slight rise.
  /// <paramref name="count"/> sets the density. Spawn on the server to broadcast to nearby clients.
  /// </summary>
  public static void SmokeCloud(IWorldAccessor world, Vec3d pos, int count) =>
    Spawn(
      world,
      Smoke,
      pos.AddCopy(-0.3, -0.2, -0.3),
      pos.AddCopy(0.3, 0.3, 0.3),
      new Vec3f(-1.2f, 0.2f, -1.2f),
      new Vec3f(1.2f, 1.6f, 1.2f),
      count,
      count,
      2.5f,
      -0.04f,
      0.6f,
      1.4f,
      EnumParticleModel.Quad,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -60f),
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, 2.5f)
    );

  /// <summary>
  /// Pale grey gas wisp hissing out of one open pipe connector face (a leak).
  /// <paramref name="intensity"/> (0..1) scales the particle density with how fast the run
  /// is bleeding — a faint wisp near 0, a thick jet near 1.
  /// </summary>
  public static void GasLeak(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face,
    float intensity = 1f
  )
  {
    float t = GameMath.Clamp(intensity, 0f, 1f);
    Vec3d center = FaceCenter(pos, face);
    Spawn(
      world,
      GasLeakTint,
      center.AddCopy(-0.1, -0.1, -0.1),
      center.AddCopy(0.1, 0.1, 0.1),
      OutVel(face, 1.5f, -0.2f),
      OutVel(face, 2.5f, 0.2f),
      GameMath.Lerp(1f, 6f, t),
      GameMath.Lerp(2f, 12f, t),
      0.5f,
      -0.05f,
      0.2f,
      0.5f,
      EnumParticleModel.Quad,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -150f)
    );
  }

  /// <summary>
  /// Gas venting out of a face under pressure (a pressure valve's open output side) —
  /// white for steam, dark for exhaust, nothing at all for plain air.
  /// </summary>
  public static void GasVent(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face,
    string gasType
  )
  {
    if (GasColor(gasType, ventAir: false) is not int color)
      return;

    Vec3d center = FaceCenter(pos, face);
    Spawn(
      world,
      color,
      center.AddCopy(-0.1, -0.1, -0.1),
      center.AddCopy(0.1, 0.1, 0.1),
      OutVel(face, 0.6f, -0.05f),
      OutVel(face, 1.2f, 0.05f),
      2,
      3,
      2.0f,
      -0.05f,
      0.4f,
      1.2f,
      EnumParticleModel.Quad,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100f),
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1.2f)
    );
  }

  /// <summary>
  /// Heavy blue water droplets sprayed out of one open face and pulled down by gravity —
  /// a leaking liquid line or a pressure valve spilling water. Pair with
  /// <see cref="ExSounds.SplashSound"/> once per spill event for the audio.
  /// </summary>
  public static void WaterJet(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face,
    float intensity = 1f
  )
  {
    float t = GameMath.Clamp(intensity, 0f, 1f);
    Vec3d center = FaceCenter(pos, face);
    Spawn(
      world,
      Water,
      center.AddCopy(-0.1, -0.1, -0.1),
      center.AddCopy(0.1, 0.1, 0.1),
      OutVel(face, 1.2f, -0.15f),
      OutVel(face, 2.0f, 0.15f),
      GameMath.Lerp(1f, 8f, t),
      GameMath.Lerp(3f, 16f, t),
      0.8f,
      1.0f,
      0.18f,
      0.45f,
      EnumParticleModel.Cube,
      shouldDieInLiquid: true
    );
  }

  /// <summary>
  /// Blue water splash pooling out of the top of <paramref name="cell"/> — condensate
  /// (engine outlet) with nowhere to drain.
  /// </summary>
  public static void WaterSpill(IWorldAccessor world, BlockPos cell)
  {
    var pos = new Vec3d(cell.X + 0.5, cell.Y + 0.1, cell.Z + 0.5);
    Spawn(
      world,
      Water,
      pos.AddCopy(-0.25, 0.0, -0.25),
      pos.AddCopy(0.25, 0.15, 0.25),
      new Vec3f(-0.4f, 0.1f, -0.4f),
      new Vec3f(0.4f, 0.5f, 0.4f),
      7,
      11,
      1.0f,
      0.3f,
      0.2f,
      0.45f,
      EnumParticleModel.Cube,
      shouldDieInLiquid: true
    );
  }

  /// <summary>
  /// Dark dust raining down out of the bottom of <paramref name="pos"/> — the hopper bell
  /// dropping its charge into the furnace shaft.
  /// </summary>
  // Mirrors the original hopper-bell call exactly: it passed (float)EnumParticleModel.Cube
  // (== 0) into the maxSize slot, so maxSize is 0 and the model stays the Quad default.
  public static void FallingDust(IWorldAccessor world, BlockPos pos) =>
    Spawn(
      world,
      Dust,
      new Vec3d(pos.X + 0.3, pos.Y - 0.2, pos.Z + 0.3),
      new Vec3d(pos.X + 0.7, pos.Y - 0.2, pos.Z + 0.7),
      new Vec3f(-0.2f, -2f, -0.2f),
      new Vec3f(0.2f, -4f, 0.2f),
      20,
      30,
      1.5f,
      0.5f,
      1.2f,
      0f,
      EnumParticleModel.Quad
    );
}
