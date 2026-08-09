using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks.Structures;

/// <summary>
/// A behaviour a mega-block can host on one of its footprint cells (see
/// <see cref="StructureFillers"/>). The shared <see cref="BlockStructureFiller"/> renders nothing
/// and carries no behaviours of its own, but a <c>fillerOffsets</c> cell can declare one or more
/// <see cref="FillerBehavior"/>s; the filler block entity instantiates each by class code and, when
/// it implements this interface, hands it the principal's position and the cell's already-rotated
/// connector face, so the behaviour orients itself around the principal rather than the filler's
/// own always-north variant.
/// </summary>
public interface IFillerHostedBehavior {
  /// <summary>
  /// Called once, before the behaviour's own <c>Initialize</c>, with the principal block's position
  /// (or null if the cell is orphaned) and the cell's connector face already rotated into the placed
  /// orientation (null when the declaration carried no face). <paramref name="properties"/> is the
  /// behaviour's declared JSON config, or null.
  /// </summary>
  void ConfigureFromFiller(
    BlockPos? principal,
    BlockFacing? connectorFace,
    JsonObject? properties
  );
}

/// <summary>
/// A single behaviour declared on a <c>fillerOffsets</c> cell: the registered class
/// <see cref="Code"/> (e.g. <c>exlib.BEBehaviorMPFillerPort</c>), an optional north-orientation
/// <see cref="ConnectorFace"/> (rotated into the placed orientation by
/// <see cref="StructureFillers.FootprintCells"/>) and optional <see cref="Properties"/> passed
/// through to the behaviour. Stored on the filler block entity and recreated on load.
/// </summary>
public readonly record struct FillerBehavior(
  string Code,
  BlockFacing? ConnectorFace,
  JsonObject? Properties
);
