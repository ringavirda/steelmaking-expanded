using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SteelmakingExpanded.Structures.BlastFurnace.BlockEntities;

/// <summary>Block entity for the solidified-slag block; tracks how many slag units it will drop.</summary>
public class BlockEntitySlag : BlockEntity
{
  /// <summary>Number of slag units stored, used to scale the break drop.</summary>
  public int SlagCount { get; set; } = 0;

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(Lang.Get("smex:slag-info-count", SlagCount));
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetInt("slagCount", SlagCount);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    SlagCount = tree.GetInt("slagCount", 0);
  }
}
