using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded;

/// <summary>
/// Shared sound asset locations and small server-side play helpers used across
/// the mod. All sounds resolve from the vanilla "game" domain (which also covers
/// the survival asset folder). Playing on the server replicates to nearby clients.
/// </summary>
internal static class SmexSounds
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
  public static readonly AssetLocation Bellows = new(
    "game:sounds/effect/bellows"
  );
  public static readonly AssetLocation Ingot = new("game:sounds/block/ingot");
  public static readonly AssetLocation AnvilHit = new(
    "game:sounds/effect/anvilhit1"
  );
  public static readonly AssetLocation Build = new("game:sounds/player/build");
  public static readonly AssetLocation StoneCrush = new(
    "game:sounds/effect/stonecrush"
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
  /// Plays a sound at most once per <paramref name="intervalMs"/> (using world
  /// elapsed time), updating <paramref name="lastMs"/> when it fires. Use for the
  /// looping ambience of ongoing processes so per-second ticks don't spam audio.
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
}
