# Changelog - Expanded Library (`exlib`)

All notable changes to this mod are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[Semantic Versioning](https://semver.org/). For changes before this file existed,
see the git history.

## [0.7.1] - 2026-08-09

### Added

- **Footprint cells can host block-entity behaviours.** A mega-block's `fillerOffsets`
  entry may now declare `behaviors: [{ code, face, properties }]`; the invisible filler
  at that cell instantiates each behaviour on the principal's behalf and hands it the
  connector face already rotated into the placed orientation. Mechanical-power discovery
  is position-local, so a machine can only accept an axle at the cell the axle touches -
  this is how a machine several cells wide couples to a shaft.
- **`BEBehaviorMPFillerPort`** - a minimal mechanical-power node for exactly that: it
  renders nothing, loads the network with a configurable resistance, and exposes the
  network's speed and angle for the principal to drive its parts by. Couples on both ends
  of its axis, so an axle can run straight through.
- **`MPAnim`** - phase-lock helpers for a part that must turn in step with an axle rather
  than merely at the same rate.
- **Item code migrations.** `IItemCodeMigration` declares item-side renames alongside the
  existing block-side `IBlockCodeMigration`.

### Fixed

- **Hiding content no longer crashes the client.** Disabling registered content cleared
  its creative tabs to a null. The game walks every collectible and reads that array's
  length without a null guard while building the interaction help for an attachable
  entity, so the client crashed the moment a player looked at a boat, a raft or a mount.
  The array is now emptied rather than nulled, which hides the content identically.
- **Migrations no longer confuse an item with a block of the same code.** Every stack was
  matched against one table keyed by code, so an item sharing a block's code could be
  rewritten into that block, or deleted outright as an unresolvable purge. Stacks now
  route by class through separate tables.
- **A tick that ran long no longer detonates machinery.** Production and network ticks
  forwarded the engine's delta verbatim, and the grace timers downstream accumulate
  against a threshold with no per-step bound - so one overlong server tick could cross a
  boiler's 30 s over-pressure grace in a single call. Both sites now cap the step.
- **A structure's rotation is established when it loads,** not three ticks later. The
  angle started unset and was only assigned by a slow monitor tick, and the unset value
  read as "unrotated" - so a rotated machine briefly resolved every structure-local
  offset to the wrong block.
- **Orientation relaxation actually relaxes.** Single-letter orientation codes were passed
  to a lookup that wants the full word and returns null for every letter, so the fallback
  removed nothing; a network node with a connector against a wall then read as unsupported
  and broke itself out of the world.
- **An unreadable config file is backed up, not overwritten.** Loading ended in an
  unconditional write, so any path that failed to reproduce a player's values replaced
  their file with defaults - and a blank file deserialises to null rather than throwing,
  so the most destructive case logged nothing. Unreadable files are copied to
  `<name>.corrupt` first, and only the server writes, so a singleplayer session's client
  and server no longer race over one file.
- **Machine sound loops are typed as ambience** rather than as effects, so accessibility
  mods can list and mute them.

## [0.7.0] - 2026-06-21

### Added

- **Orphaned block-entity healer.** A server-side system that recreates a block
  entity when a block is left in the world without one - e.g. a block entity
  discarded on chunk load (a load exception) or lost to a server desync, which
  otherwise leaves an inert, often unbreakable block. It runs automatically as
  chunks load (and once over already-loaded chunks at startup), scoped to block
  entities registered through the mod's attribute system so vanilla/other-mod
  entities are never touched.
- **`/exmod heal` command.** Sweeps the loaded chunks and recreates orphaned block
  entities on demand, for an operator who does not want to wait for the automatic
  on-load pass. Server-side, gated behind the `/exmod` root's `controlserver`.
- **Config framework.** A generic, versioned per-mod config store with
  source-generated value accessors, version-reset migrations, and legacy file-name
  renaming. Values can be marked manageable and edited live via
  `/exmod config <mod> [value] [new]` - applied immediately, no world reload.
- **Min/max range gates** on config values: out-of-range edits are rejected with a
  clear message.
- **Recipe-cost profiles.** A per-mod catalogue framework that rebalances grid and
  right-click-construction ingredient quantities, switchable with
  `/exmod recipes <mod> <level>`.
- **Content-gating helper** (`ExContentGate`) for hiding a block/item from creative
  and the handbook and removing its recipes - the framework behind smex's mold
  toggle.
- **Command framework.** Attribute-driven `[CommandRegister]` / `[SubCommandRegister]`
  registration under a shared `/exmod` (server) and `.exmod` (client) root, so
  dependent mods hang their own sub-commands off one root.
- **Production-machine base** (`BlockEntityProductionMachine`) and machine-port
  helpers, shared by engines, furnaces, converters and sub-machines.
- **Legacy support framework.** Shims and polyfills that let the family build and run
  against Vintage Story 1.21 and 1.20 alongside 1.22.
- **Russian and Ukrainian** translations.

### Changed

- **Internal reorganization** into `Blocks/{Networks,Structures,Machines,Migrations,Construction,Healing}`,
  `Registries/{Entities,Commands,Config,Preferences,Recipes}`, `Helpers`,
  `Renderers` and `Legacy`.
- **Registration attributes split.** The single `[EntityRegister]` became
  kind-specific `[BlockRegister]`, `[ItemRegister]`, `[BlockEntityRegister]`,
  `[BlockBehaviorRegister]`, `[BlockEntityBehaviorRegister]` and
  `[CollectibleBehaviorRegister]`, each validating that the class derives from the
  expected base type.
- **Right-click-construction salvage:** the ratio of materials dropped when a
  partially-built or finished structure is broken is now configurable.
- **Multiblock structures read live config changes** without a world reload.

### Fixed

- Right-click-constructable blocks ignored their last construction stage when
  computing dropped materials.
- Non-pipe network blocks could incorrectly burst.
- Block display-name ordering and assorted localization issues.
- `/exmod config` value display formatting.

## [0.6.0] - 2026-06-16

### Added

- **Command framework.** A shared `/exmod` (server) and `.exmod` (client) command
  root, with a server-side version and privilege handling, so dependent mods hang
  their sub-commands off one root.
- **Measurement helpers** (metric/imperial) and a **per-player preference registry**,
  with a handbook patch that converts displayed measurements to the player's units.
- **Network-highlight** subcommand and a **base surface renderer**.
- **Source generators** that bake block/item JSON attributes into generated class
  members.
- **Config migrations** from older versions.

### Changed

- The **structure filler** mirrors the principal block's block-info.

## [0.5.1] - 2026-06-14

### Changed

- The **migration system** now also covers items held in inventories, not just placed
  blocks.

### Fixed

- **Right-click-constructable** wildcard handling and the names shown for missing
  materials.

## [0.5.0] - 2026-06-13

The first standalone release of the shared library, extracted from Steelmaking
Expanded (internal `0.1.0` groundwork promoted to `0.5.0`).

### Added

- **Block-network framework** (nodes, connectors, graph) backing gas pipes and molten
  canals.
- **Multiblock structure framework** with right-click construction.
- **Attribute-driven registration** for blocks, items and behaviors.
- **World-migration system** for updating old blocks.
- Shared **particle, sound and orientation** catalogues and helpers.
