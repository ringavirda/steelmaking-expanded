using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>Block entity for the gas passthrough; behaves as a plain pipe node that also acts as a gas consumer.</summary>
[EntityRegister]
public class BlockEntityPipePassthrough : BlockEntityPipe, IPipeConsumer { }
