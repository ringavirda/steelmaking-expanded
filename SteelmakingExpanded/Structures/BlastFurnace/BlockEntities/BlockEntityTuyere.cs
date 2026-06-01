using SteelmakingExpanded.Networks.Gas;
using SteelmakingExpanded.Networks.Gas.BlockEntities;

namespace SteelmakingExpanded.Structures.BlastFurnace.BlockEntities;

/// <summary>Block entity for the tuyere; a gas-pipe node that the furnace draws air/blast from as a consumer.</summary>
public class BlockEntityTuyere : BlockEntityGasPipe, IGasConsumer { }
