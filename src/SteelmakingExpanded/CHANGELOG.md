# Changelog - Steelmaking Expanded (`smex`)

All notable changes to this mod are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[Semantic Versioning](https://semver.org/). For changes before this file existed,
see the git history.

## [0.9.6] - 2026-08-09

Covers the 0.9.3 through 0.9.5 development bumps, which were never published separately.

### Added

- **Twin-Tub Blower** - axle-driven bellows, and the only air source that needs no steam.
  A waterwheel or windmill will now run a blast furnace, so the iron chain no longer waits
  on a boiler. It delivers 60 L/s at full axle speed and raises its run to 2.0 atm, which
  clears the furnace's blast gate and never reaches the converter's - mechanical power
  makes iron, steam is still required for steel. Right-click constructed: the grid recipe
  gives a wooden frame, and the beam, axle, tubs and pipe connection follow.
- **The blast furnace burns charcoal as well as coke.** Charcoal carries about half the
  carbon, so a batch takes four charcoal where two coke would do, and the two mix freely
  in one batch. The coke oven is still the efficient route, not the only one.
- **Every refractory structure accepts any brick tier.** Only the Bessemer converter still
  asks for a specific tier.

### Changed

- **The blast furnace's blast gate is separate from the converter's,** and lower: the
  furnace fires at 1.5 atm, the converter still needs 2.5. Iron is reachable with
  mechanical power; steel is not.
- **The heat sink is built from refractory brick** rather than iron sheet, which is what a
  regenerator is actually made of. It comes in three tiers, its recipe takes any of them,
  and existing heat sinks are converted on load.
- **The blast furnace takes coke whole; crushed coke is retired.** The intermediate existed
  only to be fed to the furnace, and producing it meant bolting a crushing recipe onto
  vanilla coke - which put this mod in the middle of every other mod's crushing economy.
  Leftover crushed coke converts to coke one-for-one as chunks load. The per-batch cost is
  restated in whole coke.
- **The blast furnace pushes its exhaust at 2 atm.** At the old implicit 1 atm the
  pressure-relief valve in the setup diagrams could never open.

### Fixed

- **A blocked flue stalls the blast furnace instead of extinguishing it.** Shutting the hot
  exhaust off to a cowper stove is exactly what the handbook's regenerator swap requires,
  but it counted as a disruption, so thirty seconds later the furnace died - dumping the
  molten iron, slagging every hearth pile and ending the campaign. A choked furnace now
  falls back to its natural draught ceiling and resumes when the exhaust reopens.
  The choke test itself was also wrong: it read "any outlet refused" where it meant "no
  outlet accepted", so piping one gas outlet and leaving the other bare choked the furnace
  permanently, and the message naming the reason never appeared.
- **The reinforced hopper's contents no longer go stale on the client.** The bell hopper
  marked itself after draining the hopper above, but the client reads those slots from the
  hopper's own block entity - so the contents looked right until the dialog was reopened,
  and relogging cleared it. It now syncs on every inventory change, including a chute
  feeding it.
- **Molten canals settle instead of sloshing.** Levelling moved the whole difference
  between two cells, so an adjacent pair inverted every tick. Taps and mold pedestals still
  take everything offered, so a run can still empty.
- **The smoke stack accepts clinker brick and every brick course colour.** The masonry
  legend enumerated seven of the eight colours and omitted clinker, so a chimney built from
  them never completed and the only feedback was the outline.
- **Slag paths and the heat sink no longer render near-black** under lighting mods; they
  declared the light absorption vanilla reserves for solid opaque cubes.
- **A rotated blast furnace no longer scans the wrong cells** for the first few ticks after
  loading.
- **Nothing is patched into another mod's crushing recipes.** The compatibility patch that
  disabled a crushing recipe by index is gone along with crushed coke itself.

## [0.9.2] - 2026-06-21

### Added

- **Bell hopper drops by default.** A freshly built blast furnace now feeds itself
  without the player first discovering the Ctrl + right-click toggle.
- **Chisel residue out of the Bessemer converter** when only a little hardened metal
  remains, instead of having to break the whole vessel; the cooldown coefficient is
  configurable.
- **Chisel molten canal taps and pedestals**, matching the canals and barrels.
- **Cooldown coefficients** for taps, barrels and pedestals (how fast their metal
  cools), and the cooldown speed now **syncs to the client** so the glow matches the
  server.
- **`/exmod molds <plate|ingot|rod|all> <on|off>`** to enable/disable the mod's tool
  molds (hidden from creative + handbook and their recipes removed when off).
- **Recipe-cost switching** for the steam/steel chains via the shared `/exmod recipes`
  command.
- **Orphaned machine blocks now self-heal** - a furnace door/tap/tuyere left without
  its block entity (a load failure or desync) gets a fresh one recreated on load
  (via the exlib healer), so it is interactable and breakable again rather than an
  inert ghost.
- **Russian and Ukrainian** translations.

### Changed

- **Molten chiselling generalized** into one shared behaviour across canals, barrels
  and the converter (consistent tool, sound, wear and recovery).
- **Converter (and boilers) no longer drop their base block when broken** - they
  scatter their build materials instead of dropping the whole mega-block.
- **Raised mining-tier / break-tool requirements** for mega-blocks.
- **Converter status strings** are more descriptive.
- Structures **read live config changes** without a world reload.

### Fixed

- The **Bessemer converter dropped itself** when broken.
- Corrected the **converter chisel cell position**.
- Fixed **engine animation direction** when powering mechanical power.
- The **molten canal tap** allowed placing barrels on structure filler blocks.
- Fixed **broken cowper-stove behaviour**.
- Fixed the **bell-hopper client/server inventory handshake** desync (grid clicks
  silently desyncing).
- Fixed **recipe item variants** and the **vanilla crushed-ore ratio** (EM compat).
- Fixed the **converter transmission recipe** rod requirements.
- Assorted localization issues.

## [0.9.1] - 2026-06-14

### Changed

- **Source-generator refactor:** block and item JSON attributes are now baked into
  generated class members instead of hand-written accessors.
- Block-info lines display measurements in the player's chosen unit system.
- The **structure filler** mirrors the block-info of the principal block it belongs to.
- Updated several block descriptions.

### Fixed

- Corrected the **Bessemer converter's mechanical resistance**.

## [0.9.0] - 2026-06-13

The **Steam Mechanics** release - steelmaking integrated with the new steam-power
system (split out into Pipes and Power Expanded; see its 0.5.0). Steel can now be
produced via the steam chain.

### Added

- **Slag and slag-path** blocks - waste output from the new processes - with several
  variants.
- **Brick variants** for the molten canals.
- **Tool molds became proper `smex` items**, and the migration system now also
  migrates items held in inventories (not just placed blocks).

### Changed

- **Rebalanced recipes** and added world migrations for older saves.

### Fixed

- Right-click construction now handles **wildcard ingredients** correctly and shows
  the **correct names for missing materials** while building.
- Quad-rod tool-mold fixes.

## [0.8.7] - 2026-06-08

### Fixed

- Mold recipes were missing their **domain**.
- Several **uncraftable recipes** and **recipe conflicts**.
- Valves **constantly rotating** in some situations.
- Possible **client crash** from the unsafe vanilla incomplete-structure highlight,
  resolved by reimplementing it safely.
- The molten network **ignored input temperature** on repeated pouring.
- Canals containing metal or solidified metal can **no longer be wrench-rotated**.
- **Blast furnace:** retains its internal state when melting stops, and no longer
  pours when not melting.
- Could incorrectly **seal solidified canals**.
- Assorted renderer issues.

## [0.8.6] - 2026-06-04

### Added

- **World-migration system** for updating old blocks to new variants (moved into the
  compiled library).
- **Brick variants** for the full-pipe blocks, with migration of existing blocks.
- More **cowper-stove and smoke-stack** variants; the bottom of the smoke stack now
  requires **refractory bricks**.

### Fixed

- Full-network blocks could not be rotated into all orientations.
- The valve animator ignored the **X component** of the rotation matrix.
- Added overlays to prevent **transparent-texture rendering** artifacts.

## [0.8.5] - 2026-06-04

### Added

- **Passthrough bend** variant.

### Fixed

- Crash from the **blast door** not dropping the correct block entity (duplicated
  vanilla `Block.GetDrops`).
- Some recipes did not return the correct **default block variants**.

## [0.8.4] - 2026-06-03

### Added

- **Lighting** for canals and barrels that hold molten contents.

### Changed

- Raised the default **Bessemer process temperature** so the melt does not solidify
  too fast.
- Removed the ability to **chisel out non-hardened metal**.

### Fixed

- The **Bessemer converter** now respects the global melting config.
- Rendered **molten surfaces** now re-render to reflect temperature changes.
- **Taps and pedestals** now actually sever the network connection when closed.
- The converter now respects the **input tap state**.
- Valve animation for **rotated valves**; valves used the wrong **interaction sound**.

## [0.8.3] - 2026-06-03

### Added

- **Rewrote the molten network** to simulate molten-iron flow between cells, with
  tuned default flow values.
- **Extensible iron-ore compat system**.

### Changed

- Updated **Bessemer converter** animation and sound.
- More forgiving **cowper-stove** defaults.

### Fixed

- Handbook formatting.

## [0.8.2] - 2026-06-02

### Changed

- **Centralized the mod config** into a single JSON file.

### Fixed

- Cowper-stove issues.

## [0.8.1] - 2026-06-02

### Added

- **Construction costs** shown in the handbook.
- **Specific interaction sounds** for all blocks.
- Multiblock structures now **log the missing block** in chat while building.

### Changed

- The **Bessemer converter** can be placed without cost in creative.
- Corrected the **iron hatch door's** name.

### Fixed

- Crash when **breaking a solidified Bessemer converter** (JSON wildcard issue).
- **Blast-furnace** solidified-drop amount.
- **Bessemer-converter and blast-furnace** global-position translation.

## [0.8.0] - 2026-06-01

### Added

- **First public release.** The Steelmaking Expanded mod: blast furnace, Bessemer
  converter, cowper stove, smoke stack, molten canals and tool molds, with their gas
  and molten block networks.
