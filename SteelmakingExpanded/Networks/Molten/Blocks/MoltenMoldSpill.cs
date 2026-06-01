using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Networks.Molten.Blocks;

/// <summary>
/// Shared rule: a tool mold holding still-molten (liquid) metal may only be
/// safely carried in the active hand. Anywhere else — another hotbar slot, a
/// backpack, a chest, a mold rack — the liquid metal spills out, emptying the
/// mold. Hardened/cooling castings are unaffected.
/// </summary>
public static class MoltenMoldSpill
{
  /// <summary>Error code used for the spill notification.</summary>
  public const string ErrorCode = "moltenspill";

  /// <summary>Lang key for the spill message, resolved client-side by SendIngameError.</summary>
  public const string ErrorMessage = "smex:moltenspill-error";

  /// <summary>
  /// If <paramref name="slot"/> holds a tool mold with molten metal, strips its
  /// contents and (optionally) notifies the player. Returns true if it spilled.
  /// </summary>
  public static bool SpillIfMolten(
    ItemSlot? slot,
    IWorldAccessor world,
    IServerPlayer? notify
  )
  {
    if (slot?.Itemstack is not { } stack || stack.Block is not BlockToolMold)
      return false;

    var beData = stack.Attributes?.GetTreeAttribute("blockEntityAttributes");
    var contents = beData?.GetItemstack("contents");
    int fill = beData?.GetInt("fillLevel") ?? 0;
    if (contents == null || fill <= 0)
      return false;

    contents.ResolveBlockOrItem(world);
    if (contents.Collectible == null)
      return false;

    float temp = contents.Collectible.GetTemperature(world, contents);
    float meltPoint = contents.Collectible.GetMeltingPoint(
      world,
      null,
      new DummySlot(contents)
    );
    if (temp <= 0.8f * meltPoint)
      return false;

    stack.Attributes?.RemoveAttribute("blockEntityAttributes");
    slot.MarkDirty();
    notify?.SendIngameError(ErrorCode, ErrorMessage);
    return true;
  }
}
