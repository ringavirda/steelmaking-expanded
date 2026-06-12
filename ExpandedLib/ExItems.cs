using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib;

/// <summary>
/// Shared item-stack lookups for interaction help. Results are cached in the API's
/// <c>ObjectCache</c>, which lives exactly as long as the game session/world — a plain
/// static cache would hand out stacks of a previous world's items after rejoining.
/// </summary>
public static class ExItems
{
  private const string WrenchCacheKey = "exlib:wrenchStacks";

  /// <summary>
  /// One stack per registered wrench item (code path containing "wrench" — the same
  /// test the engine repair applies to the held tool), for "rotate"/"repair"
  /// interaction-help icons.
  /// </summary>
  public static ItemStack[] WrenchStacks(IWorldAccessor world)
  {
    if (
      world.Api.ObjectCache.TryGetValue(WrenchCacheKey, out object? cached)
      && cached is ItemStack[] stacks
    )
      return stacks;

    ItemStack[] built =
    [
      .. world
        .Items.Where(i => i.Code?.Path?.Contains("wrench") == true)
        .Select(i => new ItemStack(i)),
    ];
    world.Api.ObjectCache[WrenchCacheKey] = built;
    return built;
  }
}
