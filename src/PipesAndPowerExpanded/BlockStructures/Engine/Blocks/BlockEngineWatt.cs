using ExpandedLib.Blocks.Structures;
using ExpandedLib.Registries.Entities;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The Watt engine mega-block (iron, low-pressure tier). No control rods. Repairs accept
/// iron or steel. All behavior lives in <see cref="BlockEngine"/>.
/// </summary>
[BlockRegister]
public partial class BlockEngineWatt : BlockEngine, IFillerHost, IEngineGeometry {
  protected override RepairItem[] RepairItems =>
    [
      new(["metalplate-iron", "metalplate-steel"], 4, "iron/steel plate"),
      new(["rod-iron", "rod-steel"], 2, "iron/steel rod"),
    ];
}
