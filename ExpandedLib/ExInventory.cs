using Vintagestory.API.Common;
using Math = System.Math;

namespace ExpandedLib;

/// <summary>
/// Shared inventory queries for machine costs: counting and consuming player items that
/// match a predicate. One implementation for both the whole-inventory scope (engine
/// repairs) and the hotbar-only scope (converter spawning), so each machine no longer
/// hand-rolls its own walk/take loops.
/// </summary>
public static class ExInventory
{
  /// <summary>Total stack size of all items in the player's inventories matching <paramref name="matches"/>.</summary>
  public static int Count(IPlayer player, System.Func<ItemStack, bool> matches)
  {
    int count = 0;
    player.Entity.WalkInventory(slot =>
    {
      if (slot?.Itemstack != null && matches(slot.Itemstack))
        count += slot.Itemstack.StackSize;
      return true;
    });
    return count;
  }

  /// <summary>
  /// Removes up to <paramref name="quantity"/> matching items from the player's
  /// inventories. Returns the amount actually taken.
  /// </summary>
  public static int Take(
    IPlayer player,
    System.Func<ItemStack, bool> matches,
    int quantity
  )
  {
    int remaining = quantity;
    player.Entity.WalkInventory(slot =>
    {
      if (remaining <= 0)
        return false;
      if (slot?.Itemstack != null && matches(slot.Itemstack))
      {
        int take = Math.Min(remaining, slot.Itemstack.StackSize);
        slot.TakeOut(take);
        slot.MarkDirty();
        remaining -= take;
      }
      return remaining > 0;
    });
    return quantity - remaining;
  }

  /// <summary>Total stack size of matching items in the player's hotbar only.</summary>
  public static int CountHotbar(
    IPlayer player,
    System.Func<ItemStack, bool> matches
  )
  {
    int count = 0;
    var hotbar = player.InventoryManager?.GetHotbarInventory();
    if (hotbar == null)
      return 0;
    foreach (var slot in hotbar)
      if (slot?.Itemstack != null && matches(slot.Itemstack))
        count += slot.Itemstack.StackSize;
    return count;
  }

  /// <summary>
  /// Removes up to <paramref name="quantity"/> matching items from the player's hotbar
  /// only. Returns the amount actually taken.
  /// </summary>
  public static int TakeHotbar(
    IPlayer player,
    System.Func<ItemStack, bool> matches,
    int quantity
  )
  {
    int remaining = quantity;
    var hotbar = player.InventoryManager?.GetHotbarInventory();
    if (hotbar == null)
      return 0;
    foreach (var slot in hotbar)
    {
      if (remaining <= 0)
        break;
      if (slot?.Itemstack != null && matches(slot.Itemstack))
      {
        int take = Math.Min(remaining, slot.Itemstack.StackSize);
        slot.TakeOut(take);
        slot.MarkDirty();
        remaining -= take;
      }
    }
    return quantity - remaining;
  }
}
