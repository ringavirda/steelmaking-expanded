using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>Block entity for the gas outlet; behaves as a plain pipe node that also acts as a gas producer.</summary>
[EntityRegister]
public class BlockEntityPipeOutlet : BlockEntityPipe, IGasProducer { }
