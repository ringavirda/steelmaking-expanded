# Changelog - Steelmaking Expanded (`smex`)

All notable changes to this mod are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[Semantic Versioning](https://semver.org/). For changes before this file existed,
see the git history.

## [0.9.8] - 2026-08-13

Requires Expanded Library 0.7.2, which stops the blast furnace parts and the cowper heat sink losing
their state when an update renames them. If a furnace has been going out since the last update, or a
heat sink stopped reporting a temperature, that is the fix.

### Changed

- **Roasted iron ore is worth more in the burden than raw ore.** Roasting is a heat step in its own
  right, so the furnace pays for it: a roasted piece counts 14 ore units against raw ore's 12, about
  1.98x what the same ore returns down the bloomery route against 1.70x. Set `HopperRoastedOreBonus`
  to 0 to price the two the same.
- **The furnace no longer takes vanilla crushed iron when Industrial Story is installed.** That mod
  routes every ore and nugget to its own crushed items and leaves iron bits as the only thing that
  pulverizes into `crushed-iron` - and bits are what the furnace itself drops, so feeding them back
  returned more iron than they were cast from. Industrial Story's own crushed and roasted ores are
  taken as before; nothing changes without it installed.
- **Iron feed is matched on the whole item code rather than a prefix**, so another mod's item cannot
  be accepted as full-value iron ore by sharing the first few letters of one.

### Fixed

- **The molten metal tap dropped an unknown item when broken.** It handed back a code that stopped
  existing when the taps gained refractory tiers. A world that had placed one before then still
  resolves that code - to the placeholder the game keeps for it - so the fallback never ran and the
  tap dropped something unusable. Only ever affected worlds carried across that change, which is why
  the handbook looked right. It now drops its own tier, facing north.
- **A roasted iron nugget is accepted by the reinforced hopper.** Industrial Story roasts limonite
  and siderite rather than crushing them, so on that route the roasted nugget is the first solid form
  the ore takes - and the hopper refused it with nothing on screen to say why.
- **Two patch errors on every server start with Industrial Story installed.** Our nugget crushing
  patch fought that mod's ore overhaul, which removes limonite and galena crushing outright because
  those ores must be roasted first. The patch now stands aside for any mod that owns nugget crushing,
  as it already did for Expanded Matter, and stops rewriting values those mods had set deliberately.
- **A molten canal or barrel no longer swallows metal it cannot hand back.** Pouring a metal with no
  bit item of its own - another mod's ingot, say - left the cell empty and dropped nothing at all.
  It comes back as slag now, the same as the converter already did.

## [0.9.7] - 2026-08-09

The furnace rebalance. Three long-standing complaints and one exploit, plus the machine retune they
pull in.

### Added

- **The blast furnace takes raw iron nuggets.** Limonite, hematite and magnetite go into the iron
  slot alongside crushed ore, counted by ore content rather than by the piece the way the fuel slot
  counts carbon - and worth the same as crushed ore, so 12 of either makes a batch and the two mix
  freely. The furnace takes its iron however it comes; running it through a pulverizer first is a
  convenience on this route rather than a requirement.
- **Crushed ore and iron nuggets state what they are worth in the furnace.** Their tooltips now
  carry the units of iron one piece finally yields, next to the bloomery figure vanilla already
  shows.

### Changed

- **Melting is a rate now, not a gate.** The furnace no longer sits at a ceiling waiting for a
  condition you cannot see. How fast it melts is set by two things at once - how far the hearth is
  over iron's melting point, and how much of the blast it asked for actually arrived - and both are
  on the block info, along with the multiplier it is running at.
- **Cold blast melts iron.** A furnace blown with plain air reaches 1543 C, comfortably over the
  1482 C melting point. Regenerator stoves are an improvement - a higher ceiling, faster heating,
  faster melting, up to about 3.3x - and no longer a requirement. A part-charged stove is worth a
  proportional share of that, rather than nothing until it crosses a threshold.
- **The blast furnace out-yields a bloomery.** 102 units of iron per melt cycle against 60, roughly
  1.7x what a bloomery returns for the same ore.
- **Burden is never destroyed.** It no longer turns to slag when a furnace goes out. A lit pile
  with no furnace drawing air through it simply burns down and goes cold, still burden, ready to
  be lit again.
- **No more 20-minute campaign clock.** A fed furnace runs until you stop it. A full reservoir and a
  blocked flue now stall production instead of ending the run - tap the vessel or reopen the exhaust
  and it picks straight back up.
- **The converter remelts scrap.** Right-click it holding iron or steel bits. There is no fixed
  limit: cold scrap drags the bath's temperature down, and the blow stalls if it falls under the
  refining floor - so more blast pressure buys you more scrap. Pressure also decides how fast the
  blow runs.
- **The steam air blower is worth building.** It now feeds several furnaces, or one driven hard,
  where the mechanical blower covers exactly one at baseline. The engine-driven water pump moves
  three times what a mechanical pump does on the same engine.
- **Canals reach.** A run used to deliver nothing from about eight blocks out. Canal capacity and
  throughput are both doubled, and the flow rule no longer costs a chunk of head at every block.
- **Gearing up the twin-tub blower is now the intended build, and pays a falling return.** Output
  follows a saturating curve rather than a straight proportion - worked harder the tubs have less
  time to refill - so one large gear turns the bellows 5.5x faster for 3x the air: about 20 L/s from
  a bare waterwheel against 60 L/s geared. One geared wheel runs one blast furnace with headroom
  where it used to run four, and no gear train reaches what a steam air blower makes.
- **The blast furnace breathes while it is being lit.** It drew nothing through its tuyeres until
  the whole charge had caught, so a furnace could be lit at leisure and blown afterwards. Piles
  catching one by one now need the blast from the first one, and an unblown hearth goes out.
- **Blast at working pressure is a requirement, not just the melt's throttle.** Losing it stops
  production at once and puts the furnace out on the usual disruption grace - long enough for a
  deliberate cowper swap, not long enough for stopped blowers.
- **Blast mix is now called burden**, which is what a blast furnace charge of ore, fuel and flux is
  actually called - and what the Russian and Ukrainian translations already called it. Existing
  stacks are converted on world load.
- Cowper stoves cool when idle and drain faster under a heavy blast, so alternating two of them is a
  real decision rather than a formality.
- **Cold scrap costs the converter's bath its heat, and blast pressure buys it back.** There is no
  fixed scrap limit: scrap is cold mass on the heat balance, and the limit is wherever it drags the
  bath under the refining temperature. On the minimum blast that is about 15% of the vessel; at 6
  atm, as hard as a Cornish engine driving an air blower can blow, it is 40%. The return falls away
  as you push - the first atmosphere over the gate is worth far more than the fourth - so half a
  vessel of scrap is out of reach at any pressure. A full stack of 128 bits is 27% of capacity and
  wants about 3 atm behind it.
- **The blow's air draw and speed climb across the same band.** They used to reach full rate at 4
  atm, so pressure above that bought scrap tolerance for nothing. A converter now draws 12 L/s at
  the gate and 48 L/s at 6 atm, and refines proportionally faster for it.
- **Scrap returns as steel unit for unit.** The 3% burn-off is gone; material loss on a remelt is not
  this mod's idea.
- The converter's look-at panel is three lines instead of five: one charge line carrying the fill,
  the split between molten metal and scrap, and what the scrap is costing; then blast supplied
  against blast needed; then pressure against the gate. Nothing is stated twice.

### Removed

- **Iron bits no longer crush into crushed iron.** That loop was break-even at the old furnace yield
  and would have printed iron at the new one. Scrap goes to the converter instead, which is where
  remelting belongs.

### Fixed

- **The blast furnace build projection ignored the door's facing**, showing the outline as if the
  door faced north. It now follows the door, and keeps up if the door is rotated after placement.
- **The blast furnace door, tuyeres and molten metal taps have refractory tier variants.** All three
  always showed tier-3 brick whatever you built with; their recipes now hand back the tier you spent.
  Existing placements become tier 3.
- **Refractory tiers are named.** The door, tuyere, tap, heat sink and smoke stack intake all read as
  one block in the inventory; each now carries its tier in its name, the way the pipe blocks do.
- The heat sink used a brick texture that does not cover a full face.
- The Bessemer converter is raised from tier-2 refractory brick but was drawn in tier-3. It now
  looks like what it is built from.
- Engine sub-machines - the air blower, the water pump and the mechanical generator - now report what
  they are producing. The generator also says when the shaft is loaded past what the engine can hold.
- Charging scrap into the converter moved from the control panel to the vessel's upper hatch, next to
  the chisel-out.
- A clogged mold pedestal could not be chiselled clear. The interaction hint advertised it and the
  click did nothing.
- A barrel parked under a canal tap cooled roughly twelve times faster than a barrel anywhere else.
- Chiselling a barrel returned half the metal that chiselling anything else did.
- A cowper stove held its charge forever while idle.
- A furnace with less than a full cycle's charge in the hearth produced a whole cycle's iron.
- **Machines behind a gear train read the wrong shaft speed.** The mechanical port ignored its own
  gear ratio, so the twin-tub blower, the water pump and the mechanical generator reported the
  drive's speed or their own depending on which end of the network loaded first - gearing appeared
  to do nothing, or to multiply output, run to run.
- A burden batch mixing crushed ore and nuggets could quietly cost up to 8 ore units more than the
  batch was worth, because the last nugget cannot be split. The surplus is now banked against the
  next batch, so a long run pays exactly the advertised rate per item.
- **The handbook sent players to the wrong block to charge scrap.** It said to right-click the
  control; the vessel takes scrap at its upper hatch, the same cell a frozen heat is chiselled out
  of. It also now says that one click charges the whole stack in hand.
- **The handbook promised a five-minute blow.** The blow's length follows the blast now - about ten
  minutes on the 2.5 atm gate, two and a half at 6 atm - and the article said five regardless.
- **The cowper stove's four gas ports are documented.** Which one takes exhaust, which gives hot
  blast, which takes air and which vents was left to be worked out by trial.
- **Cowper stoves discharged far too fast.** The heat a stove gave up was scaled by the gas standing
  in the whole cold main rather than by the air passing through the stove, so the length of your
  blast main set the discharge rate: a modest run emptied a full stove in about half a minute
  instead of seven.
- **The converter reported 0 atm with blast standing at its intake.** The panel is drawn on the
  client, where the pipe network it was asking cannot be reached. The blast figures are measured
  during the blow now and sent with the rest of the machine's state.
- **The converter's fill figure ignored charged scrap**, though scrap takes up the same room, so a
  vessel reading half full could refuse to take more.
- **Charging scrap took every matching stack off the hotbar** instead of the one in hand. Holding
  iron bits also swallowed your steel. It now takes only what you are holding.
- **Iron and steel scrap are tracked apart and come back apart.** Breaking or chiselling a vessel
  mid-charge returned one lump of whatever the molten charge happened to be; each kind now returns
  as itself. Scrap charged into an empty vessel - the order the converter expects - was destroyed
  outright when the vessel was broken.

### Configuration

Every process constant above is a config key, live-editable with `/exmod config smex`. Existing
configs pick up the rebalanced defaults automatically; anything you tuned outside that set is kept.

## [0.9.6] - 2026-08-09

Covers the 0.9.3 through 0.9.5 development bumps, which were never published separately.

### Added

- **Twin-Tub Blower** - axle-driven bellows, and the only air source that needs no steam.
  A waterwheel or windmill will now run a blast furnace, so the iron chain no longer waits
  on a boiler. It raises its run to 2.0 atm, which
  clears the furnace's blast gate and never reaches the converter's - mechanical power
  makes iron, steam is still required for steel. Right-click constructed: the grid recipe
  gives a wooden frame, and the beam, axle, tubs and pipe connection follow.
- **The blast furnace burns charcoal as well as coke.** Charcoal carries about half the
  carbon, so a batch takes four charcoal where two coke would do, and the two mix freely
  in one batch. The coke oven is still the efficient route, not the only one.
- **Every refractory structure accepts any brick tier.** Only the Bessemer converter still
  asks for a specific tier.

### Changed

- **The blast furnace's blast gate is separate from the converter's,** and lower: the
  furnace fires at 1.5 atm, the converter still needs 2.5. Iron is reachable with
  mechanical power; steel is not.
- **The heat sink is built from refractory brick** rather than iron sheet, which is what a
  regenerator is actually made of. It comes in three tiers, its recipe takes any of them,
  and existing heat sinks are converted on load.
- **The blast furnace takes coke whole; crushed coke is retired.** The intermediate existed
  only to be fed to the furnace, and producing it meant bolting a crushing recipe onto
  vanilla coke - which put this mod in the middle of every other mod's crushing economy.
  Leftover crushed coke converts to coke one-for-one as chunks load. The per-batch cost is
  restated in whole coke.
- **The blast furnace pushes its exhaust at 2 atm.** At the old implicit 1 atm the
  pressure-relief valve in the setup diagrams could never open.

### Fixed

- **A blocked flue stalls the blast furnace instead of extinguishing it.** Shutting the hot
  exhaust off to a cowper stove is exactly what the handbook's regenerator swap requires,
  but it counted as a disruption, so thirty seconds later the furnace died - dumping the
  molten iron, slagging every hearth pile and ending the campaign. A choked furnace now
  falls back to its natural draught ceiling and resumes when the exhaust reopens.
  The choke test itself was also wrong: it read "any outlet refused" where it meant "no
  outlet accepted", so piping one gas outlet and leaving the other bare choked the furnace
  permanently, and the message naming the reason never appeared.
- **The reinforced hopper's contents no longer go stale on the client.** The bell hopper
  marked itself after draining the hopper above, but the client reads those slots from the
  hopper's own block entity - so the contents looked right until the dialog was reopened,
  and relogging cleared it. It now syncs on every inventory change, including a chute
  feeding it.
- **Molten canals settle instead of sloshing.** Levelling moved the whole difference
  between two cells, so an adjacent pair inverted every tick. Taps and mold pedestals still
  take everything offered, so a run can still empty.
- **The smoke stack accepts clinker brick and every brick course colour.** The masonry
  legend enumerated seven of the eight colours and omitted clinker, so a chimney built from
  them never completed and the only feedback was the outline.
- **Slag paths and the heat sink no longer render near-black** under lighting mods; they
  declared the light absorption vanilla reserves for solid opaque cubes.
- **A rotated blast furnace no longer scans the wrong cells** for the first few ticks after
  loading.
- **Nothing is patched into another mod's crushing recipes.** The compatibility patch that
  disabled a crushing recipe by index is gone along with crushed coke itself.

## [0.9.2] - 2026-06-21

### Added

- **Bell hopper drops by default.** A freshly built blast furnace now feeds itself
  without the player first discovering the Ctrl + right-click toggle.
- **Chisel residue out of the Bessemer converter** when only a little hardened metal
  remains, instead of having to break the whole vessel; the cooldown coefficient is
  configurable.
- **Chisel molten canal taps and pedestals**, matching the canals and barrels.
- **Cooldown coefficients** for taps, barrels and pedestals (how fast their metal
  cools), and the cooldown speed now **syncs to the client** so the glow matches the
  server.
- **`/exmod molds <plate|ingot|rod|all> <on|off>`** to enable/disable the mod's tool
  molds (hidden from creative + handbook and their recipes removed when off).
- **Recipe-cost switching** for the steam/steel chains via the shared `/exmod recipes`
  command.
- **Orphaned machine blocks now self-heal** - a furnace door/tap/tuyere left without
  its block entity (a load failure or desync) gets a fresh one recreated on load
  (via the exlib healer), so it is interactable and breakable again rather than an
  inert ghost.
- **Russian and Ukrainian** translations.

### Changed

- **Molten chiselling generalized** into one shared behaviour across canals, barrels
  and the converter (consistent tool, sound, wear and recovery).
- **Converter (and boilers) no longer drop their base block when broken** - they
  scatter their build materials instead of dropping the whole mega-block.
- **Raised mining-tier / break-tool requirements** for mega-blocks.
- **Converter status strings** are more descriptive.
- Structures **read live config changes** without a world reload.

### Fixed

- The **Bessemer converter dropped itself** when broken.
- Corrected the **converter chisel cell position**.
- Fixed **engine animation direction** when powering mechanical power.
- The **molten canal tap** allowed placing barrels on structure filler blocks.
- Fixed **broken cowper-stove behaviour**.
- Fixed the **bell-hopper client/server inventory handshake** desync (grid clicks
  silently desyncing).
- Fixed **recipe item variants** and the **vanilla crushed-ore ratio** (EM compat).
- Fixed the **converter transmission recipe** rod requirements.
- Assorted localization issues.

## [0.9.1] - 2026-06-14

### Changed

- **Source-generator refactor:** block and item JSON attributes are now baked into
  generated class members instead of hand-written accessors.
- Block-info lines display measurements in the player's chosen unit system.
- The **structure filler** mirrors the block-info of the principal block it belongs to.
- Updated several block descriptions.

### Fixed

- Corrected the **Bessemer converter's mechanical resistance**.

## [0.9.0] - 2026-06-13

The **Steam Mechanics** release - steelmaking integrated with the new steam-power
system (split out into Pipes and Power Expanded; see its 0.5.0). Steel can now be
produced via the steam chain.

### Added

- **Slag and slag-path** blocks - waste output from the new processes - with several
  variants.
- **Brick variants** for the molten canals.
- **Tool molds became proper `smex` items**, and the migration system now also
  migrates items held in inventories (not just placed blocks).

### Changed

- **Rebalanced recipes** and added world migrations for older saves.

### Fixed

- Right-click construction now handles **wildcard ingredients** correctly and shows
  the **correct names for missing materials** while building.
- Quad-rod tool-mold fixes.

## [0.8.7] - 2026-06-08

### Fixed

- Mold recipes were missing their **domain**.
- Several **uncraftable recipes** and **recipe conflicts**.
- Valves **constantly rotating** in some situations.
- Possible **client crash** from the unsafe vanilla incomplete-structure highlight,
  resolved by reimplementing it safely.
- The molten network **ignored input temperature** on repeated pouring.
- Canals containing metal or solidified metal can **no longer be wrench-rotated**.
- **Blast furnace:** retains its internal state when melting stops, and no longer
  pours when not melting.
- Could incorrectly **seal solidified canals**.
- Assorted renderer issues.

## [0.8.6] - 2026-06-04

### Added

- **World-migration system** for updating old blocks to new variants (moved into the
  compiled library).
- **Brick variants** for the full-pipe blocks, with migration of existing blocks.
- More **cowper-stove and smoke-stack** variants; the bottom of the smoke stack now
  requires **refractory bricks**.

### Fixed

- Full-network blocks could not be rotated into all orientations.
- The valve animator ignored the **X component** of the rotation matrix.
- Added overlays to prevent **transparent-texture rendering** artifacts.

## [0.8.5] - 2026-06-04

### Added

- **Passthrough bend** variant.

### Fixed

- Crash from the **blast door** not dropping the correct block entity (duplicated
  vanilla `Block.GetDrops`).
- Some recipes did not return the correct **default block variants**.

## [0.8.4] - 2026-06-03

### Added

- **Lighting** for canals and barrels that hold molten contents.

### Changed

- Raised the default **Bessemer process temperature** so the melt does not solidify
  too fast.
- Removed the ability to **chisel out non-hardened metal**.

### Fixed

- The **Bessemer converter** now respects the global melting config.
- Rendered **molten surfaces** now re-render to reflect temperature changes.
- **Taps and pedestals** now actually sever the network connection when closed.
- The converter now respects the **input tap state**.
- Valve animation for **rotated valves**; valves used the wrong **interaction sound**.

## [0.8.3] - 2026-06-03

### Added

- **Rewrote the molten network** to simulate molten-iron flow between cells, with
  tuned default flow values.
- **Extensible iron-ore compat system**.

### Changed

- Updated **Bessemer converter** animation and sound.
- More forgiving **cowper-stove** defaults.

### Fixed

- Handbook formatting.

## [0.8.2] - 2026-06-02

### Changed

- **Centralized the mod config** into a single JSON file.

### Fixed

- Cowper-stove issues.

## [0.8.1] - 2026-06-02

### Added

- **Construction costs** shown in the handbook.
- **Specific interaction sounds** for all blocks.
- Multiblock structures now **log the missing block** in chat while building.

### Changed

- The **Bessemer converter** can be placed without cost in creative.
- Corrected the **iron hatch door's** name.

### Fixed

- Crash when **breaking a solidified Bessemer converter** (JSON wildcard issue).
- **Blast-furnace** solidified-drop amount.
- **Bessemer-converter and blast-furnace** global-position translation.

## [0.8.0] - 2026-06-01

### Added

- **First public release.** The Steelmaking Expanded mod: blast furnace, Bessemer
  converter, cowper stove, smoke stack, molten canals and tool molds, with their gas
  and molten block networks.
