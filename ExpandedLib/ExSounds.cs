using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib;

/// <summary>
/// Shared catalogue of sound asset locations and small play helpers used across the mod
/// family (ppex + smex). All sounds resolve from the vanilla "game" domain (which also
/// covers the survival asset folder). Playing on the server replicates to nearby clients;
/// the helpers do the side-checking where noted.
/// </summary>
public static class ExSounds
{
  // Molten / heat
  public static readonly AssetLocation Sizzle = new("game:sounds/sizzle");
  public static readonly AssetLocation MoltenMetal = new(
    "game:sounds/effect/moltenmetal"
  );
  public static readonly AssetLocation PourMetal = new("game:sounds/pourmetal");
  public static readonly AssetLocation Embers = new(
    "game:sounds/effect/embers"
  );
  public static readonly AssetLocation Fire = new(
    "game:sounds/environment/fire"
  );
  public static readonly AssetLocation Extinguish = new(
    "game:sounds/effect/extinguish1"
  );
  public static readonly AssetLocation Ignite = new("game:sounds/torch-ignite");

  // Mechanical / interaction
  public static readonly AssetLocation Latch = new("game:sounds/effect/latch");
  public static readonly AssetLocation CokeOvenDoorOpen = new(
    "game:sounds/block/cokeovendoor-open"
  );
  public static readonly AssetLocation CokeOvenDoorClose = new(
    "game:sounds/block/cokeovendoor-close"
  );
  public static readonly AssetLocation Bellows = new(
    "game:sounds/effect/bellows"
  );
  public static readonly AssetLocation Ingot = new("game:sounds/block/ingot");
  public static readonly AssetLocation AnvilHit = new(
    "game:sounds/effect/anvilhit1"
  );
  public static readonly AssetLocation AnvilHitShort = new(
    "game:sounds/effect/anvilhit"
  );
  public static readonly AssetLocation Build = new("game:sounds/player/build");
  public static readonly AssetLocation StoneCrush = new(
    "game:sounds/effect/stonecrush"
  );
  public static readonly AssetLocation ToggleSwitch = new(
    "game:sounds/toggleswitch"
  );
  public static readonly AssetLocation MePostHit = new(
    "game:sounds/block/meposthit"
  );

  // Fluids / venting
  public static readonly AssetLocation SmallSplash = new(
    "game:sounds/environment/smallsplash"
  );

  /// <summary>Vanilla barrel/container pour — used when manually filling the boiler.</summary>
  public static readonly AssetLocation WaterPour = new(
    "game:sounds/effect/water-pour"
  );
  public static readonly AssetLocation ExtinguishHiss = new(
    "game:sounds/effect/extinguish"
  );

  // Steam-machine ambience / effects
  /// <summary>Cooking-pot bubble loop.</summary>
  public static readonly AssetLocation Cooking = new(
    "game:sounds/effect/cooking"
  );

  /// <summary>Lava bubble/rumble — the ambience of a pressurised gas pipe and a boiling boiler.</summary>
  public static readonly AssetLocation Lava = new(
    "game:sounds/environment/lava"
  );

  /// <summary>Gentle creek babble — repurposed as the ambience of a water-carrying pipe.</summary>
  public static readonly AssetLocation Creek = new(
    "game:sounds/environment/creek"
  );

  /// <summary>Iron-on-iron grind — the engine/sub-machine piston rising (up stroke).</summary>
  public static readonly AssetLocation MetalGrinding = new(
    "game:sounds/effect/metalgrinding"
  );

  /// <summary>Airy swoosh — gas venting from a freshly opened pipe end.</summary>
  public static readonly AssetLocation Swoosh = new(
    "game:sounds/effect/swoosh"
  );

  /// <summary>Torch un-equip whoosh — the engine/sub-machine piston rising (up stroke).</summary>
  public static readonly AssetLocation TorchUnequip = new(
    "game:sounds/held/torch-unequip"
  );

  /// <summary>Anvil merge clang — the engine/sub-machine piston bottoming out (down stroke).</summary>
  public static readonly AssetLocation AnvilMergeHit = new(
    "game:sounds/effect/anvilmergehit"
  );

  /// <summary>Planetary-gear churn — the constant low hum of a running engine's gear housing.</summary>
  public static readonly AssetLocation PlanetaryGears = new(
    "game:sounds/effect/planetary_gears"
  );

  /// <summary>Large explosion — boiler burst (CreateExplosion plays its own, this is a spare).</summary>
  public static readonly AssetLocation LargeExplosion = new(
    "game:sounds/effect/largeexplosion"
  );

  /// <summary>Medium explosion — the muffled blast of an engine bursting.</summary>
  public static readonly AssetLocation MediumExplosion = new(
    "game:sounds/effect/mediumexplosion"
  );

  /// <summary>Small explosion — the muffled pop of a pipe bursting.</summary>
  public static readonly AssetLocation SmallExplosion = new(
    "game:sounds/effect/smallexplosion"
  );

  /// <summary>Plays a one-shot sound centred on <paramref name="pos"/> (server only — replicates to clients).</summary>
  public static void Play(
    ICoreAPI? api,
    BlockPos pos,
    AssetLocation sound,
    float volume = 1f,
    float range = 24f
  )
  {
    if (api == null || api.Side != EnumAppSide.Server)
      return;
    api.World.PlaySoundAt(
      sound,
      pos.X + 0.5,
      pos.Y + 0.5,
      pos.Z + 0.5,
      null,
      true,
      range,
      volume
    );
  }

  /// <summary>
  /// Plays a sound at most once per <paramref name="intervalMs"/> (using world elapsed
  /// time), updating <paramref name="lastMs"/> when it fires. Use for the looping ambience
  /// of ongoing processes so per-second ticks don't spam audio.
  /// </summary>
  public static void PlayThrottled(
    ICoreAPI? api,
    BlockPos pos,
    AssetLocation sound,
    ref long lastMs,
    long intervalMs,
    float volume = 1f,
    float range = 24f
  )
  {
    if (api == null || api.Side != EnumAppSide.Server)
      return;
    long now = api.World.ElapsedMilliseconds;
    if (now - lastMs < intervalMs)
      return;
    lastMs = now;
    api.World.PlaySoundAt(
      sound,
      pos.X + 0.5,
      pos.Y + 0.5,
      pos.Z + 0.5,
      null,
      true,
      range,
      volume
    );
  }

  /// <summary>
  /// Plays a one-shot sound centred on <paramref name="pos"/> with NO side gate — for
  /// client-side, animation-synced sounds that must play locally on each client (e.g. the
  /// piston-stroke sounds tied to the cycle animation's keyframes).
  /// </summary>
  public static void PlayLocal(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    float volume = 1f,
    float range = 16f,
    bool randomizePitch = true
  ) =>
    world.PlaySoundAt(
      sound,
      pos.X + 0.5,
      pos.Y + 0.5,
      pos.Z + 0.5,
      null,
      randomizePitch,
      range,
      volume
    );

  /// <summary>
  /// Plays a sound at most once per <paramref name="intervalMs"/> with NO side gate — a
  /// client-safe throttled loop for ongoing ambience (boiler hum, pipe bubbling/trickle).
  /// Updates <paramref name="lastMs"/> when it fires.
  /// </summary>
  public static void PlayLoop(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    ref long lastMs,
    long intervalMs,
    float volume = 1f,
    float range = 16f
  )
  {
    long now = world.ElapsedMilliseconds;
    if (now - lastMs < intervalMs)
      return;
    lastMs = now;
    world.PlaySoundAt(
      sound,
      pos.X + 0.5,
      pos.Y + 0.5,
      pos.Z + 0.5,
      null,
      false,
      range,
      volume
    );
  }

  /// <summary>
  /// Plays a one-shot sound centred on <paramref name="pos"/> through a world accessor (for
  /// callers that hold an <see cref="IWorldAccessor"/> rather than an API), optionally
  /// excluding <paramref name="byPlayer"/> who triggered it.
  /// </summary>
  public static void PlayAt(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    IPlayer? byPlayer = null,
    bool randomizePitch = true,
    float range = 32f,
    float volume = 1f
  ) =>
    world.PlaySoundAt(
      sound,
      pos.X + 0.5,
      pos.Y + 0.5,
      pos.Z + 0.5,
      byPlayer,
      randomizePitch,
      range,
      volume
    );

  /// <summary>
  /// Plays a sound only <paramref name="chance"/> (0–1) of the time, so a recurring event
  /// (a spill, a leak hiss) is audible without a constant roar.
  /// </summary>
  public static void PlayChance(
    IWorldAccessor world,
    BlockPos pos,
    AssetLocation sound,
    double chance,
    bool randomizePitch = true,
    float range = 32f,
    float volume = 1f
  )
  {
    if (world.Rand.NextDouble() >= chance)
      return;
    world.PlaySoundAt(
      sound,
      pos.X + 0.5,
      pos.Y + 0.5,
      pos.Z + 0.5,
      null,
      randomizePitch,
      range,
      volume
    );
  }

  /// <summary>
  /// Creates a gapless, positioned looping ambient sound (client only — returns null on the
  /// server). The caller owns the returned handle: <c>Start()</c> / <c>Stop()</c> it on state
  /// changes and <c>Dispose()</c> it on unload. Use for a machine's constant running hum,
  /// where re-firing one-shots would gap or stack instead of looping seamlessly.
  /// </summary>
  public static ILoadedSound? CreateLoop(
    ICoreAPI? api,
    BlockPos pos,
    AssetLocation sound,
    float volume = 1f,
    float range = 16f,
    float pitch = 1f
  )
  {
    if (api is not ICoreClientAPI capi)
      return null;
    return capi.World.LoadSound(
      new SoundParams
      {
        Location = sound,
        ShouldLoop = true,
        Position = new Vec3f(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f),
        DisposeOnFinish = false,
        Volume = volume,
        Range = range,
        Pitch = pitch,
        RelativePosition = false,
      }
    );
  }

  /// <summary>Plays a quiet splash ~30% of the time, so a spill is audible without a roar.</summary>
  public static void SplashSound(IWorldAccessor world, BlockPos pos) =>
    PlayChance(world, pos, SmallSplash, 0.3);

  /// <summary>Plays a soft steam/gas hiss ~30% of the time, so venting gas is audible without a constant roar.</summary>
  public static void HissSound(IWorldAccessor world, BlockPos pos) =>
    PlayChance(world, pos, ExtinguishHiss, 0.3, range: 24f, volume: 0.5f);
}
