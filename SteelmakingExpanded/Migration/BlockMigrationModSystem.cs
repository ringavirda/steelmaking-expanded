using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.Migration;

/// <summary>
/// Generic, server-side world migrator for renamed/re-variantted blocks. Collects every
/// <see cref="IBlockCodeMigration"/> in the mod assembly into a single old-block-id →
/// new-block-id table and rewrites matching blocks as chunk columns load.
///
/// <para>How it works: when a block code is removed, the engine keeps every placed
/// instance as a "missing" placeholder block that retains the original code (see
/// <c>ServerSystemBlockIdRemapper</c>), so no data is lost to air. This system looks the
/// old codes up by id, and on each <see cref="IServerEventAPI.ChunkColumnLoaded"/> swaps
/// the stored block id for the new one via <see cref="IBlockAccessor.SetBlock(int,
/// BlockPos)"/>. The replacement gets a fresh block entity that reconstructs its state
/// (e.g. a network node's orientation/connectors) from the new variant code.</para>
///
/// <para>By default this is a plain block-id swap that discards block-entity state,
/// which is appropriate for stateless blocks such as network nodes. A migration that
/// also implements <see cref="IBlockEntityMigration"/> opts into block-entity handling:
/// the old block entity's tree is read just before the swap and handed to the migration
/// to copy or reshape onto the new block entity.</para>
/// </summary>
public class BlockMigrationModSystem : ModSystem
{
  /// <summary>One resolved remap target for a given legacy block code.</summary>
  private readonly record struct RemapEntry(
    Block NewBlock,
    AssetLocation OldCode,
    AssetLocation NewCode,
    IBlockEntityMigration? BlockEntityMigration
  );

  private ICoreServerAPI _sapi = null!;

  // Legacy block code -> replacement, merged across all discovered migrations. Keyed by
  // code (not id) because the engine can renumber block ids when it reconciles the save
  // mapping on load, so a precomputed id would not match what is actually in the chunk.
  private readonly Dictionary<AssetLocation, RemapEntry> _remap = [];
  private bool _initialized;

  // Only the server owns world block data; the client has nothing to migrate.
  public override bool ShouldLoad(EnumAppSide side) =>
    side == EnumAppSide.Server;

  public override void StartServerSide(ICoreServerAPI api)
  {
    _sapi = api;
    // Built lazily on the first chunk load: by then every block — including the
    // missing-block placeholders created during world load — is registered.
    api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
  }

  private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
  {
    if (!_initialized)
    {
      BuildRemapTable();
      _initialized = true;
      if (_remap.Count == 0)
      {
        // Nothing in this world matches any migration — stop listening entirely.
        _sapi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
        return;
      }
    }

    const int cs = GlobalConstants.ChunkSize;
    IBlockAccessor ba = _sapi.World.BlockAccessor;
    int migrated = 0;

    for (int cy = 0; cy < chunks.Length; cy++)
    {
      IWorldChunk chunk = chunks[cy];
      if (chunk == null)
        continue;
      chunk.Unpack();
      IChunkBlocks data = chunk.Data;
      int len = data.Length;

      for (int i = 0; i < len; i++)
      {
        int id = data[i];
        if (id == 0 || !_remap.TryGetValue(id, out RemapEntry entry))
          continue;

        // index3d layout: ((y * cs) + z) * cs + x
        int x = i % cs;
        int z = i / cs % cs;
        int y = i / (cs * cs);

        BlockPos pos = new(
          chunkCoord.X * cs + x,
          cy * cs + y,
          chunkCoord.Y * cs + z
        );

        ReplaceBlock(ba, pos, entry);
        migrated++;
      }
    }

    if (migrated > 0)
      _sapi.Logger.Notification(
        "[smex] Migrated {0} block(s) in chunk column {1},{2}.",
        migrated,
        chunkCoord.X,
        chunkCoord.Y
      );
  }

  private void BuildRemapTable()
  {
    var byCode = new Dictionary<AssetLocation, int>();
    foreach (Block? block in _sapi.World.Blocks)
    {
      if (block?.Code != null)
        byCode[block.Code] = block.BlockId;
    }

    foreach (IBlockCodeMigration migration in DiscoverMigrations())
    {
      var beMigration = migration as IBlockEntityMigration;
      int count = 0;
      foreach (var (oldCode, newCode) in migration.GetRemaps(_sapi))
      {
        if (
          !byCode.TryGetValue(oldCode, out int oldId)
          || oldId == 0
          || !byCode.TryGetValue(newCode, out int newId)
          || newId == 0
        )
          continue;

        if (
          _remap.TryGetValue(oldId, out RemapEntry existing)
          && existing.NewBlockId != newId
        )
        {
          _sapi.Logger.Warning(
            "[smex] Migration '{0}' remaps {1} but it is already remapped elsewhere; keeping the first mapping.",
            migration.Name,
            oldCode
          );
          continue;
        }

        _remap[oldId] = new RemapEntry(newId, oldCode, newCode, beMigration);
        count++;
      }

      if (count > 0)
        _sapi.Logger.Notification(
          "[smex] Migration '{0}': {1} legacy block code(s) found to update.",
          migration.Name,
          count
        );
    }
  }

  /// <summary>
  /// Swaps the block at <paramref name="pos"/> for its replacement. For a plain
  /// (code-only) migration this is a bare <see cref="IBlockAccessor.SetBlock(int,
  /// BlockPos)"/>; when the migration handles block-entity state, the old entity's tree
  /// is captured first and applied to the new entity afterwards.
  /// </summary>
  private void ReplaceBlock(IBlockAccessor ba, BlockPos pos, RemapEntry entry)
  {
    if (entry.BlockEntityMigration == null)
    {
      ba.SetBlock(entry.NewBlockId, pos);
      return;
    }

    ITreeAttribute? oldState = null;
    if (ba.GetBlockEntity(pos) is BlockEntity oldBe)
    {
      oldState = new TreeAttribute();
      oldBe.ToTreeAttributes(oldState);
    }

    ba.SetBlock(entry.NewBlockId, pos);

    if (ba.GetBlockEntity(pos) is BlockEntity newBe)
    {
      entry.BlockEntityMigration.MigrateBlockEntity(
        entry.OldCode,
        entry.NewCode,
        oldState,
        newBe,
        _sapi.World
      );
      newBe.MarkDirty(true);
    }
  }

  private static IEnumerable<IBlockCodeMigration> DiscoverMigrations() =>
    typeof(BlockMigrationModSystem)
      .Assembly.GetTypes()
      .Where(t =>
        typeof(IBlockCodeMigration).IsAssignableFrom(t)
        && t is { IsAbstract: false, IsInterface: false }
        && t.GetConstructor(Type.EmptyTypes) != null
      )
      .Select(t => (IBlockCodeMigration)Activator.CreateInstance(t)!);
}
