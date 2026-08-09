using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Blocks.Migrations;

/// <summary>
/// Generic, server-side world migrator for renamed/re-variantted blocks - and a purger for ones a
/// mod wants gone. Collects every <see cref="IBlockCodeMigration"/> and <see cref="IBlockRemoval"/>
/// in all loaded assemblies into one legacy-code → action table and applies it to matching blocks as
/// chunk columns load. It matches on <see cref="Block.Code"/> (not a precomputed id, since the engine
/// renumbers ids on load) so it also catches the missing-block placeholders the engine keeps for
/// removed codes.
/// <para>
/// A plain migration is a bare block-id swap (state reconstructed from the new variant code); one
/// that also implements <see cref="IBlockEntityMigration"/> gets the old BE's tree handed to it. A
/// removal deletes the block in place. Either way the same matching content held as item stacks
/// (container BEs, player inventories) is rewritten or stripped too, migrations preserving stack size
/// and attributes.
/// </para>
/// </summary>
public class BlockMigrationModSystem : ModSystem {
  /// <summary>One resolved action for a given legacy block code. A null
  /// <see cref="NewBlock"/> means "remove" (delete the block / drop the item stack); otherwise it is
  /// the replacement to swap in.</summary>
  private readonly record struct RemapEntry(
    Block? NewBlock,
    AssetLocation OldCode,
    AssetLocation? NewCode,
    IBlockEntityMigration? BlockEntityMigration
  );

  private ICoreServerAPI _sapi = null!;

  /// <summary>Log prefix, e.g. "[smex]" / "[ppex]" - the owning mod's id.</summary>
  private string Tag => "[" + Mod.Info.ModID + "]";

  // Legacy block code -> replacement, merged across all discovered migrations. Keyed by code
  // (not id) because the engine can renumber block ids on load.
  private readonly Dictionary<AssetLocation, RemapEntry> _remap = [];

  // Legacy ITEM code -> replacement item. Kept separate from _remap: an item and a block may share
  // one code, and the block table can never resolve an item code, so a shared table would delete a
  // matching item stack as though it were a purge.
  private readonly Dictionary<AssetLocation, Item> _itemRemap = [];

  private bool _initialized;

  // Only the server owns world block data; the client has nothing to migrate.
  public override bool ShouldLoad(EnumAppSide side) =>
    side == EnumAppSide.Server;

  public override void StartServerSide(ICoreServerAPI api) {
    _sapi = api;
    // Spawn-area chunks are already loaded before this event is wired up, so sweep them once at
    // RunGame and handle every column that loads afterwards via the event.
    api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, SweepLoadedChunks);
    api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
    // Migrated blocks can also sit as item stacks in a player's inventory (the chunk scan never
    // sees those), so remap them on join.
    api.Event.PlayerJoin += OnPlayerJoin;
  }

  /// <summary>Builds the remap table on first use; returns false if nothing to migrate.</summary>
  private bool EnsureInitialized() {
    if (!_initialized) {
      BuildRemapTable();
      _initialized = true;
    }
    // An item-only migration has nothing in the block table but still has inventories to sweep.
    return _remap.Count > 0 || _itemRemap.Count > 0;
  }

  private void SweepLoadedChunks() {
    if (!EnsureInitialized())
      return;

    int chunksTall = _sapi.WorldManager.MapSizeY / GlobalConstants.ChunkSize;
    int total = 0;

    // Copy the keys: ReplaceBlock mutates chunks, so don't enumerate the live dictionary.
    foreach (
      long index2d in _sapi.WorldManager.AllLoadedMapchunks.Keys.ToArray()
    ) {
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

  private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks) {
    if (!EnsureInitialized()) {
      // Nothing in this world matches any migration - stop listening entirely.
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
  private int ScanChunk(int chunkX, int chunkY, int chunkZ, IWorldChunk? chunk) {
    if (chunk == null)
      return 0;
    chunk.Unpack();
    IChunkBlocks data = chunk.Data;
    int len = data.Length;

    const int cs = GlobalConstants.ChunkSize;
    IBlockAccessor ba = _sapi.World.BlockAccessor;
    int migrated = 0;

    for (int i = 0; i < len; i++) {
      int id = data[i];
      if (id == 0)
        continue;

      // Resolve the live block and match on its code, so renumbered ids and missing-block
      // placeholders are both handled.
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

    // Container BEs (chests, ground storage, mold racks) can store migrated blocks as item stacks
    // the voxel loop didn't see, so scan their slots too. Snapshot the values first - ReplaceBlock
    // above may have mutated this dictionary.
    if (chunk.BlockEntities != null)
      foreach (BlockEntity be in chunk.BlockEntities.Values.ToArray())
        if (be is IBlockEntityContainer { Inventory: { } inv }) {
          int n = RemapInventory(inv);
          if (n > 0) {
            be.MarkDirty(true);
            migrated += n;
          }
        }

    return migrated;
  }

  /// <summary>
  /// Rewrites every item stack in <paramref name="inv"/> whose block is a migration source
  /// to the replacement block, preserving stack size and attributes (e.g. a filled mold's
  /// stored contents). Returns how many slots changed.
  /// </summary>
  private int RemapInventory(IInventory inv) {
    int changed = 0;
    foreach (ItemSlot slot in inv) {
      ItemStack? stack = slot.Itemstack;
      if (stack?.Collectible?.Code == null)
        continue;

      // Item stacks go through the item table only. Matching them against the block table would
      // rewrite an item into a same-named block, or delete it outright when no block resolves.
      if (stack.Class == EnumItemClass.Item) {
        if (!_itemRemap.TryGetValue(stack.Collectible.Code, out Item? newItem))
          continue;

        ItemStack itemReplacement = new(newItem, stack.StackSize);
        if (stack.Attributes is { Count: > 0 })
          itemReplacement.Attributes = stack.Attributes.Clone();
        slot.Itemstack = itemReplacement;
        slot.MarkDirty();
        changed++;
        continue;
      }

      if (!_remap.TryGetValue(stack.Collectible.Code, out RemapEntry entry))
        continue;

      // A removal: drop the stack from the slot entirely.
      if (entry.NewBlock == null) {
        slot.Itemstack = null;
        slot.MarkDirty();
        changed++;
        continue;
      }

      ItemStack replacement = new(entry.NewBlock, stack.StackSize);
      if (stack.Attributes is { Count: > 0 })
        replacement.Attributes = stack.Attributes.Clone();
      slot.Itemstack = replacement;
      slot.MarkDirty();
      changed++;
    }
    return changed;
  }

  /// <summary>Remaps any migrated blocks a joining player is carrying as item stacks.</summary>
  private void OnPlayerJoin(IServerPlayer player) {
    if (!EnsureInitialized())
      return;

    int changed = 0;
    foreach (
      KeyValuePair<string, IInventory> kv in player.InventoryManager.Inventories
    ) {
      // The creative inventory is a virtual search list whose Count getter NREs on join - skip it.
      if (
        kv.Value is not { } inv
        || inv.ClassName == GlobalConstants.creativeInvClassName
      )
        continue;

      // A single misbehaving (e.g. modded) inventory must not abort the join.
      try {
        changed += RemapInventory(inv);
      } catch (Exception e) {
        _sapi.Logger.Warning(
          Tag + " Skipped inventory '{0}' for {1} during migration: {2}",
          kv.Key,
          player.PlayerName,
          e.Message
        );
      }
    }

    if (changed > 0)
      _sapi.Logger.Notification(
        Tag + " Migrated {0} carried item stack(s) for {1}.",
        changed,
        player.PlayerName
      );
  }

  private void LogColumn(int migrated, int chunkX, int chunkZ) =>
    _sapi.Logger.Notification(
      Tag + " Migrated {0} block(s)/stack(s) in chunk column {1},{2}.",
      migrated,
      chunkX,
      chunkZ
    );

  private void BuildRemapTable() {
    foreach (IBlockCodeMigration migration in Discover<IBlockCodeMigration>()) {
      var beMigration = migration as IBlockEntityMigration;
      int count = 0;
      foreach (var (oldCode, newCode) in migration.GetRemaps(_sapi)) {
        // GetBlock resolves missing-block placeholders too, so a null means this world has no
        // such legacy block - skip it.
        if (_sapi.World.GetBlock(oldCode) == null)
          continue;

        Block? newBlock = _sapi.World.GetBlock(newCode);
        if (newBlock == null || newBlock.BlockId == 0) {
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
          && existing.NewBlock?.Code.Equals(newCode) != true
        ) {
          _sapi.Logger.Warning(
            Tag
              + " Migration '{0}' remaps {1} but it is already mapped elsewhere; keeping the first mapping.",
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

    // Item renames (IItemCodeMigration): a separate table, resolved against the item registry.
    foreach (IItemCodeMigration migration in Discover<IItemCodeMigration>()) {
      int count = 0;
      foreach (var (oldCode, newCode) in migration.GetRemaps(_sapi)) {
        if (oldCode == null || newCode == null)
          continue;
        // A world without the legacy item has nothing to migrate.
        if (_sapi.World.GetItem(oldCode) == null)
          continue;

        Item? newItem = _sapi.World.GetItem(newCode);
        if (newItem == null || newItem.ItemId == 0) {
          _sapi.Logger.Warning(
            Tag
              + " Item migration '{0}': replacement item '{1}' is not registered; skipping.",
            migration.Name,
            newCode
          );
          continue;
        }

        if (
          _itemRemap.TryGetValue(oldCode, out Item? existingItem)
          && existingItem?.Code.Equals(newCode) != true
        ) {
          _sapi.Logger.Warning(
            Tag
              + " Item migration '{0}' remaps {1} but it is already mapped elsewhere; keeping the first mapping.",
            migration.Name,
            oldCode
          );
          continue;
        }

        _itemRemap[oldCode] = newItem;
        count++;
      }

      if (count > 0)
        _sapi.Logger.Notification(
          Tag
            + " Item migration '{0}': {1} legacy item code(s) found to update.",
          migration.Name,
          count
        );
    }

    // Purges (IBlockRemoval): same matching, but the action is "delete" (null replacement).
    foreach (IBlockRemoval removal in Discover<IBlockRemoval>()) {
      int count = 0;
      foreach (AssetLocation code in removal.GetRemovals(_sapi)) {
        if (code == null || _sapi.World.GetBlock(code) == null)
          continue;

        if (_remap.ContainsKey(code)) {
          _sapi.Logger.Warning(
            Tag
              + " Removal '{0}' targets {1} but it is already mapped elsewhere; keeping the existing mapping.",
            removal.Name,
            code
          );
          continue;
        }

        _remap[code] = new RemapEntry(null, code, null, null);
        count++;
      }

      if (count > 0)
        _sapi.Logger.Notification(
          Tag + " Removal '{0}': {1} block code(s) marked for purge.",
          removal.Name,
          count
        );
    }
  }

  /// <summary>
  /// Swaps the block at <paramref name="pos"/> for its replacement. A plain migration is a bare
  /// <c>SetBlock</c>; one that handles BE state captures the old entity's tree first and applies it
  /// to the new entity afterwards.
  /// </summary>
  private void ReplaceBlock(IBlockAccessor ba, BlockPos pos, RemapEntry entry) {
    // A removal: delete the block (and its entity) outright.
    if (entry.NewBlock == null) {
      ba.SetBlock(0, pos);
      return;
    }

    if (entry.BlockEntityMigration == null) {
      ba.SetBlock(entry.NewBlock.BlockId, pos);
      return;
    }

    ITreeAttribute? oldState = null;
    if (ba.GetBlockEntity(pos) is BlockEntity oldBe) {
      oldState = new TreeAttribute();
      oldBe.ToTreeAttributes(oldState);
    }

    ba.SetBlock(entry.NewBlock.BlockId, pos);

    if (ba.GetBlockEntity(pos) is BlockEntity newBe) {
      entry.BlockEntityMigration.MigrateBlockEntity(
        entry.OldCode,
        entry.NewCode!, // non-null for a migration entry (removals return above)
        oldState,
        newBe,
        _sapi.World
      );
      newBe.MarkDirty(true);
    }
  }

  // Scan every loaded assembly for parameterless implementations of T: this system lives in exlib,
  // but ppex/smex declare their own migrations and removals.
  private static IEnumerable<T> Discover<T>()
    where T : class {
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
      Type[] types;
      try {
        types = asm.GetTypes();
      } catch (ReflectionTypeLoadException ex) {
        types = ex.Types.Where(t => t != null).ToArray()!;
      }

      foreach (var t in types) {
        if (
          !typeof(T).IsAssignableFrom(t)
          || t is not { IsAbstract: false, IsInterface: false }
          || t.GetConstructor(Type.EmptyTypes) == null
        )
          continue;
        yield return (T)Activator.CreateInstance(t)!;
      }
    }
  }
}
