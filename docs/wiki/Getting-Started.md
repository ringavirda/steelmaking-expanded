# Getting Started

This page gets a third-party mod consuming `exlib`: declaring the runtime dependency, wiring a
project reference so you can call its APIs, and registering your first attribute-marked block.

## 1. Depend on exlib at runtime

`exlib` is a separate `Code` mod. In your mod's `modinfo.json`, add it under `dependencies`
with the minimum version you build against:

```json
{
  "type": "Code",
  "modid": "yourmod",
  "name": "Your Mod",
  "version": "1.0.0",
  "dependencies": {
    "game": "1.22.0",
    "exlib": "0.7.0"
  }
}
```

At load time the game ensures `exlib` is present and loaded before your mod, so its
`ModSystem`s (the block-network manager, migration sweeper, healer, `/exmod` root) are already
up when your `Start`/`StartServerSide`/`StartClientSide` run.

> **Game versions.** `exlib` targets 1.22 but the family also builds and runs on 1.21 and 1.20
> via the `Legacy/` shim. If you only target 1.22 you can ignore the shim entirely; the public
> APIs on this wiki are the same across versions unless a page says otherwise.

## 2. Reference exlib at compile time

To call exlib's types you need a reference to `ExpandedLib.dll`. Inside this monorepo that is a
plain `ProjectReference`:

```xml
<ItemGroup>
  <ProjectReference Include="..\ExpandedLib\ExpandedLib.csproj" />
</ItemGroup>
```

If you build your mod outside this repo, reference the shipped `exlib.dll` (the same DLL players
install) with `<Private>false</Private>` so you don't bundle a second copy.

> **One library identity.** In this monorepo only **one** mod assembly physically contains
> `ExpandedLib` (it is compiled into its own `exlib.dll`), and the other mods project-reference it so the
> network-manager and registries are a single identity at runtime. If you ship your own mod
> separately you simply depend on the installed `exlib` mod - there is exactly one `exlib` in a
> running game.

## 3. Register your content

exlib is **attribute-driven**: you tag classes, and a one-line call in `Start` registers them
all by reflection. No manual `api.RegisterBlockClass(...)` lists to maintain.

```csharp
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Common;

[BlockRegister]                       // registers as "yourmod.BlockMachine"
public partial class BlockMachine : Block { }

[BlockEntityRegister]                 // registers as "yourmod.BlockEntityMachine" (+ short aliases)
public class BlockEntityMachine : BlockEntity { }

public class YourModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        // Scans THIS assembly for every [BlockRegister]/[ItemRegister]/[BlockEntityRegister]/
        // [*BehaviorRegister] class and registers each with the matching game registry.
        EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

        // Scans for [CommandRegister]/[SubCommandRegister] classes (optional).
        CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);
    }
}
```

Because blocks are singletons, marking the class `partial` lets the
**[attribute source generator](Source-Generators)** surface the block's JSON `attributes` as
typed C# members on the same class - no `Attributes["..."].AsFloat()` boilerplate.

See **[Registries](Registries)** for every attribute and the full registration story.

## 4. Pick the system you need

| You want to... | Read |
| --- | --- |
| Build pipes / wires / canals (anything that connects into a network) | [Block Networks](Block-Networks) |
| Build a multi-cell machine (furnace, boiler) with completion + build outline | [Multiblock Structures](Multiblock-Structures) |
| Run periodic server-side work on a block entity | [Production Machines](Production-Machines) |
| Use the vanilla right-click-construction flow with salvage drops | [Construction (RCC)](Construction) |
| Ship gameplay tunables players can edit live | [Config System](Config-System) |
| Add a `/exmod` (server) or `.exmod` (client) sub-command | [Commands](Commands) |
| Offer cheap/normal recipe-cost levels | [Recipe Costs](Recipe-Costs) |
| Rotation math, particles, sounds, inventory counting, content gating | [Helpers & Renderers](Helpers-and-Renderers) |
| Rename/remove blocks in old saves without orphaning them | [Migrations & Healing](Migrations-and-Healing) |
| Unit/integration-test all of the above headlessly | [Testing Harness](Testing-Harness) |
