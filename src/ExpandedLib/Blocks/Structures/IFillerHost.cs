using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks.Structures;

/// <summary>
/// A mega-block whose footprint cells are declared by its JSON <c>fillerOffsets</c> attribute,
/// surfaced through the generated <c>FillerOffsets</c> member.
/// <see cref="StructureFillers.FootprintCells"/> consumes this contract instead of reading the
/// attribute by name, so the attribute key lives only in the generated accessor.
/// <para>
/// A block satisfies this automatically: the attribute source generator already emits a matching
/// <c>public JsonObject? FillerOffsets</c>, so a concrete block only adds <c>IFillerHost</c> to its
/// class declaration (the generated member implements it). Abstract bases that place fillers for their
/// concrete subclasses (e.g. the engine/boiler bases) cast <c>this</c> to <c>IFillerHost</c>.
/// </para>
/// </summary>
public interface IFillerHost {
  /// <summary>The block's <c>fillerOffsets</c> JSON node (the generated accessor), or null if none.</summary>
  JsonObject? FillerOffsets { get; }
}
