# Pipes and Power Expanded (`ppex`)

A [Vintage Story](https://www.vintagestory.at/) mod adding pipe networks and modular
steam power machinery. It is the infrastructure layer of the *Expanded* mod family and
a hard dependency of [Steelmaking Expanded](../SteelmakingExpanded/README.md).

## What it adds

- **Pipe networks** — iron and steel piping (straights, bends, T/X junctions) carrying
  one medium per network: a gas (air, blast, exhaust, steam) or water. Networks track
  volume, pressure, and a shared temperature; iron pipes burst above 5 atm, steel above
  10 atm; open ends leak.
- **Fittings** — hand valves (sever the line), directional pressure valves (overflow
  above a configurable gate), brick passthroughs/outlets (build structure walls across
  a pipe run; cap an outlet with a vanilla chimney to vent gas), fluid intakes
  (draw fresh water from a pond), and a steam condenser (the only place steam turns
  back into water).
- **Boilers** — the compact **Cornish** boiler (32 L/s steam, 5 atm) and the heavy
  **Lancashire** boiler (48 L/s, 12 atm). Both are raised through right-click
  construction stages over a fire-brick firebox, burn coal piles, and explode if left
  over-pressured.
- **Steam engines** — the low-pressure **Watt** engine (2–4 atm) and high-pressure
  **Cornish** engine (6–8 atm, three steam throttle settings). Each engine drives one
  attached sub-machine:
  - **MP Generator** — constant-power axle drive for vanilla machines,
  - **Fluid Pump** — moves water into a pressurised output line (boiler feed),
  - **Cornish Air Blower** (from Steelmaking Expanded) — makes Blast for the furnace.

In-game **handbook articles** (`Steam Power: …`) cover build costs, operating steps and
failure modes; all gameplay numbers live in `ModConfig/ppex.json` (see `PpexValues.cs`).

## Code layout

- `BlockNetworkPipe/` — the unified pipe network (`PipeNetwork`), pipe/valve/intake/
  condenser blocks and block entities.
- `BlockStructures/` — boiler and engine mega-block machines (multiblock structure +
  right-click construction + animation).
- `Patches/` — Harmony patches into vanilla (chimney look-at info).
- `BlockMigrations/` — save migrations for renamed block codes.
- `assets/ppex/` — blocktypes, shapes, recipes, lang, handbook pages.

Depends on [Expanded Library](../ExpandedLib/README.md) (`exlib`) for the block-network
and multiblock-structure frameworks.

## Building

Requires the .NET SDK and a Vintage Story install with the `VINTAGE_STORY` environment
variable pointing at it:

```sh
dotnet build PipesAndPowerExpanded/PipesAndPowerExpanded.csproj
```
