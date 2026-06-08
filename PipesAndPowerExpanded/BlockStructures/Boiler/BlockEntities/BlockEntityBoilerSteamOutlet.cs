using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;

/// <summary>
/// Block entity for the boiler steam outlet. The outlet is a fixed network
/// connector (not a graph node), so this entity only records the boiler it is
/// bound to; steam production is driven from the boiler.
/// </summary>
[EntityRegister]
public class BlockEntityBoilerSteamOutlet : BlockEntity
{
  /// <summary>The boiler this outlet is bound to, if any.</summary>
  public BlockPos? BoilerPos { get; set; }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    if (BoilerPos != null)
    {
      tree.SetInt("boilerX", BoilerPos.X);
      tree.SetInt("boilerY", BoilerPos.Y);
      tree.SetInt("boilerZ", BoilerPos.Z);
    }
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    BoilerPos = tree.HasAttribute("boilerX")
      ? new BlockPos(
        tree.GetInt("boilerX"),
        tree.GetInt("boilerY"),
        tree.GetInt("boilerZ")
      )
      : null;
  }
}
