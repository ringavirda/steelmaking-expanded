using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Items;

/// <summary>Blast mix item (crushed iron ore + coke + flux); piles into a coal pile that fuels the blast furnace.</summary>
[EntityRegister]
public class ItemBlastmix : ItemPileable
{
  protected override AssetLocation PileBlockCode => new("coalpile");
}
