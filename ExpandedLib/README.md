# Expanded Library (`exlib`)

Shared framework mod for the *Expanded* family
([Pipes and Power Expanded](../PipesAndPowerExpanded/README.md),
[Steelmaking Expanded](../SteelmakingExpanded/README.md)). It ships no gameplay content
of its own — install it because another mod depends on it.

## What it provides

- **Block networks** (`BlockNetworks/`) — a generic connected-graph framework:
  self-orienting node blocks (`BlockNetworkNode`), node block entities, live network
  instances with merge/fracture handling, and a single `BlockNetworkModSystem`
  manager. `ppex` registers the "pipe" network on it, `smex` the "molten" network.
- **Multiblock structures** (`BlockStructures/`) — completion monitoring, build-outline
  projection (ctrl+shift+rmb), crash-safe incomplete-part highlighting, and the shared
  invisible `structurefiller` block that gives mega-block machines per-cell collision.
- **Entity registry** (`EntityRegistry/`) — attribute-driven registration
  (`[EntityRegister]`) of blocks, block entities, items and behaviors.
- **Block migrations** (`BlockMigrations/`) — rewrites renamed/re-variantted block
  codes in old saves as chunks load.
- **Shared helpers** — `ExOrientation` (rotation math), `ExParticles` / `ExSounds`
  (effect catalogues), `ExCreativeTabs`, `ExInventory` / `ExItems`, and `ExBlockNames`
  (material/rock/brick variant display names).

## Building

```sh
dotnet build ExpandedLib/ExpandedLib.csproj
```
