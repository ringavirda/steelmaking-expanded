# Pipes and Power Expanded (`ppex`)

A [Vintage Story](https://www.vintagestory.at/) mod adding pipe networks and modular
steam power machinery. It is the infrastructure layer of the *Expanded* mod family and
a hard dependency of [Steelmaking Expanded](../SteelmakingExpanded/README.md).

## What it adds

- **Pipe networks** - iron and steel piping (straights, bends, T/X junctions) carrying
  one medium per network: a gas (air, blast, exhaust, steam) or water. Networks track
  volume, pressure, and a shared temperature; iron pipes burst above 5 atm, steel above
  10 atm; open ends leak.
- **Fittings** - hand valves (sever the line), directional pressure valves (overflow
  above a configurable gate), brick passthroughs/outlets (build structure walls across
  a pipe run; cap an outlet with a vanilla chimney to vent gas), fluid intakes
  (draw fresh water from a pond), and a steam condenser (the only place steam turns
  back into water).
- **Boilers** - the compact **Cornish** boiler (32 L/s steam, 5 atm) and the heavy
  **Lancashire** boiler (48 L/s, 12 atm). Both are raised through right-click
  construction stages over a fire-brick firebox, burn coal piles, and explode if left
  over-pressured.
- **Steam engines** - the low-pressure **Watt** engine (2-4 atm) and high-pressure
  **Cornish** engine (6-8 atm, three steam throttle settings). Each engine drives one
  attached sub-machine:
  - **MP Generator** - constant-power axle drive for vanilla machines,
  - **Fluid Pump** - moves water into a pressurised output line (boiler feed),
  - **Air Blower** (from Steelmaking Expanded) - makes Blast for the furnace.
- **Pumps that need no engine** - the hand-cranked **Manual Fluid Pump** (2 L/s at
  1 atm) and the axle-driven walking-beam **Mechanical Fluid Pump** (8 L/s at a fixed
  1.5 atm, since the beam lifts the same column at any speed). Both are transfer
  devices over a fluid intake, and both fill a boiler whose fire is out.

In-game **handbook articles** (`Steam Power: …`) cover build costs, operating steps and
failure modes; all gameplay numbers live in `ModConfig/ppex_values.json` (see `PpexConfig.cs`),
with recipe and construction costs in `ModConfig/ppex_recipes.json`.

## Code layout

- `BlockNetworkPipe/` - the unified pipe network (`PipeNetwork`), pipe/valve/intake/
  condenser blocks and block entities.
- `BlockStructures/` - boiler, engine, manual-pump and mechanical-pump mega-block
  machines (multiblock structure + right-click construction + animation).
- `Commands/` - `.exmod` sub-commands (the metric/imperial `measure` unit toggle).
- `Preferences/` - the per-player display-unit preference definition.
- `Patches/` - Harmony patches into vanilla (chimney look-at info).
- `BlockMigrations/` - save migrations for renamed block codes.
- `assets/ppex/` - blocktypes, shapes, recipes, lang, handbook pages.

Depends on [Expanded Library](../ExpandedLib/README.md) (`exlib`) for the block-network,
multiblock-structure, config, command and recipe-cost frameworks.

## Building

Requires only the .NET SDK - provision the game binaries into the repo first (see the
[root README](../../README.md#building)), then build:

```sh
scripts/provision-game.sh -Version 1.22.0   # or scripts/provision-game.ps1 on Windows
dotnet build src/PipesAndPowerExpanded/PipesAndPowerExpanded.csproj
```
