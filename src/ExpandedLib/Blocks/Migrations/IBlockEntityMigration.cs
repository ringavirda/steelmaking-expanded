using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks.Migrations;

/// <summary>
/// Optional companion to <see cref="IBlockCodeMigration"/>, implemented on the same class when the
/// block carries BE state that must survive the swap (inventory, progress, …). Without it the swap
/// is a plain block-id replace; with it, the old BE's serialized tree is read before the swap and
/// handed to <see cref="MigrateBlockEntity"/> to copy or reshape onto the new BE.
/// </summary>
public interface IBlockEntityMigration {
  /// <summary>
  /// Called immediately after the new block is placed, for each migrated position.
  /// </summary>
  /// <param name="oldCode">The legacy block code that was found.</param>
  /// <param name="newCode">The replacement block code that was placed.</param>
  /// <param name="oldState">
  /// The old block entity's serialized tree, or <c>null</c> if the position had no block
  /// entity (e.g. the old block-entity class no longer exists).
  /// </param>
  /// <param name="newBlockEntity">
  /// The block entity of the just-placed replacement block. Mutate it directly - e.g.
  /// <c>newBlockEntity.FromTreeAttributes(oldState, world)</c> for a verbatim copy, or
  /// rename/convert fields first - then it is marked dirty for you.
  /// </param>
  /// <param name="world">The world accessor (for resolving stacks during deserialization).</param>
  void MigrateBlockEntity(
    AssetLocation oldCode,
    AssetLocation newCode,
    ITreeAttribute? oldState,
    BlockEntity newBlockEntity,
    IWorldAccessor world
  );
}
