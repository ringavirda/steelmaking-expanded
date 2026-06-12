using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The Watt engine mega-block (iron, low-pressure tier). No control rods. Repairs accept
/// iron or steel. All behavior lives in <see cref="BlockEngine"/>.
/// </summary>
[EntityRegister]
public class BlockEngineWatt : BlockEngine
{
  protected override RepairItem[] RepairItems =>
    [
      new(["metalplate-iron", "metalplate-steel"], 4, "iron/steel plate"),
      new(["rod-iron", "rod-steel"], 2, "iron/steel rod"),
    ];
}
