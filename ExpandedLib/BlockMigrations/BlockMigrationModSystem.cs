using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.BlockMigrations;

/// <summary>
/// Generic, server-side world migrator for renamed/re-variantted blocks. Collects every
/// <see cref="IBlockCodeMigration"/> in the mod assembly into a single legacy-code →
/// replacement-block table and rewrites matching blocks as chunk columns load.
///
/// <para>How it works: when a block code is removed, the engine keeps every placed
/// instance as a "missing" placeholder block that retains the original code (see
/// <c>ServerSystemBlockIdRemapper</c>), so no data is lost to air. On each
/// <see cref="IServerEventAPI.ChunkColumnLoaded"/> this system resolves the live block
/// for every stored id and matches on its <see cref="Block.Code"/> — not a precomputed
/// id, because the engine can renumber block ids while reconciling the save mapping on
/// load — then swaps in the replacement via <see cref="IBlockAccessor.SetBlock(int,
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

  /// <summary>Log prefix, e.g. "[smex]" / "[ppex]" — the owning mod's id.</summary>
  private string Tag => "[" + Mod.Info.ModID + "]";

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
    // Two passes are needed because the spawn-area chunks are already loaded by the time
    // this event is wired up, so ChunkColumnLoaded never fires for them:
    //   - a one-time sweep of all currently-loaded chunks once the world is up, and
    //   - the event handler for every chunk column that loads afterwards.
    api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, SweepLoadedChunks);
    api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
  }

  /// <summary>Builds the remap table on first use; returns false if nothing to migrate.</summary>
  private bool EnsureInitialized()
  {
    if (!_initialized)
    {
      BuildRemapTable();
      _initialized = true;
    }
    return _remap.Count > 0;
  }

  private void SweepLoadedChunks()
  {
    if (!EnsureInitialized())
      return;

    int chunksTall = _sapi.WorldManager.MapSizeY / GlobalConstants.ChunkSize;
    int total = 0;

    // Copy the keys: ReplaceBlock mutates chunks, and we do not want to enumerate the
    // live dictionary while that happens.
    foreach (
      long index2d in _sapi.WorldManager.AllLoadedMapchunks.Keys.ToArray()
    )
    {
      Vec2i coord = _sapi.WorldManager.MapChunkPosFromChunkIndex2D(index2d);
      int migrated = 0;
      for (int cy = 0; cy < chunksTall; cy++)
        migrated += ScanChunk(
          coord.X,
          cy,
          coord.Y,
          _sapi.WorldManager.GetChunk(coord.X, cy, coord.Y)
        );

      if (migrated > 0)
        LogColumn(migrated, coord.X, coord.Y);
      total += migrated;
    }

    if (total > 0)
      _sapi.Logger.Notification(
        Tag
          + " Startup migration sweep updated {0} block(s) across loaded chunks.",
        total
      );
  }

  private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
  {
    if (!EnsureInitialized())
    {
      // Nothing in this world matches any migration — stop listening entirely.
      _sapi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
      return;
    }

    int migrated = 0;
    for (int cy = 0; cy < chunks.Length; cy++)
      migrated += ScanChunk(chunkCoord.X, cy, chunkCoord.Y, chunks[cy]);

    if (migrated > 0)
      LogColumn(migrated, chunkCoord.X, chunkCoord.Y);
  }

  /// <summary>Scans one chunk section and rewrites every block matched by a migration.</summary>
  private int ScanChunk(int chunkX, int chunkY, int chunkZ, IWorldChunk? chunk)
  {
    if (chunk == null)
      return 0;
    chunk.Unpack();
    IChunkBlocks data = chunk.Data;
    int len = data.Length;

    const int cs = GlobalConstants.ChunkSize;
    IBlockAccessor ba = _sapi.World.BlockAccessor;
    int migrated = 0;

    for (int i = 0; i < len; i++)
    {
      int id = data[i];
      if (id == 0)
        continue;

      // Resolve the live block for this id and match on its code, so renumbered ids
      // (and the missing-block placeholders that keep their original code) are handled.
      Block block = _sapi.World.GetBlock(id);
      if (
        block?.Code == null
        || !_remap.TryGetValue(block.Code, out RemapEntry entry)
      )
        continue;

      // index3d layout: ((y * cs) + z) * cs + x
      int x = i % cs;
      int z = i / cs % cs;
      int y = i / (cs * cs);

      BlockPos pos = new(chunkX * cs + x, chunkY * cs + y, chunkZ * cs + z);

      ReplaceBlock(ba, pos, entry);
      migrated++;
    }

    return migrated;
  }

  private void LogColumn(int migrated, int chunkX, int chunkZ) =>
    _sapi.Logger.Notification(
      Tag + " Migrated {0} block(s) in chunk column {1},{2}.",
      migrated,
      chunkX,
      chunkZ
    );

  private void BuildRemapTable()
  {
    foreach (IBlockCodeMigration migration in DiscoverMigrations())
    {
      var beMigration = migration as IBlockEntityMigration;
      int count = 0;
      foreach (var (oldCode, newCode) in migration.GetRemaps(_sapi))
      {
        // GetBlock(code) resolves missing-block placeholders too (they are registered in
        // BlocksByCode), so a null here means this world has no such legacy block — skip
        // it, which keeps _remap empty when there is nothing to migrate.
        if (_sapi.World.GetBlock(oldCode) == null)
          continue;

        Block? newBlock = _sapi.World.GetBlock(newCode);
        if (newBlock == null || newBlock.BlockId == 0)
        {
          _sapi.Logger.Warning(
            Tag
              + " Migration '{0}': replacement block '{1}' is not registered; skipping.",
            migration.Name,
            newCode
          );
          continue;
        }

        if (
          _remap.TryGetValue(oldCode, out RemapEntry existing)
          && !existing.NewBlock.Code.Equals(newCode)
        )
        {
          _sapi.Logger.Warning(
            Tag
              + " Migration '{0}' remaps {1} but it is already remapped elsewhere; keeping the first mapping.",
            migration.Name,
            oldCode
          );
          continue;
        }

        _remap[oldCode] = new RemapEntry(
          newBlock,
          oldCode,
          newCode,
          beMigration
        );
        count++;
      }

      if (count > 0)
        _sapi.Logger.Notification(
          Tag + " Migration '{0}': {1} legacy block code(s) found to update.",
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
      ba.SetBlock(entry.NewBlock.BlockId, pos);
      return;
    }

    ITreeAttribute? oldState = null;
    if (ba.GetBlockEntity(pos) is BlockEntity oldBe)
    {
      oldState = new TreeAttribute();
      oldBe.ToTreeAttributes(oldState);
    }

    ba.SetBlock(entry.NewBlock.BlockId, pos);

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

  // Scan every loaded assembly, not just this lib's: this system lives in the shared
  // exlib mod, but the mods that depend on it (ppex, smex) declare their own migrations.
  private static IEnumerable<IBlockCodeMigration> DiscoverMigrations()
  {
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
    {
      Type[] types;
      try
      {
        types = asm.GetTypes();
      }
      catch (ReflectionTypeLoadException ex)
      {
        types = ex.Types.Where(t => t != null).ToArray()!;
      }

      foreach (var t in types)
      {
        if (
          !typeof(IBlockCodeMigration).IsAssignableFrom(t)
          || t is not { IsAbstract: false, IsInterface: false }
          || t.GetConstructor(Type.EmptyTypes) == null
        )
          continue;
        yield return (IBlockCodeMigration)Activator.CreateInstance(t)!;
      }
    }
  }
}
