# Steelmaking Expanded (`smex`)

A [Vintage Story](https://www.vintagestory.at/) mod adding an industrial-era iron and
steel production chain on top of vanilla metalworking. Requires
[Expanded Library](../ExpandedLib/README.md) (`exlib`) and
[Pipes and Power Expanded](../PipesAndPowerExpanded/README.md) (`ppex`).

## What it adds

- **Blast furnace** - a tall multiblock of refractory brick in any tier, fed by a
  hopper pair that combines crushed iron ore, lime and fuel into blast mix. The fuel
  slot takes coke or charcoal, counted by carbon (2 coke or 4 charcoal per batch,
  mixing freely). Fired and held above iron's melting point, it pools molten iron
  and slag.
- **Hot blast machinery** - cowper stoves that recycle furnace exhaust into scorching
  blast air, a smoke stack that vents the surplus, and two ways to pressurise the
  line: a steam-driven air blower (a `ppex` engine sub-machine), or the axle-driven
  **twin-tub blower**, which reaches 2.0 atm - past the furnace's 1.5 atm gate and
  short of the converter's 2.5 atm, so mechanical power makes iron and steam makes
  steel.
- **Molten canal network** - liquid metal is plumbed, not carried: rock-built canals,
  furnace taps, a pouring canal tap, mold pedestals, and molten barrels for bulk
  storage. Metal cools in the canals and solidifies if neglected.
- **Casting** - new plate / quad rod / double ingot ceramic molds, plus casting of
  large molds (anvil, helve hammer) directly under a canal tap. Still-liquid molds can
  only be carried in an empty hand and burn unprotected skin.
- **Bessemer converter** - stage II: a 3×3×3 vessel of tier-2 refractory brick that
  takes mechanical power and a 2.5 atm blast line and blows molten iron into steel,
  poured back out through the same canals.
- **Slag chain** - solidified slag grinds into powdered slag, usable as mortar
  ingredient or phosphate fertilizer; scrap iron bits crush back into crushed iron.

The in-game **handbook** ships five articles (overview, blast furnace, hot blast,
casting, Bessemer) with full build costs and operating procedures. Gameplay tunables
live in `ModConfig/smex_values.json` (see `SmexConfig.cs`), with recipe and
construction costs in `ModConfig/smex_recipes.json`.

## Code layout

- `BlockNetworkMolten/` - the molten-metal network, canal/tap/pedestal/barrel blocks,
  and the shared molten-chiselling behaviour.
- `BlockStructures/` - blast furnace, cowper stove, smoke stack, Bessemer converter,
  the air-blower engine sub-machine and the twin-tub mechanical blower.
- `Molds/` - the tool-mold blocks and their config-gated enable/disable.
- `Commands/` - server `/exmod` sub-commands (the `molds` toggle).
- `Patches/` - Harmony patches into vanilla (tool mold filled-mold flow + held
  rendering, mold rack spill rule, coal pile blast-mix burn-to-slag).
- `Compat/` - other-mod compatibility (extra crushed-iron-ore item codes).
- `BlockMigrations/` - save migrations for renamed block codes.
- `assets/smex/` - blocktypes, shapes, recipes, patches, lang, handbook pages.

## Building

Requires only the .NET SDK - provision the game binaries into the repo first (see the
[root README](../../README.md#building)), then build:

```sh
scripts/provision-game.sh -Version 1.22.0   # or scripts/provision-game.ps1 on Windows
dotnet build src/SteelmakingExpanded/SteelmakingExpanded.csproj
```
