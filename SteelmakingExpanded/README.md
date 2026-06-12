# Steelmaking Expanded (`smex`)

A [Vintage Story](https://www.vintagestory.at/) mod adding an industrial-era iron and
steel production chain on top of vanilla metalworking. Requires
[Expanded Library](../ExpandedLib/README.md) (`exlib`) and
[Pipes and Power Expanded](../PipesAndPowerExpanded/README.md) (`ppex`).

## What it adds

- **Blast furnace** — a tall refractory multiblock fed by a hopper pair that combines
  crushed iron ore, crushed coke and lime into blast mix. Fired and held above iron's
  melting point, it pools molten iron and slag.
- **Hot blast machinery** — cowper stoves that recycle furnace exhaust into scorching
  blast air, a smoke stack that vents the surplus, and a steam-driven air blower
  (a `ppex` engine sub-machine) that pressurises the line.
- **Molten canal network** — liquid metal is plumbed, not carried: rock-built canals,
  furnace taps, a pouring canal tap, mold pedestals, and molten barrels for bulk
  storage. Metal cools in the canals and solidifies if neglected.
- **Casting** — new plate / quad rod / double ingot ceramic molds, plus casting of
  large molds (anvil, helve hammer) directly under a canal tap. Still-liquid molds can
  only be carried in an empty hand and burn unprotected skin.
- **Bessemer converter** — stage II: a 3×3×3 vessel that takes mechanical power and a
  Blast line and blows molten iron into steel, poured back out through the same canals.
- **Slag chain** — solidified slag grinds into powdered slag, usable as mortar
  ingredient or phosphate fertilizer; scrap iron bits crush back into crushed iron.

The in-game **handbook** ships five articles (overview, blast furnace, hot blast,
casting, Bessemer) with full build costs and operating procedures. Gameplay tunables
live in `ModConfig/smex.json` (see `SmexValues.cs`).

## Code layout

- `BlockNetworkMolten/` — the molten-metal network, canal/tap/pedestal/barrel blocks.
- `BlockStructures/` — blast furnace, cowper stove, smoke stack, Bessemer converter.
- `Patches/` — Harmony patches into vanilla (tool mold filled-mold flow + held
  rendering, mold rack spill rule, coal pile blast-mix burn-to-slag).
- `Compat/` — other-mod compatibility (extra crushed-iron-ore item codes).
- `BlockMigrations/` — save migrations for renamed block codes.
- `assets/smex/` — blocktypes, shapes, recipes, patches, lang, handbook pages.

## Building

Requires the .NET SDK and a Vintage Story install with the `VINTAGE_STORY` environment
variable pointing at it:

```sh
dotnet build SteelmakingExpanded/SteelmakingExpanded.csproj
```
