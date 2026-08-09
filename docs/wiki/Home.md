# Expanded Library Wiki

**Expanded Library** (`exlib`) is the shared framework mod behind the
_Fallenstar Expanded_ family for [Vintage Story](https://www.vintagestory.at/) -
currently [Pipes and Power Expanded](https://mods.vintagestory.at/) (`ppex`) and
[Steelmaking Expanded](https://mods.vintagestory.at/) (`smex`). It ships no
gameplay content of its own; you can use it because another mod depends on it or
because it gives you batteries-included systems that are tedious to build from
scratch:

- a generic **block-network** graph (auto-orienting node blocks, merge/fracture handling, a
  single manager) - used for gas pipes, steam pipes and molten-metal canals;
- **multiblock structures** with completion monitoring, build-outline projection and an
  invisible filler block that gives mega-blocks real per-cell collision - and can carry a
  network port or a block entity behaviour on the controller's behalf;
- a **production-machine** tick lifecycle base;
- **attribute-driven registration** for blocks/items/entities/behaviours/commands, plus a
  source-generated, versioned, live-editable **config** system;
- a shared `/exmod` (server) and `.exmod` (client) command root, **recipe-cost** profiles,
  per-player **preferences**, **block migrations**, orphaned-BE **healing**, and a grab-bag
  of rotation / particle / sound / inventory **helpers**.

This wiki documents both libraries the family publishes for reuse:

- **`exlib`** - the runtime framework mod other mods depend on and call. It ships as the Vintage
  Story mod artifact (`exlib_<version>.zip`) on the [releases page](https://github.com/ringavirda/modding-vsexpanded/releases).
- **`exlib.testing`** (`ExpandedLib.Testing`) - a headless xUnit harness that loads the real game
  assemblies and exercises network/block-entity logic under `dotnet test`, no game launch
  required. It's a build-/test-time developer library, not something installed in the game: you
  consume it by referencing the project from source, or the `ExpandedLib.Testing.dll` published
  with each GitHub release. (It isn't on NuGet - the API still moves a lot release to release.)

## Where to start

- New here? Read **[Getting Started](Getting-Started)** - declare the dependency, set up a
  project reference, and register your first attribute-marked block.
- Building plumbing/wiring of any kind? **[Block Networks](Block-Networks)**.
- Building a furnace, boiler or other big machine? **[Multiblock Structures](Multiblock-Structures)**
  and **[Production Machines](Production-Machines)**.
- Want config, commands or recipe tuning? **[Registries](Registries)**,
  **[Config System](Config-System)**, **[Commands](Commands)**, **[Recipe Costs](Recipe-Costs)**.
- Writing tests? **[Testing Harness](Testing-Harness)** and **[Testing API Reference](Testing-API-Reference)**.

## A note on accuracy

These pages document the public surface a third-party mod consumes. Signatures are taken from
the source in this repository; when in doubt, the code in `src/ExpandedLib/` and
`test/ExpandedLib.Testing/` is the source of truth. Game-version differences (1.20 / 1.21 / 1.22)
are handled by the `Legacy/` shim and noted where they affect you.
