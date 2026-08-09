using Vintagestory.API.Common;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>
/// A block entity that holds molten metal which, once solidified and cooled, can be chipped out with a
/// chisel + hammer instead of breaking the whole block (canal cells, the molten barrel, the bessemer
/// charge). Implementing this lets the shared <see cref="MoltenChisel"/> drive the interaction ritual -
/// tool gating, the "not cool enough yet" feedback, the recovered drop, tool wear and sound - so each
/// holder only supplies the content-specific state and the clear-and-recover step.
/// </summary>
public interface IChiselableMolten {
  /// <summary>
  /// Whether there is solidified content here at all. Gates whether a chisel + hammer click is
  /// <em>claimed</em> by the chisel-out (and possibly explained as "too hot") or falls through to the
  /// holder's other interactions. False when empty or still liquid.
  /// </summary>
  bool HasChiselableContent { get; }

  /// <summary>
  /// Whether the content can actually be chipped out right now: solidified, cooled past the hardened
  /// threshold, and (where the holder caps it) small enough. False while it is still too hot to chip.
  /// </summary>
  bool CanChiselOut { get; }

  /// <summary>
  /// The <c>game:ingameerror-*</c> code to surface when the player tries to chisel
  /// (<see cref="HasChiselableContent"/>) but <see cref="CanChiselOut"/> is false - e.g. "too hot" or
  /// "too full". <c>null</c> to claim the click silently with no message.
  /// </summary>
  string? ChiselBlockedError { get; }

  /// <summary>
  /// Server-side: chips the hardened content out, clearing it from the holder, and returns the
  /// recovered metal-bit drop (or <c>null</c> when there is nothing to recover). Only called once
  /// <see cref="CanChiselOut"/> has been confirmed.
  /// </summary>
  ItemStack? ChiselOut();
}
