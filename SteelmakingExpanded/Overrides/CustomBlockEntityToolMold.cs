using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Overrides;

/// <summary>
/// Drop-in replacement for the vanilla <see cref="BlockEntityToolMold"/> that
/// applies a stored <c>blockEntityAttributes</c> tree when the mold is placed
/// in the world.
///
/// Vanilla tool molds never carry contents inside their item stack (you can
/// only pick up an empty mold), so the base class has no <c>OnBlockPlaced</c>
/// hook that reads such data. The mold pedestal hands back a filled-mold item
/// stack, so without this override the metal silently vanishes the moment the
/// mold is placed as a normal block. This mirrors what
/// <c>BlockMoltenBarrel.OnBlockPlaced</c> does for barrels.
/// </summary>
public class CustomBlockEntityToolMold : BlockEntityToolMold
{
  public override void OnBlockPlaced(ItemStack? byItemStack = null)
  {
    base.OnBlockPlaced(byItemStack);

    if (
      byItemStack?.Attributes?.GetTreeAttribute("blockEntityAttributes")
      is not ITreeAttribute beData
    )
      return;

    // Assign the mold-specific fields directly rather than calling
    // FromTreeAttributes: the base implementation rebuilds Pos from
    // posx/posy/posz, which this partial tree lacks, so it would corrupt the
    // block entity's position to (0,0,0) and orphan it on the next reload.
    MetalContent = beData.GetItemstack("contents");
    MetalContent?.ResolveBlockOrItem(Api.World);
    FillLevel = beData.GetInt("fillLevel");
    Shattered = beData.GetBool("shattered");

    UpdateRenderer();
    MarkDirty(true);
  }
}
