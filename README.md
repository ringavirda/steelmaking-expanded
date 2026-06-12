# Fallenstar's Expanded mods

A monorepo of three [Vintage Story](https://www.vintagestory.at/) mods that together
add an industrial-era production chain — pipe networks, steam power, bulk iron and
steel making:

| Mod                                                       | modid   | What it is                                                                                                   |
| --------------------------------------------------------- | ------- | ------------------------------------------------------------------------------------------------------------ |
| [Expanded Library](ExpandedLib/README.md)                 | `exlib` | Shared framework: block networks, multiblock structures, entity registry, save migrations, common helpers.    |
| [Pipes and Power Expanded](PipesAndPowerExpanded/README.md) | `ppex`  | Pipe networks (gas + water), boilers, steam engines and their sub-machines (MP generator, fluid pump).        |
| [Steelmaking Expanded](SteelmakingExpanded/README.md)     | `smex`  | Blast furnace, cowper stoves, molten-metal canals and casting, Bessemer converter. Depends on both above.     |

![Diagram for the iron/steel production process](docs/smex/process.png)

For the full survival production flow — what feeds what, gating, and tunables — see
[`docs/smex/graph.txt`](docs/smex/graph.txt).

## Repository layout

| Path                      | Purpose                                                                |
| ------------------------- | ---------------------------------------------------------------------- |
| `ExpandedLib/`            | The `exlib` framework mod (C# + minimal assets).                       |
| `PipesAndPowerExpanded/`  | The `ppex` mod: pipe network + steam machinery.                        |
| `SteelmakingExpanded/`    | The `smex` mod: the iron/steel chain.                                  |
| `docs/`                   | Diagrams, screenshots, moddb listing sources.                          |
| `CakeBuild/`              | Cake build script project that packages release zips.                  |
| `VintageStory.sln`        | Solution tying the projects together.                                  |

`smex` project-references `exlib` and `ppex` (with `Private=false`), so players install
all three mods separately; the network manager identity lives in `exlib` only.

## Code conventions

Code is organized by **feature**, and within each feature by Vintage Story's
`Block` / `BlockEntity` split:

- **`Block*`** classes = the block definition (placement, orientation, interaction
  routing, drops).
- **`BlockEntity*`** classes = the per-tile state and logic (ticking, inventory,
  networks, rendering).
- **`Patches/`** in each mod = Harmony patches into vanilla classes. Vanilla behavior
  is extended via prefix/postfix patches, never by re-registering vanilla class names,
  so other mods touching the same blocks can coexist.
- **`BlockMigrations/`** in each mod = `IBlockCodeMigration` implementations that
  rewrite old block codes when variants change between versions (the framework in
  `exlib` discovers them by reflection and applies them as chunks load).
- Registration is attribute-driven: decorate a class with `[EntityRegister]` and
  `EntityRegistry.RegisterAll` picks it up.
- Gameplay tunables live in `PpexValues.cs` / `SmexValues.cs`, loaded from
  `ModConfig/ppex.json` / `ModConfig/smex.json`.

## Network system (`ExpandedLib/BlockNetworks/`)

Both the pipe and molten systems are instances of one generic block-network framework.
A network is a connected graph of same-type nodes; the library owns the **graph-level**
work (membership, merge on join, fracture on break, per-tick dispatch) while each
concrete network owns its typed state and rules.

- `INetworkNode` — the block-entity-facing contract: connector faces, network type,
  open/leaking faces, state pushes.
- `BlockNetworkNode` — the `Block` base for self-orienting nodes (placement
  orientation, wrench rotation, variant-aware display names).
- `BlockEntityNetworkNode` — the `BlockEntity` base that registers/unregisters with
  the manager and persists state.
- `BlockNetwork` — the abstract live-network instance (`PipeNetwork`,
  `MoltenNetwork`).
- `BlockNetworkModSystem` — the graph manager; concrete types register a factory via
  `RegisterNetworkType("pipe", …)` during `ModSystem.Start`.

## Building

Requires the .NET SDK and a Vintage Story install with the `VINTAGE_STORY`
environment variable pointing at it (the `.csproj` files reference game DLLs from
there).

```sh
dotnet build SteelmakingExpanded/SteelmakingExpanded.csproj   # builds all three mods
./build.ps1   # or ./build.sh — full Cake build, produces packaged release zips
```

If you use VS Code, a launch config is included — just set the `VINTAGE_STORY` env
var to your game install path.
