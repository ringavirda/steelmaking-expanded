using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.BlockMigrations;

/// <summary>
/// Optional companion to <see cref="IBlockCodeMigration"/>. Implement it on the same
/// migration class when the block carries block-entity state that must survive the
/// swap (inventory, accumulated progress, …) instead of being discarded.
///
/// <para>Without this interface <see cref="BlockMigrationModSystem"/> does a plain
/// block-id swap, which is correct for stateless blocks such as network nodes. With it,
/// the system reads the old block entity's serialized tree just before replacing the
/// block and calls <see cref="MigrateBlockEntity"/> so you can copy or reshape that
/// state onto the freshly placed block entity.</para>
/// </summary>
public interface IBlockEntityMigration
{
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
  /// The block entity of the just-placed replacement block. Mutate it directly — e.g.
  /// <c>newBlockEntity.FromTreeAttributes(oldState, world)</c> for a verbatim copy, or
  /// rename/convert fields first — then it is marked dirty for you.
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
