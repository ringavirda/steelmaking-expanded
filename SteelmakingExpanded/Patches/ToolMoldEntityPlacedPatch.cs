using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Patches;

/// <summary>
/// Harmony patch that applies a stored <c>blockEntityAttributes</c> tree when a
/// tool mold is placed back into the world.
///
/// Vanilla tool molds never carry contents inside their item stack (you can
/// only pick up an empty mold), so the base class has no <c>OnBlockPlaced</c>
/// hook that reads such data. The mold pedestal hands back a filled-mold item
/// stack, so without this the metal silently vanishes the moment the mold is
/// placed as a normal block. This mirrors what
/// <c>BlockMoltenBarrel.OnBlockPlaced</c> does for barrels.
///
/// <see cref="BlockEntityToolMold"/> does not declare <c>OnBlockPlaced</c>, so
/// the patch targets the <see cref="BlockEntity"/> base and guards on the
/// instance type.
/// </summary>
[HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.OnBlockPlaced))]
public static class ToolMoldEntityPlacedPatch
{
  public static void Postfix(BlockEntity __instance, ItemStack? byItemStack)
  {
    if (__instance is not BlockEntityToolMold be)
      return;

    if (
      byItemStack?.Attributes?.GetTreeAttribute("blockEntityAttributes")
      is not ITreeAttribute beData
    )
      return;

    // Assign the mold-specific fields directly rather than calling
    // FromTreeAttributes: the base implementation rebuilds Pos from
    // posx/posy/posz, which this partial tree lacks, so it would corrupt the
    // block entity's position to (0,0,0) and orphan it on the next reload.
    be.MetalContent = beData.GetItemstack("contents");
    be.MetalContent?.ResolveBlockOrItem(be.Api.World);
    be.FillLevel = beData.GetInt("fillLevel");
    be.Shattered = beData.GetBool("shattered");

    be.UpdateRenderer();
    be.MarkDirty(true);
  }
}
