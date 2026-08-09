# Migrations & Healing

Two server-side `ModSystem`s in `exlib` keep old saves working when your blocks change: a
**migrator** that rewrites renamed/removed block codes, and a **healer** that recreates block
entities that were lost while their block survived. Both are auto-discovered - you implement an
interface (or just let the healer run) and exlib does the chunk sweeping.

## Block migrations

`BlockMigrationModSystem` collects every `IBlockCodeMigration`, `IItemCodeMigration` and `IBlockRemoval` implementation
(public parameterless constructor, auto-discovered) into one table and applies it as chunk columns
load - and rewrites matching stacks in containers and player inventories too. Because it matches on
`Block.Code`, both renumbered ids and missing-block placeholders are handled.

### Rename / re-variant a block

```csharp
public interface IBlockCodeMigration
{
    string Name { get; }   // short label for log output
    IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(ICoreServerAPI api);
}
```

```csharp
public class PipeRenameMigration : IBlockCodeMigration
{
    public string Name => "pipe rename 0.6";

    public IEnumerable<(AssetLocation, AssetLocation)> GetRemaps(ICoreServerAPI api)
    {
        yield return (new("ppex:pipe-iron-ns"), new("ppex:pipe-straight-iron-ns"));
        // Return the full set unconditionally - pairs whose old or new code is absent in this
        // world are skipped, so a superset is safe.
    }
}
```

### Migrate block-entity state too

If the renamed block carried BE state worth preserving (inventory, progress), implement
`IBlockEntityMigration` on the **same class**. It runs after the new block is placed, with the old
BE's serialized tree:

```csharp
public interface IBlockEntityMigration
{
    void MigrateBlockEntity(AssetLocation oldCode, AssetLocation newCode,
        ITreeAttribute? oldState, BlockEntity newBlockEntity, IWorldAccessor world);
}
```

Mutate `newBlockEntity` directly - it is marked dirty for you. `oldState` is `null` if the old
block had no BE.

### Remove a block entirely

```csharp
public interface IBlockRemoval
{
    string Name { get; }
    IEnumerable<AssetLocation> GetRemovals(ICoreServerAPI api);   // full domain-qualified codes to delete
}
```

Removals delete the block in place **and** strip matching items from containers and player
inventories. You can read config to decide what to purge - the table is built once at startup.

## Block-entity healing

`BlockEntityHealModSystem` repairs **orphaned block entities**: a block that is still placed but
lost its BE after a deserialization failure or desync. Symptom: a door (or machine) that is
un-interactable and unbreakable because the controlling BE is gone. The healer recreates a fresh
BE as chunk columns load, making the block functional again.

```csharp
public class BlockEntityHealModSystem : ModSystem
{
    public int HealLoadedChunks();                                  // sweep all loaded chunks; returns count healed
    public bool HealOrphanAt(IBlockAccessor ba, BlockPos pos);      // heal a single position; returns true if healed
}
```

It runs automatically (spawn-chunk sweep at `RunGame`, then on every `ChunkColumnLoaded`) and is
also exposed through the **`/exmod heal`** command for a manual pass. Its scope is restricted to
types carrying `[BlockEntityRegister]`, so it never touches vanilla or foreign BEs. Recreated BEs
start from default state; multiblock anchors re-detect their structure on the next monitor tick.

> You get healing for free for any `[BlockEntityRegister]` block - no interface to implement. The
> only thing to be aware of is that a healed BE starts empty, so anything you can't reconstruct
> from the block alone (an inventory) is lost; healing restores *function*, not prior contents.

## Related pages

- [Registries](Registries) - `[BlockEntityRegister]` is what scopes the healer.
- [Commands](Commands) - `/exmod heal`.
