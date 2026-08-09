using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Registries;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Blocks.Healing;

/// <summary>
/// Server-side self-healer for <em>orphaned block entities</em>: a block that is still placed in the
/// world but has lost its <see cref="BlockEntity"/>. The engine keeps the block but discards the BE
/// whenever its deserialization throws on chunk load (<c>ServerChunk.AfterDeserialization</c> logs
/// "Failed loading blockentity ... Will discard it."), or whenever a server desync drops the BE while
/// the robust block layer survives. The result is an inert block - no interaction, often unbreakable
/// (its <c>multiblock-monolithic</c> halves redirect to an empty controller), and impossible to build
/// over - which is exactly how a furnace door, tap or tuyere can "die" mid-base. See the save-report
/// analysis that motivated this.
/// <para>
/// As chunk columns load (plus a one-off sweep of the spawn chunks already loaded before the event is
/// wired) it walks each section's blocks; any block whose <see cref="Block.EntityClass"/> resolves to
/// one of <b>our</b> registered block entities but has no live BE gets a fresh one spawned via
/// <see cref="IBlockAccessor.SpawnBlockEntity(string, BlockPos, ItemStack)"/>. The recreated BE starts
/// from default state - the lost runtime data is unrecoverable - but the block becomes functional and
/// breakable again, and a multiblock anchor re-detects its structure on its next monitor tick.
/// </para>
/// <para>
/// Scope is restricted to types carrying <see cref="BlockEntityRegisterAttribute"/> (the same
/// attribute every BE in this mod family is registered through), so vanilla and third-party block
/// entities are never touched. This lives in exlib and so covers every dependent mod (smex, ppex)
/// automatically.
/// </para>
/// </summary>
public class BlockEntityHealModSystem : ModSystem {
  private ICoreServerAPI _sapi = null!;

  /// <summary>Log prefix, e.g. "[exlib]" - the owning mod's id.</summary>
  private string Tag => "[" + Mod.Info.ModID + "]";

  // Block ids whose block declares an entityClass that resolves to one of our
  // [BlockEntityRegister] types. Built lazily, once the world's block list exists.
  private readonly HashSet<int> _healableBlockIds = [];
  private bool _initialized;

  // Only the server owns world block/BE data; the client has nothing to heal.
  public override bool ShouldLoad(EnumAppSide side) =>
    side == EnumAppSide.Server;

  public override void StartServerSide(ICoreServerAPI api) {
    _sapi = api;
    // Spawn-area chunks are already loaded before this event is wired up, so sweep them once at
    // RunGame and handle every column that loads afterwards via the event.
    api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, SweepLoadedChunks);
    api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
  }

  /// <summary>Builds the healable block-id set on first use; returns false if this world has none.</summary>
  private bool EnsureInitialized() {
    if (!_initialized) {
      BuildHealableSet();
      _initialized = true;
    }
    return _healableBlockIds.Count > 0;
  }

  private void BuildHealableSet() {
    // Every block-entity type this mod family owns - i.e. registered through the attribute system.
    HashSet<Type> ourBeTypes = CollectRegisteredBlockEntityTypes();
    if (ourBeTypes.Count == 0)
      return;

    // entityClass code -> is it one of ours? Resolved once per distinct code (a block entity is
    // instantiated to read its concrete type; this is cheap and one-off).
    Dictionary<string, bool> resolvedByCode = [];

    foreach (Block block in _sapi.World.Blocks) {
      if (block?.EntityClass == null || block.BlockId == 0)
        continue;

      if (!resolvedByCode.TryGetValue(block.EntityClass, out bool isOurs)) {
        isOurs = IsOurBlockEntity(block.EntityClass, ourBeTypes);
        resolvedByCode[block.EntityClass] = isOurs;
      }

      if (isOurs)
        _healableBlockIds.Add(block.BlockId);
    }

    if (_healableBlockIds.Count > 0)
      _sapi.Logger.Notification(
        Tag
          + " BE healer watching {0} block type(s) for orphaned block entities.",
        _healableBlockIds.Count
      );
  }

  /// <summary>True if <paramref name="entityClass"/> resolves to one of our registered BE types.</summary>
  private bool IsOurBlockEntity(string entityClass, HashSet<Type> ourBeTypes) {
    try {
      BlockEntity? be = _sapi.ClassRegistry.CreateBlockEntity(entityClass);
      return be != null && ourBeTypes.Contains(be.GetType());
    } catch {
      // Unknown/foreign class code: not ours, leave it alone.
      return false;
    }
  }

  /// <summary>
  /// Scans every loaded assembly for concrete <see cref="BlockEntity"/> types carrying
  /// <see cref="BlockEntityRegisterAttribute"/>. This system lives in exlib, but smex/ppex declare
  /// their own block entities, so we look across all assemblies (not just our own).
  /// </summary>
  private static HashSet<Type> CollectRegisteredBlockEntityTypes() {
    HashSet<Type> types = [];
    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
      foreach (Type t in ReflectionScan.GetCandidateTypes(asm))
        if (
          typeof(BlockEntity).IsAssignableFrom(t)
          && t.GetCustomAttribute<BlockEntityRegisterAttribute>() != null
        )
          types.Add(t);
    return types;
  }

  private void SweepLoadedChunks() {
    int total = HealLoadedChunks();
    if (total > 0)
      _sapi.Logger.Notification(
        Tag
          + " Startup sweep recreated {0} orphaned block entit(ies) across loaded chunks.",
        total
      );
  }

  /// <summary>
  /// Sweeps every currently loaded chunk and recreates any orphaned block entities, returning how
  /// many were healed. Used by the startup sweep and the <c>/exmod heal</c> admin command (which lets
  /// an op fix orphans in already-loaded chunks without a world reload).
  /// </summary>
  public int HealLoadedChunks() {
    if (!EnsureInitialized())
      return 0;

    int chunksTall = _sapi.WorldManager.MapSizeY / GlobalConstants.ChunkSize;
    int total = 0;

    foreach (
      long index2d in _sapi.WorldManager.AllLoadedMapchunks.Keys.ToArray()
    ) {
      Vec2i coord = _sapi.WorldManager.MapChunkPosFromChunkIndex2D(index2d);
      for (int cy = 0; cy < chunksTall; cy++)
        total += ScanChunk(
          coord.X,
          cy,
          coord.Y,
          _sapi.WorldManager.GetChunk(coord.X, cy, coord.Y)
        );
    }

    return total;
  }

  private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks) {
    // No watched block types in this world: nothing can orphan, so stop listening entirely.
    if (!EnsureInitialized()) {
      _sapi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
      return;
    }

    int healed = 0;
    for (int cy = 0; cy < chunks.Length; cy++)
      healed += ScanChunk(chunkCoord.X, cy, chunkCoord.Y, chunks[cy]);

    if (healed > 0)
      _sapi.Logger.Notification(
        Tag
          + " Recreated {0} orphaned block entit(ies) in chunk column {1},{2}.",
        healed,
        chunkCoord.X,
        chunkCoord.Y
      );
  }

  /// <summary>Scans one chunk section and recreates any watched block's missing block entity.</summary>
  private int ScanChunk(int chunkX, int chunkY, int chunkZ, IWorldChunk? chunk) {
    if (chunk == null)
      return 0;
    chunk.Unpack();
    IChunkBlocks data = chunk.Data;
    int len = data.Length;

    const int cs = GlobalConstants.ChunkSize;
    IBlockAccessor ba = _sapi.World.BlockAccessor;
    int healed = 0;

    for (int i = 0; i < len; i++) {
      int id = data[i];
      if (id == 0 || !_healableBlockIds.Contains(id))
        continue;

      // index3d layout: ((y * cs) + z) * cs + x
      int x = i % cs;
      int z = i / cs % cs;
      int y = i / (cs * cs);
      BlockPos pos = new(chunkX * cs + x, chunkY * cs + y, chunkZ * cs + z);

      if (HealOrphanAt(ba, pos))
        healed++;
    }

    return healed;
  }

  /// <summary>
  /// If the block at <paramref name="pos"/> declares an <see cref="Block.EntityClass"/> but has no
  /// live block entity, spawns a fresh one and returns <c>true</c>. A healthy block (BE present), an
  /// empty cell, or a block without a block entity is left untouched. Callers in the chunk scan
  /// pre-filter to our watched block ids; this primitive re-checks the block's own state so it is
  /// also safe to invoke directly. The recreated BE starts from default state - the discarded runtime
  /// data is unrecoverable - which the BE reconstructs on its own ticks (a multiblock anchor, for
  /// example, re-detects its structure on its next monitor tick).
  /// </summary>
  public bool HealOrphanAt(IBlockAccessor ba, BlockPos pos) {
    string? entityClass = ba.GetBlock(pos)?.EntityClass;
    if (entityClass == null)
      return false;

    // A live BE here means the block is healthy - only the missing ones get recreated.
    if (ba.GetBlockEntity(pos) != null)
      return false;

    try {
      // Creates the BE and runs CreateBehaviors + Initialize for it.
      ba.SpawnBlockEntity(entityClass, pos);
      ba.GetBlockEntity(pos)?.MarkDirty(true);
      return true;
    } catch (Exception e) {
      _sapi.Logger.Warning(
        Tag + " Failed to recreate block entity '{0}' at {1}: {2}",
        entityClass,
        pos,
        e.Message
      );
      return false;
    }
  }
}
