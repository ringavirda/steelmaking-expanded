using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>
/// Ambient conditions read from the world, for machines that need a "cold" reference: the resting
/// temperature of an unlit hearth, of gas in an unheated main, and the floor a regenerator decays
/// back toward.
/// </summary>
public static class ExClimate {
  /// <summary>
  /// Fallback when the world has no climate to report - off-server, before the chunk is loaded, or
  /// in a headless fixture. Room temperature, which is what the machines assumed before they asked.
  /// </summary>
  public const float DefaultAmbient = 20f;

  /// <summary>
  /// Air temperature (°C) at <paramref name="pos"/> right now, so a furnace in the snow starts
  /// colder than one in a jungle and a stove parked outside loses its charge to the season it is
  /// standing in. Falls back to <see cref="DefaultAmbient"/> when there is no climate to read.
  /// </summary>
  public static float AmbientAt(IWorldAccessor? world, BlockPos? pos) {
    if (world?.BlockAccessor == null || pos == null)
      return DefaultAmbient;
    ClimateCondition? climate = world.BlockAccessor.GetClimateAt(
      pos,
      EnumGetClimateMode.NowValues
    );
    return climate?.Temperature ?? DefaultAmbient;
  }

  /// <inheritdoc cref="AmbientAt(IWorldAccessor, BlockPos)"/>
  public static float AmbientAt(BlockEntity? be) =>
    AmbientAt(be?.Api?.World, be?.Pos);
}
