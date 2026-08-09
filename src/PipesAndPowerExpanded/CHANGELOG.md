# Changelog - Pipes and Power Expanded (`ppex`)

All notable changes to this mod are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[Semantic Versioning](https://semver.org/). For changes before this file existed,
see the git history.

## [0.6.7] - 2026-08-09

### Changed

- **The engine fluid pump's configured rate is the rate you get.** It was quietly multiplied by three
  in code, so the number in the config meant nothing on its own. That factor is folded into the
  default and the pump now moves exactly what it says: 30 L/s on a Watt, 20/40/80 on a Cornish.
  Sized at three mechanical pumps to one engine pump on the same engine.
- The hand pump's delivery head is a config key rather than a fixed value.

## [0.6.6] - 2026-08-09

Covers the 0.6.4 and 0.6.5 development bumps, which were never published separately.

### Added

- **Mechanical Fluid Pump** - a walking-beam pump driven from the mechanical power
  network instead of by steam, so it can fill a boiler whose fire is out. It sits between
  the hand crank and the engine pump: 8 L/s at full axle speed against a fixed 1.5 atm
  head, scaling down to nothing below half speed. Right-click constructed - the grid
  recipe gives a wooden frame, and the axle, piston, pipework and reservoir follow. The
  axle couples on the east face; water is drawn from beneath the far cell and delivered
  from its top.

### Changed

- **The mechanical-power overstress ceiling scales with the shaft.** It judged a shared
  network's whole resistance against a single engine's rating, so adding engines could not
  raise it and a bank stalled well below what it should carry. The load an engine holds
  per unit of power was raised from 0.875 to 1.37 to match, which puts three Cornish
  engines on one shaft at roughly 500 W where they previously reached about 320 W.
- **The engine pump's `x3` throughput factor is now a named constant.** It is playtest
  calibration rather than a stray coefficient: the quoted rates are what the pump actually
  delivers once the intake draw and the output main's free capacity bite. Both the pump's and
  the blower's rates are now pinned by tests.

### Fixed

- **Engine stroke sounds no longer fire on a backward step.** The cycle predicate read a
  decreasing frame as "the animation wrapped", but a backward-running cycle arrives the
  same way - so a single engine emitted roughly twenty plays a second per keyframe instead
  of two a revolution. A handful of machines then exhausted the game's concurrent-sound
  cap, at which point all audio starts being dropped, the game's own included.
- **Pipe bends no longer pop out of the world** when a neighbour changes beside a
  connector that faces a solid wall.
- **A blocked or over-pressured line no longer bursts from one long server tick.**

## [0.6.3] - 2026-06-21

### Added

- **Manual boiler draining with buckets** - take water back out of a boiler by hand.
- **Localizable measurement units.** `.exmod measure` reports your display units and
  `.exmod measure metric` / `imperial` switches them (L/atm/°C vs gal/psi/°F); a
  display-only change, the simulation stays metric.
- **Recipe-cost levels** for ppex's construction recipes, switchable via the shared
  `/exmod recipes` command.
- **Russian and Ukrainian** translations.

### Changed

- **Boilers no longer have an upper boil limit** - water is gated on the way in, so
  the old hard cap was removed - and the boiler **water-draw speed is gated to
  10 L/s**, so it no longer gulps its whole intake buffer in one tick.
- An **open boiler lid drops pressure to 0 atm while idle**.
- **Molten chiselling generalized** into the shared behaviour (consistent
  tool/sound/recovery handling).
- **Boilers no longer drop their base block when broken** - a broken boiler scatters
  its build materials (custom salvage ratio) instead of dropping the whole mega-block.
- **Raised break-tool requirements** for mega-blocks.
- Machines **read live config changes** without a world reload.

### Fixed

- The **Watt engine** now displays its togglable pressure band correctly.
- The **Cornish engine** now correctly costs bricks to construct.
- Assorted **valve** issues.
- Network blocks that are not pipes could incorrectly **burst**.
- Right-click-constructable blocks ignored their **last construction stage** when
  computing dropped materials.
- **Handbook**: command strings displayed incorrectly, and measurement units did not
  refresh mid-session after a `.exmod measure` change.
- Block display-name ordering and assorted localization issues.

## [0.6.2] - 2026-06-18

### Added

- The **handbook now documents** the mod's chat commands.

The boiler bucket-draining and localizable measurement-unit work from this cycle are
listed under 0.6.3 above.

## [0.6.1] - 2026-06-16

### Added

- **Craftable iron and steel gears** for the machine recipes.

### Changed

- Tuned **steam-engine power scaling**.

### Fixed

- The **fluid network** now displays pressures below 1 atm; corrected engine power
  calculation.

## [0.6.0] - 2026-06-14

### Changed

- Build and packaging maintenance (resolved Cake build warnings) ahead of the new
  publish pipeline.

## [0.5.1] - 2026-06-14

### Added

- **Manual hand-cranked fluid pump** - an engine-free water pump.

## [0.5.0] - 2026-06-13

The first release of **Pipes and Power Expanded**, split out from Steelmaking
Expanded as the home of the new steam-power system.

### Added

- **Unified pipe network** carrying gas, steam or water, with network-wide pressure
  and temperature.
- **Boilers** and **steam engines** (Watt and Cornish).
- **Sub-machines** driven by the engines: a water pump and an air blower.
- **Gas/pressure valves**, a directional **pressure-relief valve**, and a **condenser**.
- **Mechanical-power integration** so engines can drive vanilla MP machines.
