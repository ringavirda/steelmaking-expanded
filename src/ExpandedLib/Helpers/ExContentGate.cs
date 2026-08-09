using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>
/// Generic mechanism for a config-gated "disable this content" toggle: hide registered blocks/items
/// from the creative inventory and handbook, and strip the recipes that produce them. The caller
/// (a mod deciding a feature is off) supplies the predicate that selects what to gate; this owns the
/// how. Call after recipes have resolved - i.e. from a mod system's
/// <c>StartServerSide</c>/<c>StartClientSide</c>, not <c>Start</c>.
/// </summary>
public static class ExContentGate {
  /// <summary>
  /// Hides every collectible matching <paramref name="match"/> from the creative inventory and the
  /// handbook by clearing its creative tabs and stacks - the handbook lists nothing for a collectible
  /// that has neither (see <c>CollectibleObject.GetHandBookStacks</c>). Returns the number hidden.
  /// The creative inventory and handbook are client-built, so this matters on the client; it is a
  /// harmless no-op effect on the server.
  /// </summary>
  public static int HideFromCreativeAndHandbook(
    ICoreAPI api,
    System.Func<CollectibleObject, bool> match
  ) {
    int hidden = 0;
    foreach (var obj in AllCollectibles(api)) {
      if (obj?.Code == null || !match(obj))
        continue;
      // Empty, never null. The asset loader always leaves CreativeInventoryTabs as an array, and
      // game code relies on that: AttachableInteractionHelp.GetOrCreateInteractionHelp reads
      // .Length on it with no null guard while scanning every collectible, so a null here crashes
      // the client the moment a player looks at any attachable entity (a boat, a raft, a mount).
      // An empty array hides the collectible exactly as a null would - GetHandBookStacks tests
      // Length, not nullity - without breaking that invariant.
      obj.CreativeInventoryTabs = [];
      // Left null on purpose: null is the loader's own default for a collectible that declares no
      // creativeinventoryStacks, so it is a state game code already handles. An empty array here
      // would instead read as "has stacks" to the same non-null test above.
      obj.CreativeInventoryStacks = null;
      hidden++;
    }
    return hidden;
  }

  /// <summary>Removes every clay-forming recipe whose output code matches <paramref name="outputMatch"/>.
  /// Returns the count removed.</summary>
  public static int RemoveClayformingRecipes(
    ICoreAPI api,
    System.Func<AssetLocation, bool> outputMatch
  ) =>
    api.GetClayformingRecipes()
      .RemoveAll(r => r.Output?.Code is { } c && outputMatch(c));

  /// <summary>Removes every grid (crafting) recipe whose output code matches <paramref name="outputMatch"/>.
  /// Returns the count removed.</summary>
  public static int RemoveGridRecipes(
    ICoreAPI api,
    System.Func<AssetLocation, bool> outputMatch
  ) =>
    api.World.GridRecipes.RemoveAll(r =>
      r.Output?.Code is { } c && outputMatch(c)
    );

  private static IEnumerable<CollectibleObject> AllCollectibles(ICoreAPI api) =>
    api
      .World.Blocks.Cast<CollectibleObject>()
      .Concat(api.World.Items.Cast<CollectibleObject>());
}
