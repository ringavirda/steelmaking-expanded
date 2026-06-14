using System.Text;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;

/// <summary>
/// Block entity for the solidified-iron block; tracks how many iron bits it
/// will drop.
/// </summary>
[EntityRegister]
public class BlockEntitySolidifiedIron : BlockEntity
{
  /// <summary>Number of iron bits stored, used to scale the break drop.</summary>
  public int IronCount { get; set; } = 2;

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(Lang.Get("smex:solidifiediron-info-count", IronCount));
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetInt("ironCount", IronCount);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    IronCount = tree.GetInt("ironCount", 2);
  }
}
