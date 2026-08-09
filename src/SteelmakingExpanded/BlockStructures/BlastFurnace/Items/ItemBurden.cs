using ExpandedLib.Registries.Entities;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Items;

/// <summary>Burden item (crushed iron ore + coke + flux); piles into a coal pile that fuels the blast furnace.</summary>
[ItemRegister]
public partial class ItemBurden : ItemPileable {
  protected override AssetLocation PileBlockCode => new("coalpile");
}
