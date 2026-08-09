# Expanded Library (`exlib`)

Shared framework mod for the *Expanded* family
([Pipes and Power Expanded](../PipesAndPowerExpanded/README.md),
[Steelmaking Expanded](../SteelmakingExpanded/README.md)). It ships no gameplay content
of its own - install it because another mod depends on it.

## What it provides

- **Block networks** (`Blocks/Networks/`) - a generic connected-graph framework:
  self-orienting node blocks (`BlockNetworkNode`), node block entities, live network
  instances with merge/fracture handling, and a single `BlockNetworkModSystem`
  manager. `ppex` registers the "pipe" network on it, `smex` the "molten" network.
- **Multiblock structures** (`Blocks/Structures/`) - completion monitoring,
  build-outline projection (ctrl+shift+rmb), crash-safe incomplete-part highlighting,
  and the shared invisible `structurefiller` block that gives mega-block machines
  per-cell collision. A footprint cell can also expose a network port or host block
  entity behaviours on the controller's behalf - `BEBehaviorMPFillerPort` is the
  mechanical-power intake built on that.
- **Production machines** (`Blocks/Machines/`) - `BlockEntityProductionMachine` base
  (the tick lifecycle + operational gate) and `MachinePorts` helpers, shared by
  engines, furnaces, converters and sub-machines.
- **Block-entity healing** (`Blocks/Healing/`) - recreates a block entity that was
  lost while its block survived (a load failure or desync), automatically on chunk
  load and via `/exmod heal`.
- **Registries** (`Registries/`) - attribute-driven registration:
  - `Entities/` - `[BlockRegister]` / `[ItemRegister]` / `[BlockEntityRegister]` /
    `[BlockBehaviorRegister]` / `[BlockEntityBehaviorRegister]` /
    `[CollectibleBehaviorRegister]` for blocks, items, entities and behaviors.
  - `Commands/` - `[CommandRegister]` / `[SubCommandRegister]` building the shared
    `/exmod` (server) and `.exmod` (client) command root.
  - `Config/` - generic versioned config store (`ExConfigRegister`) with
    source-generated value accessors, range-gated values, migrations and live
    `/exmod config` editing.
  - `Recipes/` - per-mod recipe-cost profiles switchable with `/exmod recipes`.
  - `Preferences/` - per-player display-preference store.
- **Block migrations** (`Blocks/Migrations/`) - rewrites renamed/re-variantted block
  codes (and matching item stacks) in old saves as chunks load.
- **Shared helpers** (`Helpers/`, `Renderers/`) - `ExOrientation` (rotation math),
  `ExParticles` / `ExSounds` (effect catalogues), `ExCreativeTabs`,
  `ExInventory` / `ExItems`, `ExBlockNames` (variant display names), `ExContentGate`
  (hide-from-creative/handbook + recipe removal), and the shared `SurfaceRenderer`.
- **Legacy support** (`Legacy/`) - shims/polyfills that let the family build and run
  against Vintage Story 1.21 and 1.20 alongside 1.22.

## Building

```sh
dotnet build src/ExpandedLib/ExpandedLib.csproj
```
