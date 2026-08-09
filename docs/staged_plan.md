# Industrialization Roadmap - Full Design Specification

Companion design document to [`staged_plan.png`](staged_plan.png). The **complete, self-contained
spec** for the staged expansion of the Expanded mods (IMEX / PPEX / SMEX) into a coherent
19th-century ferrous-metallurgy and steam-power progression. Every process is given as an explicit
**input -> output**, every machine has a reference card, and all interaction/timing rules are pinned
down.

> **How to read this.** Numbers (volumes, rates, unit values, pressures) are the **proposed baseline
> economy** and are **config-tunable** via the exlib config system unless stated otherwise. Values
> already fixed by the live codebase are marked *(live)*. Genuine unknowns are collected in
> [Open balance questions](#16-open-balance-questions) - everything else is decided.

---

## 1. Goals & design pillars

- **Cheap kickstart.** Iron-tier machinery (water + cast iron) bootstraps everything. Steel is never
  required to *start* the system; it is the reward for progressing.
- **Smooth, gapless progression.** Each stage's output material is the next stage's construction
  requirement - a linear tech tree with no hard circular dependencies. **Construction is gated by
  material and machine-building, not by ore rarity.**
- **Immersive + historically grounded.** Real processes, machines and materials; deliberate gameplay
  liberties are called out where taken.
- **Industrialization pays off in bulk vanilla goods** (Sec 12), not just in more machines.
- **No GUI windows** unless genuinely unavoidable; all interaction is in-world and verb-based, all
  state readable from block-info (Sec 10).

---

## 2. Conventions, units & global rules

| Quantity | Unit | Notes |
|---|---|---|
| Metal mass | **units (u)** | Vanilla: 100 u = 1 ingot. |
| Fluid/gas volume | **litres (L)** *(live)* | Pipe segment = 30 L *(live)*. Litres everywhere, never m^3. |
| Mechanical power | **MP** *(live)* | Vanilla MP network; constant-power generator model (Sec 5.3). |
| Steam/water flow | **L/s** | Per-tick flow EMA-smoothed for the throughput readout *(live)*. |
| Temperature | **degC** | One network-wide pipe temperature *(live)*; molten canals are per-cell. |
| Pressure | **atm** | 1 atm = ambient. LP steam <= ~3 atm; HP steam ~8-12 atm (tunable). |
| Electricity | **AC / DC, volts (V)** | AC also carries Hz + phase. Two coupled networks, Stage V (Sec 5.4). |

**Global rules**

- A pipe network is a **single medium** at a time (gas *or* water) with unified
  Volume/Temperature/Pressure/MediumType *(live)*. Air, steam and exhaust are gas media; water and
  condensate are liquid media.
- Molten metal lives in **per-cell molten canals** (each block owns its metal; flows cell to cell)
  *(live)*. The **ladle** (Sec 7) is the only block that merges canals and mixes metals.
- Stock/billet items carry their remaining mass in a **unit-count stack attribute**, so any cut or
  divide step is exact arithmetic.
- **Mass-conserving - no hidden yield loss.** Every smelt/convert/refine step preserves input mass
  (1 u in -> 1 u out of the new material). Slag, smoke and fume are **cosmetic only**. Process *tiers*
  differ in **throughput and fuel cost, never material yield**, so a run's output is always
  predictable.
- **Steam machinery only:** billets and profiled stock cannot be worked on a vanilla anvil - only on
  the rolling mill / steam hammer.

**Block-size vocabulary** (Footprint column, Sec 8):

- **block** - a single 1x1x1 block.
- **megablock** - occupies more than one cell via the **filler-block** mechanic; often a
  RightClickConstructable (RCC).
- **multiblock** - a structure the player **builds by hand in a specific shape**, guided by an
  in-world **projection**. Blocks *and* megablocks can be parts of a multiblock.

A block can be **both** - e.g. boilers are RCC **megablocks** (one cell, filler footprint) whose
construction is also gated by a **multiblock** projection.

---

## 3. World-gen & raw inputs

Progression is gated by **building machines and learning the alloying mechanic (Sec 7)**, not by hunting
rare ore. The alloy elements below are ordinary worldgen inputs; only one ore is new.

| Alloy element | Ore | New? | Needed for |
|---|---|---|---|
| **Manganese** | rhodochrosite | vanilla | Hadfield steel |
| **Chromium** | chromite | vanilla | HSS |
| **Tungsten** | **wolframite** | **new** | HSS (~18 % W - no vanilla equivalent) |

Vanilla raw inputs reused: iron ore (limonite/hematite/magnetite), copper, cassiterite (tin),
sphalerite (zinc), coal -> **coke**, **limestone** (flux), clay, fireclay/refractory tiers, plus the
alloy ores above.

**Vanadium is omitted** - HSS is specced as a **tungsten + chromium** hot-hard tool steel. This is
period-accurate: the original high-speed steels (Mushet 1868, Taylor-White ~1900) were **W-Cr with no
vanadium** (a later ~1904 refinement).

**Ore crushing & the steam crusher (a real gate).** Early on, ore is crushed with the **vanilla
pulverizer**. The **steam ore crusher** (Sec 8) is the **mass** processor - bulk nugget -> crushed ore to
feed the furnaces - and a **hardness gate** keyed to steam pressure (same output, **no yield bonus**;
the gate is *access*, not yield):

- **LP steam (Stage III):** iron and softer ores, plus **stone / limestone / common rock** - a bulk
  alternative to the pulverizer. **Rhodochrosite (Mn) is a vanilla ore: crush it on the vanilla
  pulverizer *or* the LP crusher** - never gated to mod blocks; only the hard ores below are
  crusher-exclusive.
- **HP steam (Stage IV):** adds the **hard ores - chromite & wolframite** - which the pulverizer and the
  LP crusher **cannot** process, gating the **HSS** alloy elements behind HP steam. The same HP jaws
  crush **hardened steel scrap** for the arc-furnace recycle loop (Stage V).

---

## 4. Material catalogue

| Material | Stage | Produced from (process) | Carbon/character | Primary use |
|---|---|---|---|---|
| **Pig iron** | I | Blast furnace (molten) -> sand "pigs" | very high C, brittle | Feedstock for cupola & puddling |
| **Cast iron** | I | Cupola remelts pig -> molds | high C, castable, brittle | Castable components; **substitutes vanilla iron in build recipes** |
| **Wrought iron** | I | Puddling pig -> shingling | low C, tough, fibrous | **= vanilla iron**; forgeable bar/plate |
| **Blister steel** | Ia | Cementation of wrought bars | carburised surface | Crucible-steel feedstock only |
| **Crucible steel** | Ia | Draft crucible furnace melts blister | homogeneous high C | Tools/weapons (**+durability +damage**) |
| **Mild steel** | III / IV | Bessemer (**high-N** ~150 ppm) or open hearth (**low-N** ~50 ppm) | low C | **= vanilla steel**; tracks a **nitrogen ppm** attribute; **low-N required for boiler plate / pressure parts + HSS feedstock** |
| **Ingot iron** | III / V | Bessemer **over-blow**, or arc-smelted iron ore | ~0 % C, slag-free | Soft; **not wrought iron** - recarburise or re-melt |
| **Hadfield steel** | III/IV | Bessemer mild steel + manganese (ladle) | Mn austenitic, work-hardening | **HP machinery only** (boilers, cylinders); **not** tools/weapons |
| **Copper (blister/converter)** | IIIa | Reverberatory/arc -> converter | impure | Rods, impure wire, bronze base |
| **Pure copper** | V | Electrolytic refining | high purity | Low-loss cable; **unlocks alternators (AC)** |
| **Bronze types** | IIIa | Ladle: copper + tin / zinc / bismuth / gold+silver | - | Tin bronze, brass, bismuth bronze, black bronze |
| **HSS** | V | Arc furnace + tungsten + chromium | hot-hard | **Best tools; needs no tempering** |
| **Waste alloy** | any | Off-spec ladle mix | n/a | Crush -> re-melt (Fe: **cupola** / **converter** (capped) / **arc**; Cu: **reverb** / **arc**; never the blast furnace) -> recover base Fe or Cu |

**Timeline (all correctly ordered):** crucible steel (Huntsman 1740s) / Bessemer (1856) / Hadfield
steel (~1882) / reverberatory copper + Pierce-Smith converting + electrolytic refining (1860s-70s) /
electric arc furnace + HSS (Taylor-White, ~1900). Cowper regenerative hot-blast stoves belong only
with the hot-blast furnace (Stage III), never cold blast.

**Material-gated power tiers (key design lever).** The two steam-power tiers are separated **by
construction material**, not merely by recipe: **LP machinery** (Stage II) is built from **cast
iron**; **HP machinery** (Stage IV Lancashire boilers, Cornish/Corliss cylinders) can **only** be
built from **hadfield steel**.

**Hadfield is the alloying-mechanic introduction.** It is the first metal that *must* be made by
mixing a base metal (mild steel) with an alloying element (manganese) in the **ladle** (Sec 7), teaching
the ladle-alloying system that later gates HSS and the bronzes. It also **gates HP machinery by
material** - exactly mirroring how cast iron gates the LP tier - so the player must finish the Stage
III steel work before high-pressure power. It is deliberately **not** a tool/weapon material.

### 4.1 Proposed baseline unit economy (tunable)

| Item | Mass (u) | Item | Mass (u) |
|---|---|---|---|
| Ingot (vanilla) | 100 | Sand "pig" | 200 |
| Small billet | 800 | **Large billet** | **2400** |
| Heavy plate | 400 | Plate (vanilla) | 200 |
| Sheet metal | 100 | Strip | 50 |
| Heavy bar | 300 | Rod (vanilla) | 100 |
| Thin rod | 50 | Wire rod | 25 |
| Large pipe | 300 | Rolled pipe | 150 |
| Small tube | 75 | Nail (stamped) | ~3 (batched) |

### 4.2 Alloy compositions (target ratios)

Every alloy carries a **composition** (mass fractions). **Carbon is set by *process*** - the blast
furnace carburises, the Bessemer blows carbon out, cementation adds it back - while the metallic
alloying elements (Mn, W, Cr, Sn, Zn) are added **by held proportion in the ladle** (Sec 7). Targets are
tunable:

| Material | Fe / Cu base | Carbon | Other (target) | Character |
|---|---|---|---|---|
| **Pig iron** | ~96 % Fe | ~4.0 % C | Si/Mn/P/S (slag flavour) | very high C, brittle |
| **Cast iron** | ~97 % Fe | ~3.0 % C | - | castable, brittle |
| **Wrought iron** | ~99.9 % Fe | <0.1 % C | fibrous slag stringers | = vanilla iron; **puddling-only** |
| **Blister steel** | ~99 % Fe | ~1.0 % C | (surface-carburised) | crucible feedstock only |
| **Crucible steel** | ~98.8 % Fe | ~1.2 % C | - | homogeneous high-C tool steel |
| **Mild steel** | ~99.8 % Fe | ~0.2 % C | - | = vanilla steel |
| **Ingot iron** | ~100 % Fe | ~0 % C | - (slag-free) | over-blow / arc-smelted; **not** wrought iron |
| **Hadfield steel** | ~86.3 % Fe | ~1.2 % C | **~12.5 % Mn** | austenitic, work-hardening |
| **HSS** | ~77.25 % Fe | ~0.75 % C | **~18 % W, ~4 % Cr** | hot-hard; W-Cr (no vanadium) |
| **Tin bronze** | ~88 % Cu | - | ~12 % Sn | bearings, fittings, cocks |
| **Brass** | ~70 % Cu | - | ~30 % Zn | gauges; **needs a coke cover or the Zn boils off** |
| **Bismuth bronze** | ~60 % Cu | - | ~25 % Zn + ~15 % Bi *(vanilla range)* | tool/deco |
| **Black bronze** | ~84 % Cu | - | ~8 % Au + ~8 % Ag *(vanilla range)* | tool/deco |
| **Converter copper** | ~98 % Cu | - | impurities | impure rod/wire stock |
| **Pure copper** | ~99.95 % Cu | - | - | electrolytic; low-resistance wire |
| **Waste alloy** | base Fe **or** Cu | - | off-spec / slag-rich | crush -> recycle |

> Bismuth-bronze and black-bronze fractions follow the **vanilla `metalalloy` ranges** - verify
> against the installed game before pinning exact numbers.

**Ladle mixing (mass-conserving).** Pour the base metal and add each element in its listed fraction.
Worked examples for an 800 u small billet:

- **Hadfield:** ~700 u mild steel + ~100 u manganese (12.5 %).
- **HSS:** ~624 u carbon-steel base + ~144 u tungsten + ~32 u chromium.
- **Tin bronze:** ~704 u copper + ~96 u tin.

**Off-spec -> waste alloy.** Held proportions outside the valid window (wrong ratio, missing element,
boiled-off zinc) do **not** snap to the nearest alloy - they yield a **waste alloy** that keeps the
full base-metal mass (Fe or Cu) but is otherwise useless. Crush it and re-melt to recover the base metal:
**iron** waste in the **cupola** (-> cast iron), as **capped cold scrap in the Bessemer converter** on a
live heat (-> mild steel, Stage III), or in the **arc furnace** (-> ingot iron, Stage V); **copper**
waste in the **reverberatory** or the **arc furnace**. (The **blast furnace never takes scrap** at any
tier - it is an ore-reduction primary smelter; remelting scrap is the cupola/converter/arc's job.) A
botched mix costs time and the alloying additions, never the underlying iron or copper.

**Carbon is adjustable both ways.** A converter blow *removes* carbon (Stage III); the player *adds*
it back by **hand-dropping powdered coke into the molten metal in the ladle** (Sec 7). Each unit of
powdered coke adds a fixed *mass* of carbon, so its effect on % C is relative to the iron volume
present. This is how you hit crucible/Hadfield/HSS carbon targets after a clean blow - and how you
carburise the arc furnace's ingot iron (Stage V).

**Wrought iron is puddling-only.** Its toughness comes from slag *fibres* worked into a pasty ball and
elongated by shingling - a fully molten route (Bessemer, arc) can't reproduce that, so an over-blown
or arc-smelted melt yields **ingot iron** (slag-free, ~0 % C), **never** wrought iron. Ingot iron is
recarburised (ladle) or re-melted, not a shortcut.

---

## 5. Networks

Four transport-network families (the electrical one splits into AC + DC). All reuse the existing
block-network graph infrastructure.

### 5.1 Molten-canal network *(live)*
Per-cell metal, flows cell->cell, end caps recomputed on tesselation. The **ladle** (Sec 7) is the only
merge/mix point. Feeds molds and converter/furnace runners. Carries pig iron, cast iron,
mild/hadfield steel, copper, bronze, HSS.

### 5.2 Pipe network - gas *or* water *(live)*
Single medium per network, unified state pool. Used for **water** (intake->boiler->reservoir), **steam**
(boiler->engine->condensate return), **compressed air** (blower->furnace tuyeres), **exhaust**
(Stage IIa heating) and **coal gas** (coke oven -> gasholder -> gas lamps, the Stage II lighting tier).
Pressure + one network-wide temperature. Connectors read the adjacent cell;
valves sever/flow; pressure valves overflow.

### 5.3 Mechanical-power (MP) network *(live)*
Constant-power generator model: `speed = power_budget / total_load`; a machine stalls past ~2x its
rated load and stops past that *(live)*. Sources: waterwheel (Stage I), engine flywheel sub-machines
(Stage II+). Loads: helve hammers, rolling mill, steam hammer lift, boring machine, wire extruder, ore
crusher.

**Engine power ratings (kW, shown in block-info).** One **helve hammer** draws **~1 kW** at nominal
speed (~ 0.125 MP-load units -> display rule **1 load unit ~ 8 kW**):

| Engine | Rated power | Nominal load |
|---|---|---|
| Watt engine (LP) | **~4 kW** | 4 helve hammers at rated speed |
| Cornish engine (HP) | **~4 kW** nominal, throttleable higher | 4 helve hammers; more MP per litre than Watt |
| Tandem Corliss (HP+LP) | **~36 kW** | 36 helve hammers without slowing - the line/generator prime mover |

These are constant-power *budgets*; load past ~2x the rating still stalls the engine.

**Steam couples in as the budget (reconciliation with Sec 6).** The kW ratings above are the *nominal*
budgets at rated steam. Under the responsive steam chain (Sec 6), the **boiler's delivered pressure sets
the engine's actual power budget** each tick, and this constant-power model then distributes that budget
across load. So an underfed boiler simply **lowers the budget** (weaker engine, slower sub-machines)
rather than contradicting the `speed = budget / load` math - the two models are one chain:
**pressure -> budget -> speed/load**.

**Governor model - Corliss only.** The **Tandem Corliss** is the *only* engine with a governor
(centrifugal governor + rotating cut-off valves); its **steam draw scales with delivered power** -
least at idle, most at full power:

- **No load -> max speed, minimum steam** (governor cuts steam off early).
- **Rising load -> slows, torque rises, steam draw climbs** (longer valve cut-off). Power = torque x
  speed.
- **Max power -> slowest spin, maximum steam.**
- **Demand beyond max power -> the engine stalls and stops**, killing everything downstream - no soft
  brownout.

Its governed budget feeds **both the MP network and a generator at once**, so an electrical over-draw
can stall the Corliss and drop its MP line too.

**Watt and Cornish have no governor** - they draw a *constant* steam rate regardless of load (the
Cornish's "throttle" is a **manual** level with fixed draw per setting). The constant-power
speed-vs-load curve and overload stall still apply; only the steam-follows-load efficiency is the
Corliss's advantage.

### 5.4 Electrical networks - AC transmission + DC consumption *(new, Stage V)*

Electricity is **two coupled sub-networks**: transmit on one, consume on the other.

- **AC backbone (transmission).** The **alternator** (pure-copper Corliss flywheel variant) produces
  **AC at fixed voltage and a frequency set by engine speed**. **Transformers** step voltage up for
  transmission and down for delivery. The AC network **ignores wire resistance** (it is high-voltage /
  low-current), so it carries power cheaply over distance - but **most consumer blocks can't use AC
  directly**.
- **DC drops (consumption).** Away from the generator, **AC -> DC** is done by a **rectifier** block at
  the point of use. The **DC network sums total resistance**: every wire and block adds resistance,
  voltage sags by `I x R_total`, and once it falls below a consumer's rated voltage that consumer
  underpowers or cuts out. This **caps how large a single DC network can grow** - the limiter on
  electrical sprawl.
- **Wire purity & length drive resistance.** Resistance = `f(purity, length)`: impure (converter)
  copper is high-resistance, **pure** (electrolytic) copper low, and resistance stacks with run
  length. Purity barely matters on the high-voltage AC backbone; its payoff is on the DC drops and on
  how hot a cable runs under load.

**Generators - both Corliss flywheel sub-machine variants (Stage V).** Place the large flywheel,
then **upgrade it in place** into one of:

- **Dynamo (DC)** - integral commutator -> outputs **DC** directly. Buildable from **impure or pure
  copper**; the **early-Stage-V** generator that bootstraps the first electrolysis cell with no
  transformer or rectifier.
- **Alternator (AC)** - slip-ring generator -> outputs **AC** (freq prop. to engine speed). **Gated behind
  pure copper** (its windings need it), so you run dynamos + electrolysis *first* to refine the pure
  copper that unlocks it. AC enables transformer step-up, long-distance transmission, synchronising,
  and the three-phase arc furnace.

Both convert the Corliss's mechanical kW -> electrical kW at an efficiency set by **coil-wire purity** -
impure ~**20 %**, pure ~**80 %**. Engine kW and generator output kW show in block-info.

**Design intent:** run **AC for long-distance transfer** (transformers + loss-free backbone), then
drop to **DC for end consumers** (electrolysis cells, batteries), with resistance keeping each DC
cluster local. **Light bulbs hang off AC directly, and the arc furnace runs on three-phase AC** (the
heavy-power exception).

**Circuit model.** The graph is solved as an electrical circuit:

- **Radial tree with implicit return.** Cable is a **doubled conductor** (out + return in one block),
  so each segment's resistance is already the round-trip value and the network is a tree - no loops, so
  a node's voltage is a single walk from the source.
- **Generators are sources** held at a fixed positive potential; **machines/outlets/batteries are
  sinks** pulled toward the return potential.
- **Voltage at a node = source potential - accumulated `I*R` drop** along the path. Further or more
  heavily loaded -> more sag.
- **Sinks are constant-power loads:** a sink draws `I = P_rated / V`, so as its node voltage sags the
  current rises - more `I^2*R` loss, more sag - until voltage falls below a **minimum-voltage cutoff**
  and it **cuts out**. Brownout behaviour *emerges* from this rather than being asserted.
- **Resistance becomes heat (`I^2*R`), accumulated over time - that's what melts cable.** Brief surges
  survive; **sustained over-current drives temperature up until the cable melts** and breaks the
  circuit. So an **impure cable can't sustain a pure-copper generator's full ~28.8 kW** or feed several
  electrolysis cells - those runs need **heavy cable** (and pure copper) - and a **long impure run
  bleeds a large fraction of the power as heat** before it reaches the load.
- **Source ceiling = the engine, not the wire.** The generator supplies whatever current the tree
  draws, up to the driving Corliss's max power (Sec 5.3). Demand past that **stalls the engine and stops**,
  dropping the whole network (and any shared MP load) at once.

**Transformer (why AC vs DC).** A transformer trades **voltage for current at constant power**
(`V1*I1 ~ V2*I2`). Because `I*R` loss scales with **current**, stepping voltage **up / current down**
before a long run slashes transmission loss; a second transformer steps it **down** to the consumer's
rated voltage. **Only AC can be transformed** - the entire reason the backbone is **AC** (high-V,
low-I, low-loss) while end consumers run **DC** (rectified locally, kept short and resistance-bounded).

**AC frequency & phase (what physically separates AC from DC):**

- **Frequency tracks the alternator's (Corliss's) speed.** Under the governor (Sec 5.3) the engine slows
  as load rises, so **frequency sags with load** and reaches zero at stall. (Voltage is held ~fixed by
  field regulation - the abstraction; frequency is the physical signal.)
- **Paralleling alternators requires synchronisation.** Two or more alternators on one network need
  matching **frequency *and* phase** - a dedicated **synchroniser block** locks them and shares load.
  After sync the network is coherent (one frequency/phase), so this concern is **localised to the
  generation side**.
- **Rectifiers are frequency-gated.** A rectifier only converts within a frequency band, so a bogged
  Corliss whose frequency sags below the band **drops its DC side out** - a distinct failure mode, and
  another way an over-loaded engine starves consumers *before* a full mechanical stall.
- **Three-phase lines (the heavy-power tier).** Run three phase-offset AC lines (a three-phase
  alternator, or three synchronised single-phase ones 120 deg apart). Two historical payoffs: the **arc
  furnace runs on three-phase AC *directly*** (one electrode per phase - Heroult, ~1900), no rectifier;
  and **big DC loads** (large electrolysis banks) feed a **three-phase rectifier -> one smooth,
  high-power DC** bus. Splitting the load across three conductors is what makes that power transportable
  - a single line would melt under the current.

**Conductors & hardware:**

- **Cable** - a thin block on a block face (surface-run). Normal capacity; **melts under high current**.
- **Heavy cable** - high current capacity; **required at the generator and at high-power machines**
  (arc furnace, big motors).
- **Inset cable** - embedded inside a block (hidden wall/floor wiring).
- **Inset outlet** - an inset tap for **lighting** and other low-draw loads.
- **Power pole** - a tall megablock for long runs. Click pole->pole to span a visual cable line
  (beam-style placement). **2 normal-cable slots + 1 heavy-cable slot**; sneak to pick the slot.

---

## 6. Stages - detailed process flows

Each process is **input -> output** with machine, rate and setup. Footprints and build costs are in the
machine cards (Sec 8).

### Dynamic heat balance (how furnaces reach temperature)

Furnaces have **no hardcoded "max temp"**. Each runs a per-tick **heat balance**:

```
T_process = T_in - T_loss        ... melts / refines only while  T_process >= T_threshold(material)
```

- **T_in** - the heat source: coke combustion (**coke ratio x air flow**) + a **blast-preheat buff**
  (cowper/regenerator); OR **electrical power** (arc furnace); OR **autothermal oxidation** (converter -
  the pig's own C/Si burned by the blow); OR a **fuel flame + regenerator** (open hearth, reverberatory).
- **T_loss** - the heat sinks: **cold-charge mass** (scrap, ore, wet feed) that must be melted, plus
  **radiation/ambient** losses (worse in winter).

Block-info always shows **current T, the threshold, and the contributors** (coke, blast temp, scrap sink),
so a stall reads "1410 C, needs 1538 C - add coke or hot blast", never a silent failure. Two fairness
rules: the model gates **efficiency, not possibility** (there is always one guaranteed path - high-coke +
cold blast melts at Stage I), and every threshold is **config-tunable**. This makes previously-hardcoded
behaviour **emergent**:

- **Blast furnace & cupola.** `T = base(coke%) + blast_buff(air_temp) - scrap_sink`. A **high-coke** charge
  burns hot enough that **cold blast** clears the melt line (wasteful, but works at Stage I); a **low-coke**
  charge burns cooler and needs **hot blast** to clear it. So the cold-blast/hot-blast "tiers" stop being
  hardcoded - they fall out of whether you have **charged cowpers** - and the **melt rate scales with the
  temp margin** (more margin -> faster; the Stage III u/s figures are the nominal full-margin rates). An
  under-blown furnace (blower underdelivering) runs cooler. The cupola is the same balance for remelt, with
  **cold scrap lowering T**.
- **Cowper regenerator state -> the blast buff.** The cowper's checker bricks **store heat from furnace
  exhaust**; the blast temperature it delivers depends on **how charged they are** (the alternating cycle -
  one bank heats off exhaust while the other blows). A cold cowper gives little buff, so the rig must **warm
  up**, and the cowper becomes a real heat reservoir rather than a flat tier bonus.
- **Converter (autothermal - scrap lowers temp).** `T = autothermal(pig C/Si, blow rate) - scrap_cooling`
  replaces the old flat ~1800 C cap. More cold scrap -> lower T and a longer blow; past ~20-30 % the bath
  **freezes** below the refining line - so the scrap cap is *emergent*, matching the blow-time-scales-with-
  scrap rule (Stage III). Low-C pig gives a weaker, cooler blow.
- **Open hearth (regenerator beats the heat sink).** `T = fuel_flame + regenerator_preheat - scrap_sink`.
  The regenerators feed a **sustained** flame that melts a **far larger scrap charge** than the autothermal
  converter can - so "the open hearth is the bulk scrap melter" is physics, not an assertion.
- **Arc furnace (coupled to the circuit).** `T_arc = f(delivered electrical power) - charge_sink`. Tied to
  the Stage V circuit (Sec 5.4): **voltage sag or frequency droop -> less power -> cooler arc -> it won't
  melt**. An under-supplied arc furnace fails the heat line - electricity and metallurgy become one system.
- **Responsive steam chain.** Boiler **pressure = f(fuel burn, feedwater temp) - steam draw**; **engine
  power scales with the pressure actually delivered** (not just its rating); **blower flow scales with
  engine power** and feeds furnace combustion. So an **underfed boiler -> weak blower -> cold furnace**: one
  loop, *fuel -> boiler -> engine -> blast -> melt*. Hot condensate return raises boiler efficiency.
- **Ambient / season.** Cold weather raises `T_loss` - furnaces want more coke or hotter blast, and
  **molten metal cools faster** (insulated/covered canals slow it). Config-gated, so it is immersion not
  punishment.

Three chains emerge: **heat** (fuel -> boiler -> engine -> blower -> furnace + cowper buff), **electric**
(engine -> alternator -> circuit sag -> arc), and the **regenerator feedback** (exhaust -> checker bricks
-> hotter blast/flame -> hotter exhaust - which must be warmed up first).

### Stage I - Mass iron production (IMEX, early 19th c.)
*Rapid iron parts + large cast components, cheap to set up, no steel required. Waterwheel MP only.*

| Process | Machine | Input -> Output |
|---|---|---|
| Make coke | **Coke oven** (bulk) | coal -> **coke** in bulk - the mod fuel; far faster than the tiny vanilla oven |
| Mix charge | Ore mixer | crushed iron ore + coke + limestone flux (high-coke) -> **blast mix** |
| Smelt | **Cold-blast furnace** | blast mix + cold air (MP blower) + refractory build -> **molten pig iron** (continuous, *low throughput / high coke* - cold blast can't sustain ~1550 degC) |
| Cast pigs | **Sand-mold block** | molten pig iron -> solid **pigs (200 u each)** - the shared intermediate feeding **both** cupola and puddling |
| Remelt -> cast iron | **Cupola furnace** | pigs **(+ iron/steel scrap, foundry returns, crushed iron waste)** + coke + cold air -> **molten cast iron** -> molds -> **cast-iron plates / rods / components** (the iron **remelter/recycler**, vs the ore-only blast furnace) |
| Refine -> wrought | **Puddling furnace** | pigs on an **iron-oxide bed** + heat + **rabbling** (manual, Sec 10) -> **wrought-iron balls** (100 u each) |
| Shingle | Helve hammer (MP) | wrought-iron ball -> **wrought-iron ingot / plate** (= vanilla iron) |
| Bore / finish | **Boring machine** + drill bit | rough cast/forged part + **drill bit** -> precision bore/turn/thread (cylinders, bearings, threads); bit material scales - see below |

Setup: waterwheel -> wooden-gear transmission -> MP blowers feeding furnace + cupola tuyeres. The
cold-blast furnace is **iron-age tier** (tier-1/2 refractory) - deliberately limited so the full
mass-production rig waits for hot blast (Stage III). The **ore mixer** stores output in an attached
**RCC bunker** whose front opening accepts a vanilla **chute / Archimedes screw**, so the blast mix can
be **auto-fed** into the furnace; the **cupola** is a small **1-tuyere** furnace loaded by hand through
a **lid**.

**Dense blast mix.** The ore-mixer recipe yields **1 blast mix** (16x denser), so a full blast-mix pile
holds **~900 u** of iron charge. The furnace consumes one whole pile per melt at a **~30 s** interval.
Pig output is mass-conserving:

```
pig_iron (u/s) = pile_charge (u) / melt_interval (s)
```

- **Cold-blast furnace:** 900 u / 30 s = **~30 u/s**.

**Physical design.** The cold-blast furnace has **no exhaust outlets** - it does not recycle its gases;
the **open top of the stack is the chimney**. The charge loads through a **pair of hoppers on the top,
set slightly off-centre** toward the **slag-tap side**. It draws cold blast through its **tuyeres**
(24 L/s each). **A player who falls into a working furnace dies almost instantly.**

**Coke oven (mass fuel).** The vanilla coke oven is tiny (~1 coalpile per 12 h), so the mod adds a
**bulk coke oven** (a larger oven/battery) that cokes coal in volume - a working iron line shouldn't be
fronted by a wall of vanilla ovens. Output is plain coke (smoke cosmetic); it is the single fuel behind
blast mix, cupola, crucible, recarburising and the brass coke-cover. The oven also gives off **coal gas**,
the feed for the Stage II gas-lighting tier.

**Bulk lime & cement.** Vanilla quicklime means slow firepit firing. The mod adds a **continuous lime
kiln** - a coke-fired vertical shaft (limestone + coke in the top, quicklime out the bottom, like a
miniature blast furnace on the same heat balance, Sec 6) for bulk **quicklime** (mortar, flux). It
optionally pairs with **slag**: blast/hot-furnace slag (cosmetic by default - no forced handling) can be
**tapped and ground in the steam crusher**, then mixed with lime -> **cement / concrete**, a bulk
building-material payoff (Sec 12). Slag stays opt-in, never a per-heat chore.

**Puddling furnace (detailed).** A manual heat station (Sec 10) that decarburises pig into wrought iron on
an **iron-oxide bed** - emphatically **crushed iron ore (hematite), not the blast mix**: puddling
*removes* carbon (the pig-boil), so the bed must be pure oxide; the blast mix's coke/flux would put
carbon back. Operation, all in-world:

1. **Sneak + RMB crushed iron ore** onto each hearth cell -> forms an **iron-oxide bed block**. The
   hearth is **3 blocks wide**, so it holds **up to 3 oxide beds**.
2. **Place pigs** into the beds - **each bed holds up to 2 pigs** (renders up to 2 pig meshes on its
   oxide surface), so a full 3-bed hearth takes **6 pigs / 1200 u**.
3. **Fire it and work the chimney lid** to regulate temperature: **fully open** to melt the pigs and
   oxide, then **half-closed** to drop into the ball-forming window.
4. **Rabble at the hatch** with the **rabbling bar** (repeated RMB) to ball up the pasty iron.
5. **Open the hatch and pull the white-hot balls out one by one** with the same tool -> each **100 u** ball
   goes to the **helve hammer** (or steam hammer, once available) for shingling into wrought-iron bars.
   A 1200 u charge (6 x 200 u pigs) yields **12 x 100 u balls** - mass-conserving; the lost ~4 % C and
   the slag are cosmetic.

**Boring machine + scaling drill bits.** The **boring machine** is the precision metal-removal station -
**bore** (cylinder sleeves, bearings/bushings, hubs, pump/valve bodies, cock seats), **turn**
(shafts/axles, journals, mill rollers), **screw-cut** (bolts, staybolts, threads). It is **MP
(waterwheel) from Stage I** - historically the water-powered boring mill (Wilkinson, 1774) is what made
the steam-engine cylinder seal well enough to work, so it *predates and enables* the engines, and it
also finishes Stage I's own cast components. Its cutting edge is a **replaceable drill bit** (installed
sneak + RMB, wears like rollers/dies) whose **material gates the hardest metal it can machine**:

| Drill bit | Made from | Machines up to | Wear | From |
|---|---|---|---|---|
| **Cast-iron bit** | cast iron | cast iron, wrought iron (LP cylinders, bearings) | fast | I |
| **Hardened-steel bit** | mild steel forged then **quench-hardened** (vanilla quench mechanic; crucible steel = longer-lived option) | + mild steel, **hadfield cylinders** | medium | III |
| **HSS bit** | HSS | + HSS, all metals; near-permanent | minimal | V |

The harder bits are made with the game's **quenching** mechanic - forge the bit soft, then quench it to
hardness - so no new hardening system is needed. Hadfield cylinders (Stage IV HP engines) need only the
**hardened-steel bit** (Stage III, on-spine) - no backward gate to HSS - while the **HSS bit** is the
endgame buy-once that is *required* to machine HSS itself. (The helve hammer is the basic *forging*
shaper; the boring machine does the *finishing* cuts; only the **steam hammer** works billets, Sec 9.)

### Stage Ia - Crucible steel (IMEX add-on)
*Replaces the vanilla "hammer blister steel into steel" mechanic with crucibles.*

| Process | Machine | Input -> Output |
|---|---|---|
| Carburise | Cementation furnace *(vanilla)* | wrought-iron bars + charcoal -> **blister steel** |
| Fire crucible | Beehive kiln *(vanilla)* | claymolded crucible -> **fired clay crucible** |
| Melt | **Draft crucible furnace** | blister-steel ingot(s) + coke + fired crucible -> **molten crucible steel** |
| Cast | Special cast-iron mold | molten crucible steel -> **cast crucible-steel ingot** |
| Work | Helve hammer (MP) | cast ingot -> **workable crucible-steel ingot / plate** -> tools/weapons (+durability +damage) |

Crucible steel is **not** vanilla steel; it is a separate high-carbon tool alloy.

### Stage II - Low steam power (PPEX, early-mid 19th c.)
*Free the rig from rivers/seasons; the bridge that unlocks hot blast. All machines built from
**cast-iron** components - no steel needed.*

| Process | Machine | Input -> Output |
|---|---|---|
| Raise steam | **Cornish boiler** | water (pipe) + fuel -> **LP steam** + heat; shared tank, ~3-min heat-up *(live)* |
| Steam -> power | **Watt engine** | LP steam (**fixed draw** 30 L/s, no governor) -> engine power; **condensate** returned/spilled if unpiped *(live)* |
| Pump water | Fluid pump (sub-machine) | engine + intake -> **water @ 16.67 L/s** *(live)* |
| Blow air | Air blower (sub-machine) | engine -> **compressed air @ 48 L/s** *(live)* |
| Transmit power | Flywheel (sub-machine) | engine -> **MP network** (hammers/mills) |
| Store water | **Fluid tank** | pump fills it -> **bulk water buffer** on the pipe net |
| Water crops | **Mechanical sprinkler** | tank/pipe water -> waters soil in a **3-block radius below** to 100 % moisture then **stops**; **wrench-set interval** before the next top-up; **floor- or ceiling-mounted** |

Sub-machine outputs scale off **absolute** engine power *(live)*; `steam-engine-efficiency` (0.7) sets
pump/blower output pressure *(live)*. Setup: boiler -> engine -> one sub-machine per engine, connected by
pipes (steam in, water/air out) and MP for the flywheel.

**Buffered water & timed sprinklers (player-requested).** A **fluid tank** stores a large volume of
pumped water so the **mechanical sprinkler** network (floor- or ceiling-mounted) runs off the buffer.
Sprinklers do **not** spray continuously (that would drain the tank and waste water) - they water on a
**timed cycle**:

- **Saturate then stop:** a firing sprinkler waters the soil blocks in a **3-block radius beneath** it
  until each reaches **100 % moisture**, then **halts** - it consumes only the moisture deficit, so a
  wet field costs nothing.
- **Wrench-set interval:** after stopping it starts a **countdown** (interval cycled with the wrench, the
  mod's standard in-world config); when it fires the cycle repeats. Short interval = greener soil, more
  water; long interval = more frugal, but risk the soil drying below the crop threshold between cycles.

Because watering stops at saturation and only resumes on the timer, the **pump need only top the tank up
every few in-game days** instead of running continuously. Pairs naturally with the Stage IIa greenhouse
for hands-off winter farming. (The interval logic is built into the sprinkler; a separate reusable
**mechanical timer** block to pulse a whole zone is an optional extra, not required.)

**Gas lighting (coal gas - the early lighting tier).** Long before electricity, coal distillation gives
off **coal gas**. Two sources, same chemistry: the **coke oven** (Stage I) vents it as a coking
byproduct, or a dedicated **gas retort / gasworks** runs the distillation **gas-primary** (coke becomes
*its* byproduct) - the inverse of the coke oven, exactly as 19th-c. town gasworks worked. (Equivalently
the coke oven could carry a coke-vs-gas yield toggle; a distinct gasworks is just the iconic build.) Piped,
stored and burned, coal gas is the **Stage II lighting tier** - a clean parallel of the Stage V electric
system, one age earlier:

| Gas tier | Electric analog (Stage V) | Role |
|---|---|---|
| Coke oven / gasworks (coal gas) | generator | source |
| **Gasholder** (low-pressure tank) | battery | buffer/backup - lamps stay lit when the source idles |
| **Pipe -> small gas pipe** (face-run) | cable | distribution main |
| **Gas lamp** | light bulb | the fixture |

- **Storage = a low-pressure gasholder**, not a pressure tank: town gas was held in near-atmospheric
  **gasometers** (the telescoping bell *is* the pressure). It is the gas analog of the water fluid tank;
  its I/O is **normal (full) pipe**.
- **Distribution.** The bulk source->holder->building run uses **normal full pipes** (the Stage I plate-grid
  recipe, or rolled later - Sec 11); **small gas pipes** (face-run like cables, gated throughput) do the
  tidy indoor lamp distribution but are a **rolling-mill product** (Stage III, Sec 9/11), so early lighting
  runs full pipe and upgrades to compact small pipe later. A **pipe reducer** block bridges the gasholder's
  full-pipe I/O to the small face pipes. A **portable gas tank** for an isolated lamp is an optional
  convenience - gas lighting is piped.
- Coal gas is just another **gas medium** on the pipe network (Sec 5.2). Two lighting tiers result: **gas
  (Stage II)** -> **electric (Stage V)**, and the **battery backs up electric lamps exactly as the
  gasholder backs up gas lamps**.

### Stage IIa - Advanced heating (PPEX add-on - separate opt-in mod)
*Intentionally punishing: build heating before first winter. Payoff: heat a greenhouse all winter.*

| Path | Chain | Input -> Output |
|---|---|---|
| Exhaust | heat source -> passthrough wall -> chimney | fuel burning in firepit/coalpile/**brick stove** -> **exhaust** routed through **passthrough blocks** (heat adjacent rooms) -> vented at **chimney** |
| Steam | boiler -> radiator(s) | **heated steam** -> **small/large cast radiator** -> room heat -> **hot water** (condensate) returned; radiators **chainable** across rooms |

The steam path is more efficient but needs a boiler. Greenhouse kept above the cold threshold -> crops
continue through winter. **No config softening - installing the mod is the opt-in.**

### Stage III - Mass steel production (SMEX, mid 19th c.)
*Industrial steel. Needs Stage-II steam (air + power). Adds the billet pipeline.*

| Process | Machine | Input -> Output |
|---|---|---|
| Crush ore (mass) | **Steam ore crusher** (LP) | nuggets / stone -> **crushed ore** in bulk (LP: iron + softer ores + **rhodochrosite/Mn**); the mass alternative to the pulverizer (Sec 3) |
| Preheat blast | **Cowper stoves** (regenerative) | cold air (blower) + furnace **hot exhaust** (alternating cycle) -> **hot blast air**; spent exhaust -> **smoke stack** |
| Smelt (hot) | **Hot-blast furnace** | low-coke blast mix + **hot blast** -> **molten pig iron** at higher rate / lower coke |
| Convert | **Bessemer converter** | (optional **cold scrap charged first**, capped ~20-30 % of the heat) + molten pig + pressurized air -> **molten mild steel (high-N ~150 ppm)**; the hot metal melts the scrap so **the blow lengthens with the scrap charge**; the blow **continuously burns off carbon + slag** (over-blow -> ingot iron) |
| Alloy | **Tilting crucible furnace** + **Ladle** | tilting furnace smelts manganese (rhodochrosite) -> pours into a canal -> ladle mixes mild steel + ~12.5 % Mn -> **molten hadfield steel** |
| Merge/mix/pour | **Ladle** (multiblock) | converges canals; holds/mixes mild &/or hadfield -> pours **large-billet** (2400 u), **small-billet** (800 u) or **component** molds |
| Form | **Rolling mill(s)** | billet -> **profiled stock** (does not cut; Sec 9) |
| Cut/stamp | **Steam hammer** + die | profiled stock -> **discrete vanilla items**; or billet -> forged heavy components (Sec 9) |

New rolled/forged components unlocked here: heavy plate, heavy cap, rolled pipe (plus everything in
Sec 11). Only steam machinery can work the 800-2400 u billets.

**Throughput (baseline, tunable).** Charges and intervals scale to the 2400 u billets:

```
pig_iron (u/s)   = pile_charge (u)      / melt_interval (s)   # blast furnaces
mild_steel (u/s) = converter_charge (u) / blow_time (s)       # Bessemer
```

| Machine | Baseline | Note |
|---|---|---|
| Cold-blast furnace (I) | 900 u / 30 s = **~30 u/s** | dense-pile charge, 30 s melt. |
| Hot-blast furnace (III) | 900 u / 20 s = **~45 u/s** | hotter -> shorter melt + lower coke ratio. |
| Bessemer converter (III) | **>= 4800 u** / ~300 s = **~16 u/s** | holds **>= 2 large billets**; one blow = two large billets of mild steel. |
| Ladle (III) | holds **>= 4800 u** | buffers two large-billet pours. |

Rule of thumb: converter and ladle each hold **at least two large billets (4800 u)** so the steel line
never stalls. One hot-blast furnace (~45 u/s) fills a 4800 u Bessemer in ~107 s - comfortably inside one
~5-min blow - so a single furnace feeds one converter and stockpiles the surplus in molten canals.

**Carbon & slag control (simulated).** Conversion is *continuous*, and carbon runs both directions:

- The **Bessemer blow steadily burns off carbon and slag** - longer blow -> lower both. Tap for **mild
  steel (~0.2 % C)**; **over-blow** drives carbon toward zero -> **ingot iron** (soft, slag-free). Block-
  info shows live **C %** and slag level. **Ingot iron is a botched heat, not a shortcut to wrought
  iron** (puddling-only, Sec 4.2) - recarburise it or re-melt it.
- **Recarburising in the ladle:** hand-drop **powdered coke** to raise carbon (each unit a fixed mass
  of C, relative to the iron volume). How you hit crucible/Hadfield/HSS targets after a clean blow.
- **Brass needs a carbon cover.** When alloying **zinc**, the melt must be kept under a **coke cover**
  or the **zinc boils off** and drifts copper-rich -> off-spec -> waste (modelled as zinc loss-per-second
  on an uncovered hot zinc melt in the ladle).

**Tuyere & exhaust balance (air in = gas out):**

- **Each tuyere consumes 24 L/s.** A standard 2-tuyere furnace draws **48 L/s** - exactly one air
  blower (Stage II).
- The **hot-blast furnace** has **2 exhaust outlets @ 24 L/s** (48 L/s = blast intake), feeding the
  **cowper stoves** and venting via **smoke stack**.
- The **cold-blast furnace has no exhaust outlets** - vents out its open top (no cowpers, no stack).
- The **Stage IV large furnace** has **6 tuyeres (144 L/s in)** and **4 exhaust outlets @ 36 L/s
  (144 L/s out)**; needs **3 smoke stacks**, and the cowper throughput cap is removed so **2 cowpers**
  preheat its full blast (Stage IV).

### Stage IIIa - Mass copper production (SMEX add-on)

| Process | Machine | Input -> Output |
|---|---|---|
| Smelt matte | **Reverberatory furnace** | crushed copper ore + heat -> **copper matte** (+ slag); **coal burns in a separate part of the multiblock** - flame plays over the charge, fuel never mixes with it |
| Convert | **Pierce-Smith converter** | copper matte + pressurized air + MP -> **blister/molten copper** |
| Alloy (optional) | **Tilting crucible furnace** + **Ladle** | tilting furnace smelts tin / zinc / bismuth -> canal -> ladle mixes with molten copper -> **tin bronze / brass / bismuth bronze** (zinc alloys need a coke cover) |
| Cast | Ceramic molds (via ladle) | molten copper -> **copper rods** |

Supports **continuous casting** for late-tech throughput. **Wire drawing is deferred to Stage V** - the
wire extruder is useless until there's an electrical network to consume the wire. *(Stage V's arc
furnace can replace the reverberatory's smelting step - copper ore -> matte - but the converter is still
required; see Stage V.)*

**Sulfuric acid (for Stage V electrolysis & batteries).** Vanilla makes acid slowly by boiling in a
barrel (~6 L/pot) - fine for its current use, too slow to fill electrolysis banks. Two scaling levers,
both historical: (1) electrolysis **regenerates** its electrolyte (anode copper dissolves, cathode plates
out, the acid cycles), so a cell needs a **one-time fill + slow top-up**, not a constant feed - which
already makes a few cells affordable; (2) the bulk source is an **acid plant** (lead-chamber process) that
**captures the SO2 thrown off when copper sulfide ores are roasted/smelted here** (currently cosmetic
fume) and absorbs it into water -> sulfuric acid in volume, so acid becomes a **byproduct of the copper
line**, exactly as real smelters worked. (Simpler fallback: a steam-heated boiling vat scaling the vanilla
pot up.)

### Stage IV - High steam power (PPEX, mid-late 19th c.)
*Power whole production lines + the large blast furnace. Requires hadfield steel & rolled pipe.*

| Process | Machine | Input -> Output |
|---|---|---|
| Raise HP steam | **Lancashire boiler** | water + fuel -> **HP steam** (~8-12 atm); needs **hadfield plating + rolled pipe** |
| HP power | **Cornish engine** | HP steam -> MP; **throttleable**; more MP per litre than Watt; cylinder needs **hadfield steel** |
| Big pump/blast | **Large Cornish pumping engine** (~3wx6t) | HP steam -> **one large sub-machine**: heavy air blower @ ~160 L/s **or** heavy fluid pump @ ~36 L/s (the large furnace needs **both -> two large engines**). 6 tuyeres = 144 L/s, the blower runs ~16 L/s over to *build* pressure; the pump (~2.16x standard) feeds the boiler bank |
| Line power | **Tandem Corliss horizontal engine** | HP+LP steam (tandem-compound, cylinders inline) -> **heavy flywheel** -> large MP / generator drive |
| Crush hard ore | **Steam ore crusher** (HP) | HP jaws crush **chromite & wolframite** (LP can't) -> gates the **HSS** elements; also crushes **hardened steel scrap** for arc recycling (Sec 3, Stage V) |
| Low-N steel / recycle | **Open-hearth (Siemens-Martin) furnace** | pig + **bulk scrap** + flux + regenerative flame *over* the bath (no air-blast -> **low nitrogen** ~50 ppm) -> **low-N steel**; slow heat, **time scales with scrap charge**; the bulk pre-electric scrap->steel melter + low-N source for pressure parts & HSS feedstock |

> **Large blast furnace** (internal 5x5x8, **6 tuyeres**, **4 exhaust outlets**): depends on Stage-IV
> power. **Two large pumping engines** drive it (each engine = one large sub-machine): one a **heavy
> blower** (all 6 tuyeres, 6 x 24 = **144 L/s**, targeting **~160 L/s** to hold pressure), one a **heavy
> pump** (feedwater for the boilers). Its 4 exhaust outlets emit **36 L/s each (144 L/s out)**, needing **3 smoke
> stacks**; with the cowper cap removed, **2 cowper stoves** preheat the full 144 L/s blast. **Not a
> solo build** - sized for **server communities** to feed, fuel and operate *collectively*. Scale its
> pig output to ~3x a hot-blast furnace (~**135 u/s**, tunable).

**Steel nitrogen content (Bessemer vs open hearth).** It is all just **steel** (= vanilla steel); the
grade is a tracked **nitrogen attribute (ppm N)** shown in block-info / the handbook **alongside carbon**,
set by the *process* that made it. The Bessemer blows **air** through the melt, so its steel comes out
**high-nitrogen** (~150 ppm N, tunable) and is prone to strain-age embrittlement - fine for billets,
structural stock, rails and machine frames. The **open hearth** heats the bath with a regenerative flame
*over* it (no air blown through), so its steel is **low-nitrogen** (~40-60 ppm N); the **arc furnace**
(Stage V) and **crucible steel** run low-nitrogen too. Two hard gates **bar high-nitrogen steel** (above a
~80 ppm threshold, tunable): **pressure-critical parts** (boiler plate, heavy cap, staybolts) and the
**HSS feedstock** (the arc furnace won't make good HSS from high-N steel). For pressure parts the
acceptable tough materials are **wrought iron, low-nitrogen steel, or hadfield** - hadfield is gated by
being an *alloy*, not by nitrogen, so its base N doesn't matter. Beyond those gates the difference is
**light flavor**: high-N steel has a chance of **forge/roll crack-waste** when worked, slightly **lower
tool/armor durability**, and its block-info ppm reads as a brittleness tell (a subtle tint). The open
hearth is also the **bulk pre-electric scrap->steel melter** (the cupola only makes cast iron and the
converter's cold-scrap charge is heat-capped) and reuses the cowper **regenerative checker-brick**
principle. The 3-tier steel ladder: **Bessemer** (fast, cheap, high-N) -> **open hearth** (slow, low-N,
scrap-hungry) -> **arc furnace** (electric, premium, HSS).

### Stage V - Electric power (PPEX + SMEX add-on, late 19th c.)
*High-tech endgame - electricity, light, HSS, and the **electric arc furnace** as a universal melter.
Build last, independent.*

| Process | Machine | Input -> Output |
|---|---|---|
| Generate DC | **Dynamo** (Corliss flywheel upgrade) | flywheel -> dynamo: removes MP, **integral commutator -> DC** @ fixed V; Corliss ~36 kW x coil eff (impure ~20 % -> ~7.2 kW / pure ~80 % -> ~28.8 kW). Early bootstrap; powers electrolysis directly |
| Generate AC | **Alternator** (Corliss flywheel upgrade, **pure copper**) | flywheel -> alternator: removes MP, **slip rings -> AC** @ fixed V, **freq prop. to speed** (single- or three-phase). Pure-copper-gated; for transmission + three-phase arc furnace |
| Transform | **Transformer** | AC in -> AC at stepped voltage (up for transmission, down for delivery) |
| Rectify | **Rectifier** | remote **AC -> DC** (rotary converter / mercury-arc) at the point of consumption |
| Light | **Light bulb** | AC (or DC) -> light; low draw - one generator lights many |
| Store / backup | **Acid / dry-cell battery** | DC <-> stored DC; **keeps lamps & low loads lit when the generator is idle** - the electric analog of the gasholder for gas lamps |
| Refine copper | **Electrolysis cell** | impure copper plate (anode) + **DC** -> **pure copper plate** (cathode) |
| Draw wire | **Wire extruder** (MP) | copper rod/plate -> **copper wire** - impure for first dynamo coils, then **pure** (lowers resistance, extends DC reach, **unlocks alternators**) |
| Alloy / smelt / recycle | **Arc furnace** | **three-phase AC** (3 synced alternators) -> universal electric melter - see below |

HSS is the top tool alloy and **needs no tempering** (stays hard at heat).

**The arc furnace - universal electric melter.** Powered by **three-phase AC directly** (one electrode
per phase, no rectifier), it is the heaviest consumer and the real endgame gate. Given a power plant it
**bypasses the fuel/air-blast chain** (no coke fuel, cowpers, blowers) at a heavy electrical cost. Four
roles, all mass-conserving / no yield bonus:

| Role | Input -> Output | Notes |
|---|---|---|
| **HSS alloying** | **low-N** steel scrap/ingots (open-hearth/arc/crucible - **not high-N Bessemer**) + **W + Cr** + 3-phase AC -> **molten HSS** -> ingot molds | The original Stage-V tool alloy; high-N steel is too impure for reliable HSS. |
| **Scrap / recycle remelter** | crushed waste alloy / ingot iron / scrap + AC -> **molten base metal** | Coke-free; the late-game route for the Sec 4.2 recycle loop. |
| **Direct iron smelter** *(blast-furnace alternative)* | crushed iron ore (+ minor carbon reductant) + AC -> **molten ingot iron** (~0 % C) **+ slag** | Stassano-type electric smelting. Output is **pure low-carbon iron** - **recarburise in the ladle** (Sec 4.2) to reach a target steel. Replaces the blast furnace, not the carburising/alloying steps. |
| **Copper smelter** *(reverberatory alternative)* | crushed copper ore + AC -> **copper matte** (+ slag) | Replaces the **reverberatory furnace only**; the **Pierce-Smith converter -> blister -> electrolysis** chain is still required (Stage IIIa). |

> **Historical note.** Smelting iron ore and scrap in an electric arc furnace is authentic - the
> **Stassano furnace** (~1898-1901) smelted iron ore directly to steel, and **electric pig iron** was
> made commercially in early-1900s Scandinavia/the Alps where power was cheap and coke scarce. Electric
> **copper-matte** smelting is likewise real. The furnace replaces **smelting** (a heating step), never
> **converting** (matte -> blister is an air-blow oxidation step the arc furnace doesn't perform) - which
> is why the copper route keeps the Pierce-Smith converter.

**Generator sizing & the bootstrap loop.** Both generators are large-flywheel sub-machine variants only
the Tandem Corliss can drive (~36 kW each); output is `36 kW x coil efficiency`:

1. **Impure-copper dynamo (DC, ~20 % -> ~7.2 kW).** The kickstart: enough for **one electrolysis cell**
   (~7.2 kW), wired straight to DC - no transformer or rectifier. Its whole job is to refine the
   **first batch of pure copper**; it never scales.
2. **Pure copper unlocks the rest.** A **pure-copper dynamo (DC, ~80 % -> ~28.8 kW)** runs **4
   electrolysis cells**, and pure copper is what **lets you build alternators**.
3. **Alternators (AC, pure copper, ~28.8 kW each)** carry power long-distance and feed heavy loads; a
   pure-copper supply also lights a *lot* of bulbs - electricity's "civic" payoff.

- **The arc furnace runs on a three-phase AC line - three synced alternators (~86 kW) on three Corliss
  engines.** Three-phase isn't only about total power: splitting ~86 kW across three conductors is **the
  only way to carry that current** - a single line would melt even heavy cable. A genuine power-plant
  build (3 engines + 3 alternators + synchroniser), the real endgame gate.

> **Realism note.** A *dynamo* is a DC machine (integral commutator); an *alternator* is an AC machine
> (slip rings) - genuinely different builds. The Heroult arc furnace (~1900) **is** a three-phase AC
> machine, one electrode per phase. Electrolysis stays **DC** (rectified; large banks via a three-phase
> rectifier).

---

## 7. Ladle (central alloying multiblock)

The ladle is a **stationary multiblock**, not a hand tool. It:

- **Converges multiple molten-canal networks** into one vessel (e.g. a Bessemer mild-steel canal plus
  alloying-metal runners poured in by **tilting crucible furnaces**, Sec 8).
- **Mixes metals** by held proportion into the alloys of Sec 4.2: **Hadfield**, **HSS**, **tin bronze**,
  **brass**, **bismuth bronze**, **black bronze**.
- **Recarburises by hand:** dropping **powdered coke** raises carbon (each unit a fixed mass of C,
  relative to the iron volume) - how you hit crucible/Hadfield/HSS targets after a clean Bessemer blow,
  **and how you carburise the arc furnace's ingot iron** (Stage V).
- **Holds a carbon cover for zinc alloys:** brass/bismuth-bronze melts must be kept under a coke cover
  or the **zinc boils off** (Stage III).
- **Pours** the result into billet molds, component molds, or onward canals/runners.

**Off-spec mixes don't snap to the nearest alloy** - they become a **waste alloy** that keeps the base
Fe/Cu mass; crush it and re-feed the **cupola** (iron) / **reverberatory** (copper) / **arc** furnace to
recover the metal - never the blast furnace, which only smelts ore (Sec 4.2).

It is the only block allowed to merge/mix canals; everywhere else metal stays per-cell.

---

## 8. Machine reference cards

Footprint uses the Sec 2 vocabulary (block / megablock / multiblock). Power column: what drives it. Build =
key industrial components (full list in Sec 11). "Exists" = present in current ppex/smex; "Planned" = new.

| Machine | Stage | Footprint | Power | Build (key components) | Inputs -> Output | Status |
|---|---|---|---|---|---|---|
| Coke oven (bulk) | I | multiblock (oven/battery) | fuel | refractory brick | coal -> **coke** primary + **coal gas** byproduct (mass replacement for the tiny vanilla oven) | Planned |
| Gas retort / gasworks | I | multiblock (retort house) | fuel | refractory brick | coal -> **coal gas** primary + **coke** byproduct (the coke oven's inverse; the dedicated lighting-gas source) | Planned |
| Lime kiln | I | multiblock (vertical shaft) | fuel (coke) | refractory brick | limestone + coke -> **quicklime** in bulk (continuous shaft kiln, same heat balance, Sec 6); mortar/flux + cement (Sec 12) | Planned |
| Ore mixer | I | multiblock (megablock mixer + RCC bunker) | - | mixer body, ore bunker | ore + coke + flux -> blast mix in bunker; **bunker feeds the furnace via chute / Archimedes screw, or a skip hoist / bucket elevator to a tall furnace top** | Exists |
| Cold-blast furnace | I | multiblock, open-top stack | MP blower air (2 tuyeres @ 24 L/s) | refractory brick, cast pipe, grate | dense blast mix + cold air -> molten pig (~30 u/s nominal); **temp = coke% + blast buff** (cold blast needs high-coke mix, Sec 6); **no exhaust** (open top = chimney), off-centre hoppers by slag tap, **falling in = death** | Exists |
| Sand-mold block | I | block | - | sand | molten pig -> solid **pig (200 u)**; feeds cupola & puddling | Planned |
| Cupola furnace | I | multiblock (small, 1 tuyere) | MP blower air | refractory, grate, cast pipe | pigs **+ iron/steel scrap + crushed iron waste** + coke + air -> molten cast iron (the iron **recycler**); **manual load via lid** | Planned |
| Puddling furnace | I | multiblock (**3-wide hearth** = 3 oxide beds + chimney lid + hatch) | manual (lid throttle + rabbling) | refractory, furnace door, grate | 3 oxide beds (crushed ore) + up to 6 pigs + heat -> **wrought balls** (100 u; 6 pigs -> 12 balls); lid regulates temp, rabble + pull balls at the hatch (Stage I) | Planned |
| Helve hammer | I | block | MP | - (vanilla) | ball/ingot -> ingot/plate (forging/shingling; **ingot-scale only - cannot work billets**, Sec 9) | Vanilla |
| Boring machine | I | megablock | MP (waterwheel+) | bedplate, cutting head, **drill bit** (tooling) | **bore / turn / screw-cut** rough parts -> finished cylinders, bearings, shafts, threads - the finishing gate; **bit material gates hardness** (cast-iron -> quench-hardened steel -> HSS, Stage I) | Planned |
| Tilting crucible furnace | I/III | multiblock | fuel/air | refractory, large crucible, tilt mechanism | **bulk-smelt copper & other metals** (larger than a hand crucible) **and** alloying ore/metal (Mn, W, Cr, Sn, Zn...) -> **tilt-poured into a molten canal** | Planned |
| Cementation furnace | Ia | multiblock | fuel | refractory | wrought bars + charcoal -> blister | Vanilla |
| Draft crucible furnace | Ia | multiblock | fuel/air | refractory, fired crucible | blister steel + coke -> molten crucible steel | Planned |
| Cornish boiler | II | megablock + multiblock (RCC) | fuel + water | boiler plate (cast->steel), cap, injector | water + fuel -> LP steam; **pressure = fuel burn vs steam draw** (over-draw sags it -> weaker engine, Sec 6) | Exists |
| Watt engine | II | megablock + sub-machine | LP steam | bedplate, bored cylinder sleeve, conrod, flywheel, brass bearings | steam -> MP / pump / blower (**~4 kW = 4 helve hammers**) | Exists |
| Fluid pump | II | RCC megablock (sub-machine) | engine | cast pump body, valve body | engine -> water @16.67 L/s | Exists |
| Air blower | II | RCC megablock (sub-machine) | engine | cast fan, ducting | engine -> air @48 L/s | Exists |
| Fluid tank | II | megablock | - (storage) | cast/steel plate, valve body | pipe water -> **bulk buffer**; pump refills periodically, consumers draw continuously | Planned |
| Mechanical sprinkler | II | block (floor/ceiling) | - (pipe-fed) | brass nozzle, cast body | timed: waters soil in a **3-block radius below** to 100 % moisture then **stops**; **wrench-set interval** to next top-up (Stage II) | Planned |
| Gasholder | II | megablock (telescoping) | - (low-P store) | sheet iron/steel, guide frame | **coal-gas buffer** at near-atmospheric pressure; lamps stay lit when the source idles (gas analog of the fluid tank); **normal full-pipe I/O** | Planned |
| Pipe reducer | II | block | - (gas/fluid) | sheet / cast | **bridges normal full pipe <-> small face pipe** (throughput gated by the small side) - e.g. gasholder main -> lamp distribution | Planned |
| Small gas pipe | II/III | block (on a face) | - (gas conductor) | **rolling-mill small tube** (Sec 11) | face-run gas main for indoor lamp distribution (gated throughput, cable-style placement); **rolling-mill product, not plate-assembled** | Planned |
| Gas lamp | II | block | coal gas | brass, glass | gas -> light; low draw - one gasholder lights many; the pre-electric lighting fixture | Planned |
| Bucket elevator / skip hoist | II | megablock (tall / inclined) | MP | cast buckets or skip car, chain | powered **vertical lift** for bulk solids - **primary use: charge furnaces from ore-mixer bunkers**; skip-hoist variant = iconic inclined blast-furnace charger | Planned |
| Pneumatic tube | II | block (on a face) | air pressure | sheet, fittings | **abstracted** item transport: solids move at a gated throughput, **no visible item** (like fluid in a pipe) - long-range bulk | Planned |
| Radiator (sm/lg) | IIa | block / megablock | steam | cast radiator section | steam -> room heat + hot water | Planned |
| Passthrough wall / chimney | IIa | blocks | - | brick / sheet | exhaust -> room heat -> vent | Planned |
| Steam ore crusher | III / IV | megablock | LP / HP steam (MP-driven jaws) | bedplate, hardened (hadfield) jaws | mass nugget/stone -> crushed ore; **LP** = iron/soft ores + stone/lime + Mn, **HP** = hard ores (**chromite, wolframite**) + hardened scrap - a **hardness gate** + bulk pulverizer alternative (Sec 3) | Planned |
| Cowper stove | III | megablock | air + exhaust | refractory, checker brick | cold air + exhaust -> hot blast; **delivered blast temp = checker-brick charge** (alternating heat/blow cycle, Sec 6) | Exists |
| Hot-blast furnace | III | multiblock (existing furnace) | hot blast (2 tuyeres @ 24 L/s) | refractory, heavy plate | low-coke mix + hot blast -> molten pig (~45 u/s); **2 exhaust outlets @ 24 L/s** -> cowpers + smoke stack | Exists |
| Bessemer converter | III | multiblock | pressurized air + MP tilt | heavy plate, heavy cap, refractory lining | (cold scrap, capped) + molten pig + air -> molten mild steel (**high-N**); **scrap charged before the hot metal**, **blow time scales with scrap** (steel-line recycler, heat-balance limited) | Exists |
| Ladle | III/IIIa | multiblock | MP/manual tilt | heavy plate, refractory | converge canals + mix -> pour | Planned |
| Rolling mill | III | megablock | MP | bedplate, **rollers** (tooling), heavy gears | billet -> profiled stock | Planned |
| Steam hammer | III | megablock | steam (LP forge/shear, HP stamp) | bedplate, heavy cap, **dies** (tooling) | **billets & rolled stock** -> items/components (only hammer that works billets, Sec 9); **die material gates hardness** (quench-hardened dies to shear/stamp hadfield & HSS) | Planned |
| Reverberatory furnace | IIIa | multiblock | fuel (separate firebox) | refractory | crushed copper ore + heat -> copper matte; **coal burns in a separate part of the multiblock** (flame over the charge, fuel never mixes) | Planned |
| Pierce-Smith converter | IIIa | multiblock | air + MP | refractory, heavy plate | matte + air -> molten/blister copper | Planned |
| Acid plant (lead-chamber) | IIIa | multiblock (chambers) | fuel/process | lead-lined chambers | **captured SO2** (smelter fume from roasting sulfide ore) + water -> **sulfuric acid** in bulk (for Stage V electrolysis/batteries) | Planned |
| Lancashire boiler | IV | megablock + multiblock (RCC) | fuel + water | **hadfield** plating, rolled pipe, cap, injector | water + fuel -> HP steam | Exists |
| Cornish engine | IV | megablock + sub-machine | HP steam | **hadfield** cylinder, bedplate, conrod, flywheel, bearings | HP steam -> MP (**~4 kW nominal, throttleable**) | Exists |
| Large Cornish pumping engine | IV | megablock (~3wx6t) | HP steam | hadfield cylinder, heavy beam, heavy flywheel | HP steam -> **one large sub-machine**: heavy blower @ ~160 L/s **or** heavy pump @ ~36 L/s (large furnace needs **two** engines, one each) | Planned |
| Tandem Corliss engine | IV | megablock + sub-machine | HP+LP steam | hadfield cylinders x2, heavy flywheel | steam -> large MP / generator (**~36 kW = 36 helve hammers**) | Planned |
| Open-hearth furnace | IV | multiblock (hearth + regenerative chambers) | fuel + regenerated air (no bath air-blast) | refractory, checker brick, heavy plate | pig + **bulk scrap** + flux -> **low-N steel** (~50 ppm); slow, time scales with scrap; low-N + scrap-recycle gate (boiler plate / pressure parts / HSS feedstock) | Planned |
| Dynamo (DC) | V | RCC megablock (Corliss flywheel variant) | Corliss engine | copper wire coil (replaces flywheel) | upgrade in place: removes MP, **integral commutator -> DC** @ fixed V (Corliss kW x ~20 % impure / ~80 % pure); early bootstrap | Planned |
| Alternator (AC) | V | RCC megablock (Corliss flywheel variant) | Corliss engine | **pure** copper coil, slip rings | upgrade in place: removes MP, **slip rings -> AC** @ fixed V, **freq prop. to speed** (single/three-phase); **gated behind pure copper** | Planned |
| Synchroniser | V | block | - | brass, coils, synchroscope | matches **frequency + phase** to **parallel alternators** + share load | Planned |
| Transformer | V | block | - | iron core, copper wire coil | AC -> AC at stepped V | Planned |
| Rectifier (rotary / mercury-arc) | V | block | - (AC-driven) | coils + commutator, or mercury tube | **remote AC -> DC** (single-phase; **frequency-band gated**) | Planned |
| 3-phase rectifier | V | megablock | - (AC-driven) | coils, copper plate, brass | **3 phase-offset AC lines -> one smooth, powerful DC** (big electrolysis banks) | Planned |
| Wire extruder | V | megablock | MP | bedplate, draw die | rod/plate -> wire (impure or pure copper) | Planned |
| Cable | V | block (on a face) | - (conductor) | copper wire, insulator | carries the network; **melts under high current** | Planned |
| Heavy cable | V | block (on a face) | - (conductor) | heavy copper wire, insulator | high-current; **required at the generator & high-power machines** | Planned |
| Inset cable | V | block (inset) | - (conductor) | copper wire | hidden in-block wiring | Planned |
| Inset outlet | V | block (inset) | - | brass, wire | connects lighting / low-draw loads | Planned |
| Power pole | V | megablock (tall) | - (conductor) | timber/iron pole, insulators | long-distance spans (click pole->pole); **2 normal + 1 heavy cable slots**, sneak to select | Planned |
| Light bulb | V | block | AC/DC | glass, copper wire | current -> light | Planned |
| Electrolysis cell | V | blocks | DC (~7.2 kW each) | brass/copper, acid bath | impure plate + DC -> pure plate; **electrolyte = one-time fill + slow top-up** (acid regenerates, Sec IIIa) | Planned |
| Arc furnace | V | multiblock | **three-phase AC** (**3 alternators ~ 86 kW**, one electrode/phase) | refractory, electrodes, heavy plate | universal electric melter: HSS (scrap + W + Cr) / scrap/recycle remelt / iron ore -> ingot iron + slag / copper ore -> matte (Stage V); **melt temp scales with delivered power - circuit sag -> cold arc** (Sec 6) | Planned |
| Battery (acid/dry cell) | V | megablock | - (store) | brass, plates | DC <-> stored DC; **backup that keeps lamps/low loads lit when the generator is idle** | Planned |

All engine **sub-machines** - flywheel, fluid pump, air blower, dynamo, alternator, and their **large**
(Stage IV) variants - are **RCC megablocks** that attach to the engine's drive face.

Stage II **brass bearings** use early hand-/tilting-furnace bronze (the tilting crucible furnace bulk-
smelts copper from the start, Stage I) - *mass* copper is Stage IIIa, but small bronze for bearings is
available before it, so the Stage II engine is not gated behind the copper add-on.

---

## 9. The billet pipeline (forming, cutting, stamping)

**Billets** (2400 u large / 800 u small) are bulk stock workable **only** on steam machinery. They exist
in **every forgeable/rollable metal** (wrought iron, mild steel, hadfield) - the *universal* solid
feedstock, and **all fabricated components start from a billet, never a vanilla ingot or plate** (Sec 11).
Cast iron is never billeted: it is poured molten into molds. Stock carries its remaining mass in a
unit-count attribute. The rolling mill **forms** (does not divide); the **steam hammer shears**
(divides). Flow:

```
Billet (large 2400u / small 800u)
   |  rolling mill(s) - install rollers, set gap; reduces & profiles, does NOT cut
   v
Profiled stock  -->  plate-stock[2400] / bar-stock[2400] / pipe-stock[2400]   (unit-count attribute)
   |  steam hammer + shear-die - divides by item mass
   v
Discrete items   plate-stock[2400] / 200u = 12 plates;  bar-stock / 100u = 24 rods;  pipe-stock / 150u = 16 pipes
```

### 9.1 Rolling mill - rollers, gaps, trains

- **Rollers = replaceable tooling** setting the **profile** (cross-section). **Gap setting** sets the
  **thickness** step. Profile + gap pick the product, so **one roller type = one product family** - a
  3-mill train deliberately cannot make every rolled item.
- **Gap steps (up to 5; each roller exposes only its real-product gaps):**

| Gap | Flat rollers - *4 steps (5-2)* | Grooved rollers - *4 steps (5-2)* | Pipe rollers - *3 steps (5-3)* |
|---|---|---|---|
| 5 | heavy plate (HP boiler/engine) | heavy bar | large pipe |
| 4 | plate (vanilla) | rod (vanilla) | rolled pipe (HP steam) |
| 3 | sheet metal | thin rod | small tube |
| 2 | strip (feeds stamping) | wire rod (-> extruder / rivet blanks) | - |
| 1 | - | - | - |

  Blank rows are **not** selectable for that roller; **no filler items are invented** - the setting
  count is simply restricted per roller (flat 5-2, grooved 5-2, pipe 5-3).

- **Reduction:** each pass/stand reduces **one gap step**; a billet can't reach final thickness in one
  pass.
  - **Budget (one mill):** roll -> step gap down -> re-feed -> repeat. Tedious but one machine.
  - **Train (tandem mill):** several mills in a line, each pre-set to the next smaller gap; stock is
    handed down the line and exits finished in one motion. More blocks up front, big convenience payoff.
    Stock must enter at the largest gap; each mill configured separately.

- **Mill speed:** each mill processes a piece in **~3 s** (one pass = one gap-step reduction). A budget
  single-mill reduction takes `3 s x gap steps`; a primed train overlaps passes so finished stock exits
  roughly every **3 s**.

- **Hand-off timing:** **on animation end.** A mill runs its roll animation to completion, passes the
  stock to the next mill's input, and on the **next tick the next mill's animation starts** - the piece
  visibly clears one stand before the next picks it up. Adjacent BEs, no moving entities.

- **In-transit rendering:** the travelling stock is a **mesh rendered/animated inside each mill in
  turn** (not a dropped entity). Live deformation is impractical, so **each stand renders the mesh
  already at its own output thickness** and animates it sliding through - the piece **steps down** stand
  to stand, reading as an almost-seamless continuously-thinning piece across a packed train.

- **Jam:** if stock meets a stand set **wider-or-equal** to the previous (wrong order / too thin), the
  **whole train violently stops** - harsh sound + smoke burst (ExSounds / ExParticles) - the **rollers
  must be removed** to clear it, and the **jammed stock drops to the ground** as an item. A misbuilt
  train is a visible, recoverable failure, never a silent no-op.

### 9.2 Steam hammer - dies & LP/HP capability

Installable dies select the operation: **open die** (free forging large components), **shear-die** (cut
profiled stock), **stamp dies** (nail / rivet header / bracket / washer / plate-blank).

- **LP steam (Stage III):** open-die forging works, **and shearing is the explicit LP exception** -
  cutting needs a single decisive blow, not sustained energy, so the hammer can divide billet stock
  into plates/rods/pipes from day one of mass steel.
- **HP steam (Stage IV):** unlocks **stamp/blanking** - bulk small goods (nails, brackets, rivets,
  washers) from strip/bar. Sustained high blow energy is what enables die-blanking. Same machine, grown
  capability.
- **Helve vs steam hammer:** the vanilla **helve hammer** is the *ingot-scale* forging/shingling tool
  (Stage I); the steam hammer is the **only** hammer that can work the 800-2400 u **billets and rolled
  stock**. **Die material scales with the work:** plain dies handle cast/mild stock, but shearing or
  stamping **hadfield or HSS** needs **quench-hardened dies** (mirrors the boring-machine drill-bit
  tiers, Stage I).

- **Rivets:** bar-stock -> shear into blanks -> **header die** forms the head (or one cold-header die
  blanks + heads in a cycle).
- **Stamp** is the volume manufacturer of ordinary vanilla parts (cheaper/faster than the anvil).
- **Batch size = die cavity count.** A stamp die yields **one item per impression modelled in its
  shape**: a single-plate die stamps **1 plate** per blow; a multi-cavity die stamps **N items** per
  blow. The die mesh *is* the batch spec - read the count straight off its cavities, no separate config.

---

## 10. Operation & interaction grammar

**No GUI windows unless unavoidable.** All state is read from **block-info / hover**.

| Action | Gesture |
|---|---|
| Install roller/die **or** load billet/stock | **Sneak + RMB** with the item in hand (context by held item) |
| Remove installed tool | **Sneak + RMB** empty-handed |
| Setting step **up** (larger gap) | **RMB** *(engine-style, mirrors the throttle)* |
| Setting step **down** (smaller gap) | **Sprint + RMB** |
| Operate a **manual** station | **RMB (repeated)** - each press = one stroke/pass; player drives rhythm, steam/MP provides force (no pressure -> no stroke) |

- A **rolling mill is powered/automatic**, so on it RMB/Sprint+RMB are repurposed to the gap cycle and
  Sneak+RMB installs rollers / loads stock - no manual per-pass click.
- **Reconfiguration lock:** a machine **cannot be reconfigured while working**; in a **train**, no
  machine can be reconfigured while **any** train member is working. Prevents mid-pass gap changes from
  corrupting in-flight stock.

**Two operating tiers:**

- **Manual heat stations** (puddling, cupola tap, crucible pour, sand casting): RMB-hold with a **tool**
  + animation + timing/repetition, in the style of the existing hand-crank pump (2 L/s *(live)*).
  Hands-on; where operating tools live. The **puddling furnace** adds two station-specific gestures:
  **operate the chimney lid** to throttle temperature, and **work the hatch** with the rabbling bar to
  ball up and then pull the white-hot balls out one by one (Stage I).
- **Powered machines** (rolling mill, steam hammer, boring machine, wire extruder): install tool, load
  material, then run while steam/MP is available; a **train** advances stock automatically. Implemented
  as MP-load recipe loops on the constant-power generator model (Sec 5.3).

**Material handling (no general belts).** There is no vanilla belt API and full visible belts are a
perf sink, so bulk solid logistics uses three pragmatic mechanisms: **(1) adjacency hand-off** - the
rolling-mill train model generalized: adjacent machine BEs pass a workpiece BE->BE on a tick, rendering it
sliding through (visible, no moving entity) for production lines; **(2) vanilla chutes + Archimedes screw
+ a powered bucket elevator / skip hoist** for gravity/vertical runs; **(3) an abstracted pneumatic tube** -
items move through a tube network at a **gated throughput with no visible item** (like fluids in a pipe),
reusing the network graph, for long-range bulk. Visible where it matters, abstracted where it can't be.

**The primary solid-transport need is charging furnaces from ore-mixer bunkers** - a *point-to-point
lift*, not a network: the bunker already buffers a large charge, so a **skip hoist** (inclined animated
skip car, the iconic blast-furnace charger) or a vertical **bucket elevator** raises blast mix to the
furnace-top hoppers at the furnace's consumption rate, while the mixer batches behind it. (Vanilla chutes
only suffice if the bunker sits above the top; the pneumatic tube suits fine material / long horizontal
runs, not lifting a dense charge.) This single mechanism covers the main case, so general belts stay
unnecessary.

---

## 11. Industrial components catalogue

Organized by **fabrication method** (each maps to a station and gates a believable machine subset).
Target: a machine needs **2-4 component types**, each **1-2 steps** from a base material.

**Every fabricated component starts from a billet - never a vanilla ingot or plate.** Forged parts
start from a **wrought-iron billet** (helve hammer); rolled/stamped parts from a **mild-steel or
hadfield billet** (rolling mill / steam hammer). **Cast** parts are the sole exception: poured straight
from **molten** cast iron into component molds.

**The boring machine is the finishing gate (Stage I, MP).** Most rotating or mating parts come off the
casting/forging *rough* and need a precision pass: **boring** (cylinder sleeves, bearings/bushings,
flywheel & gear hubs, pump/valve bodies, cock seats), **turning** (shafts/axles, crank journals, mill
rollers + grooves, die blanks), **screw-cutting** (bolts, staybolts, valve stems, the rolling-mill gap
screw). No boring machine -> no true bearings -> no high-speed rotating machinery, so it gates every engine
and mill. Its **drill bit is replaceable tooling whose material gates the hardest metal it can machine**
(cast-iron -> quench-hardened steel -> HSS; Stage I). The helve hammer does the rough *forging*; the
boring machine does the *finishing* cuts.

**Cast** (cupola -> ceramic/sand molds; cast iron) - compression/static:
- **Cylinder sleeve** - cast iron -> (boring) -> **finished cylinder** (engine bore)
- **Engine bedplate** - cast iron -> bedplate casting (universal "anchor" for large machines)
- **Flywheel casting** - cast iron -> flywheel -> **boring machine: bore the hub** to seat on the shaft
- **Cast pipe segment** - cast iron -> LP water main
- **Furnace grate / door casting** - cast iron -> furnace parts
- **Cast radiator section** - cast iron -> radiator (Stage IIa)
- **Valve body casting** - cast iron -> valve body -> **boring machine: bore the seats**

**Forged** (puddling balls -> helve hammer; wrought iron) - tension:
- **Connecting rod & crank** - wrought-iron billet -> forged conrod -> **boring machine: turn journals / bore the big-end**
- **Forged shaft / axle** - wrought-iron billet -> shaft -> **boring machine: turn true to size**
- **Tie rod / stay bar** - wrought-iron billet -> stay -> **boring machine: screw-cut the threaded ends**
- **Rivets & staybolts** - bar-stock -> shear blanks -> header die -> rivets; **staybolts thread-cut on the boring machine** (Sec 9.2)

**Rolled** (rolling mill; mild/hadfield) - plate & tube:
- **Boiler plate ("heavy plate")** - billet -> flat rollers gap 5; **steel plate must be low-nitrogen** (open-hearth/arc) or use **wrought-iron plate** (the safe traditional choice for early/LP boilers)
- **Rolled / seamless pipe** - billet -> pipe rollers gap 4 (HP steam, seamless)
- **Small tube / small gas pipe** - billet -> pipe rollers gap 3 (compact face-run gas/fluid line) - **rolling-mill only**
- **Sheet metal** - billet -> flat rollers gap 3 (cladding/duct/hopper)

> **Pipe sourcing rule.** **Normal (full) pipes can be made two ways:** an early **plate grid recipe**
> (wrap/rivet a plate, or cast in cast iron), available from **Stage I** - Stage I already pipes air into
> the blast/cupola furnaces - **and** rolled on the **rolling mill** later (a full pipe is a rolled product
> too). The plate/cast recipe is simply the **earlier alternative**, not the only route. **Small pipes and
> seamless HP pipe are rolling-mill products only** (a precise thin or seamless bore can't be
> plate-assembled; Stage III). So gas/water mains exist from Stage I; **small face pipes are the Stage III
> compact upgrade**, bridged to full pipe by a **pipe reducer**.

**Stamped / drop-forged** (steam hammer + die; mild/hadfield) - mass parts:
- **Heavy cap / end cap** - billet -> open die (HP) -> boiler heads, cylinder covers
- **Stamped bracket / fitting** - strip/bar -> stamp die (HP) -> brackets in bulk

**Copper / brass** (Stage IIIa) - fittings & bearings:
- **Brass bearing / bushing** - bronze -> cast bearing -> **boring machine: bore/ream to the shaft**
- **Brass cock / pressure gauge / Giffard injector** - bronze/brass -> boiler accessory
- **Copper boiler tube** - copper -> heat-exchange tube

**Illustrative bills of materials:**
- *Steam engine* = bedplate + bored cylinder sleeve + connecting rod + flywheel + brass bearings
- *LP boiler* = **wrought-iron or low-N-steel** boiler plate + rivets + stays + heavy cap + injector
- *HP (Lancashire) boiler* = **hadfield** plate + rolled pipe + rivets + stays + heavy cap + injector

### 11.1 Build economy - costs, reference lines, ratios

All numbers here are **tunable baselines** (via ExRecipeCosts) - they exist so the cost curve is *defined*,
not hand-waved. Costs are in **metal units (u)**; **refractory brick / fireclay / graphite electrodes are
counted separately** ("+ R"). A **large billet = 2400 u**, small = 800 u.

**Component masses (baseline u).** Cast (poured): bedplate 800, flywheel 800 / heavy flywheel 2400,
cylinder sleeve 400, pump/fan body 400, cast-pipe segment 300, valve body 200, grate/door 200. Forged:
conrod 300, shaft 300, stay 100, staybolt 50, rivet ~5. Rolled (Sec 4.1): heavy plate 400, plate 200,
rolled pipe 150, sheet 100. Bronze: bearing 100, cock/gauge/injector 150. Tooling: roller 300, die 300,
drill bit 200, draw die 200.

**Machine build cost (key spine; metal u, ~= large billets):**

| Machine | BOM (baseline) | Metal u | ~= billets |
|---|---|---|---|
| Cold/hot-blast furnace | cast pipe 300 + grate 200 (+ R) | 500 (+R) | 0.2 |
| Cupola | cast pipe 300 + grate 200 (+ R) | 500 (+R) | 0.2 |
| Coke oven / gasworks / lime kiln | grate 200 (+ R) | 200 (+R) | 0.1 |
| Ore mixer | mixer body 400 + bunker 4x sheet 400 (+ frame) | ~1200 | 0.5 |
| Boring machine | bedplate 800 + cutting head 200 (+ bit) | 1000 | 0.4 |
| Cornish boiler (LP) | 4x boiler plate 1600 + heavy cap 400 + injector 150 + stays/rivets 200 | 2350 | ~1.0 |
| Watt engine | bedplate 800 + cylinder 400 + conrod 300 + flywheel 800 + 4x bearing 400 | 2700 | ~1.1 |
| Fluid pump / air blower (sub-machine) | cast body 400 + valve/fan 200 | 600 | 0.25 |
| Fluid tank / gasholder | 6-8x sheet ~1200 (+ frame/valve) | ~1400 | 0.6 |
| Bessemer converter | 4x heavy plate 1600 + heavy cap 400 (+ R lining) | 2000 (+R) | ~0.8 |
| Ladle | 3x heavy plate 1200 (+ R) | 1200 (+R) | 0.5 |
| Rolling mill | bedplate 800 + 2x heavy gear 1200 (+ rollers) | 2000 (+ tool) | ~0.85 |
| Steam hammer | bedplate 800 + heavy cap 400 (+ dies) | 1200 (+ tool) | 0.5 |
| Steam ore crusher | bedplate 800 + hadfield jaws 600 | 1400 | 0.6 |
| Open-hearth furnace | 4x heavy plate 1600 (+ checker brick) | 1600 (+R) | ~0.7 |
| Lancashire boiler (HP) | 4x hadfield plate 1600 + 6x rolled pipe 900 + heavy cap 400 + injector 150 | 3050 (hadfield) | ~1.3 |
| Cornish / Tandem Corliss engine | hadfield cylinder(s) 400-800 + bedplate 800 + heavy flywheel 800-2400 + conrod/bearings | 2700-4000 | 1.1-1.7 |
| Large pumping engine | large hadfield cylinder 800 + heavy beam 1200 + heavy flywheel 2400 | 4400 | ~1.8 |
| Dynamo / alternator | copper wire coil ~1600 (+ frame) | ~1600 Cu | ~0.7 |
| Arc furnace | 4x heavy plate 1600 (+ graphite electrodes + R) | 1600 (+ elec) | ~0.7 |

Minor blocks (cables, face pipes, lamps, poles, sand-mold) cost a handful of u by component - not listed.
**Tooling (rollers / dies / bits) is a separate recurring cost** that wears out with use.

**Reference lines (per stage & cycle), at baseline/live rates.** Each is one *balanced unit* - replicate to
scale. (u/s are nominal full-margin maxima, Sec 6; costs from the table above, in **large billets**.)

*Stage I*
- **Iron (blast -> pig).** 1 cold-blast furnace + ore mixer + coke oven + sand molds; **waterwheel MP**
  blower (48 L/s) + helve hammer. Out **~30 u/s pig**. In ~30 ore-u/s + flux + **~15 coke-u/s** (cold 0.5).
  Cost **~1.2** (no steam yet).
- **Cast iron (cupola cycle).** 1 cupola fed sand pigs (+ scrap) + MP blower. Out **~20 u/s cast iron** ->
  molds. In pigs + **~5 coke-u/s**. Cost **~0.2**. The cast-iron supply for all LP machinery + iron-age blocks.
- **Wrought iron (puddling cycle).** 1 puddling furnace (3-bed) + helve hammer. Out **1200 u pig -> 12 balls**
  per heat (~few min, **~5-7 u/s** effective, manual). In pigs + crushed-ore oxide bed + fuel. Cost **~0.2**
  + rabbling bar. The wrought-iron (= vanilla iron) supply.

*Stage Ia*
- **Crucible steel (low-volume tools).** Cementation furnace (wrought + charcoal -> blister) + beehive kiln
  + draft crucible furnace + helve hammer. Out **~2-4 u/s** (batch, high-value). Cost **~0.3** + refractory.

*Stage II*
- **Power module (the unit you replicate).** **1 Cornish boiler (LP) + 1 Watt engine (4 kW) + 1 sub-machine**
  (pump 16.67 L/s | blower 48 L/s | flywheel MP) *(live)*. In fuel + feedwater. Cost **~2.4**. One module per
  job; a working rig runs ~3 (blower + pump + flywheel).
- **Gas lighting.** coke oven/gasworks -> full pipe -> **gasholder (~6000 L)** -> small face pipes -> **gas
  lamps (~0.2 L/s)**. One holder ~ a lamp-week. Cost gasholder ~0.6 + lamps/pipe (minor). Pre-electric tier.

*Stage IIa*
- **Heating.** 1 boiler -> chained cast radiators (steam) **or** brick stove -> passthrough walls -> chimney
  (exhaust). Holds a greenhouse above the cold threshold all winter. Cost ~radiators (0.13 each) + a power module.

*Stage III*
- **Steel line (the spine).** 1 hot-blast furnace (**45 u/s** pig) + 2 cowpers + mixer + oven; **3 Bessemers**
  (3 x 16 = **48 u/s**) + 1 ladle; 1 mill + 1 hammer (shared). Power: 3 power modules (blower+pump+flywheel).
  Out **~45 u/s steel = 1 large billet / 53 s**. In ~45 ore-u/s + **~11 coke-u/s** (hot 0.25) + make-up water.
  Cost **~12** - the real industrialization gate.
- **Forming cycle (shared shop).** 1 rolling mill + 1 steam hammer + 1 boring machine on the MP line; **~200+
  u/s** - **serves many steel lines** (built once). billet -> stock -> 12 plates / 24 rods / 16 pipes. Cost
  **~1.75** + tooling (recurring).

*Stage IIIa*
- **Copper line.** 1 reverberatory (**~20 u/s matte**) + 1 Pierce-Smith converter (**~16 u/s copper**) + acid
  plant (SO2 -> acid byproduct) + (tilting furnace + ladle for bronzes). Power 1 blower module + MP. Out ~16
  u/s copper -> rods. Cost **~1** + refractory + lead.

*Alloying (Stage III/IIIa cycle)*
- **Ladle.** Fed by a Bessemer/open-hearth steel canal + tilting crucible furnaces pouring Mn/W/Cr/Sn/Zn.
  Out Hadfield / HSS / bronzes by held proportion (no extra power). Cost ladle **~0.5**.

*Stage IV*
- **HP power module.** **1 Lancashire boiler (HP) + 1 Cornish engine (4 kW, throttleable, hadfield) + 1
  sub-machine**, or **1 Tandem Corliss (36 kW)** for line/generator drive. Cost HP module **~3** (hadfield-heavy);
  Corliss **~1.7** + boilers.
- **Large blast furnace (community).** 1 large furnace (**135 u/s**, 6 tuyeres) + **2 large pumping engines**
  (heavy blower 160 L/s + heavy pump 36 L/s, one sub-machine each) + HP boilers + 2 cowpers + 3 smoke stacks;
  feeds **~8-9 Bessemers** + ladles + 1-2 mills. In ~135 ore-u/s + **~27 coke-u/s** (large 0.2). Cost **tens of
  billets** - a server-community build, not solo.
- **Low-N steel (open-hearth cycle).** 1 open hearth + bulk scrap + flux + regenerators. Out **~10-20 u/s
  low-N steel** (slow, scrap-hungry); the boiler-plate / HSS-feedstock source + bulk scrap recycler. Cost **~0.7**
  + checker brick.

*Stage V*
- **Electrolysis bootstrap.** **1 Tandem Corliss + 1 impure-copper dynamo (7.2 kW)** -> **1 cell** -> first pure
  copper; then a **pure dynamo (28.8 kW) -> 4 cells**. In impure plate + acid (one-time fill). Cost ~**2.5** + cells.
- **Arc furnace / HSS (endgame).** **3 Tandem Corliss + 3 alternators (~86 kW) + synchroniser** -> three-phase
  line -> **1 arc furnace**. Out **~10 u/s HSS** (low-N + W + Cr), or 30/25/20 u/s scrap/ore/matte. Cost **the
  biggest build** - 3 engines + 3 alternators + 3 HP boiler banks.
- **Lighting & transmission.** 1 alternator -> transformer (up) -> cable / power poles -> transformer (down) ->
  bulbs (+ battery backup); rectifier for DC drops. One generator lights many. Cost cabling + bulbs (minor).

*Cross-stage*
- **Lime & cement (Stage I+).** 1 lime kiln (**~10 u/s quicklime**) + optional tapped slag ground in the
  crusher -> cement (**1 lime : 2 slag**). Out mortar/flux + bulk concrete blocks. Cost lime kiln ~0.1 + R.

**Pinned ratios (were open; now baseline-decided):**

- **Coke per pig:** cold blast **~0.5 u/u**, hot blast **~0.25 u/u**, large furnace **~0.2 u/u**. The
  hot-blast payoff = **half the coke at 1.5x the rate** - the whole reason to build cowpers. Coke oven
  yields ~1 coke-u per coal-u (gasworks trades some coke for more gas).
- **Water -> steam = 1 L : 16 L** in the sim (the boiler converts litre-for-litre; **pressure** is the
  separate state, Sec 5.2). The engine **returns condensate**, so the **16.67 L/s pump** *(live)* covers
  make-up + margin against the 30 L/s *(live)* Watt draw - only spill/leak loss needs topping up.
- **Line shape = 1 furnace : 3 converters : 1 ladle : 1 mill : 1 hammer.** The **large** furnace
  (135 u/s) scales to **~8-9 converters** + 1-2 mills - a deliberate community-scale steel works.

**Shared central assets.** The mill (~200+ u/s), hammer, boring machine and crusher run **far faster than
one line supplies** (16 u/s), so they are **built once as a central shop serving many furnaces/converters**
- a solo player needs only one of each. They are priced as shared infrastructure; the **gate is always
upstream smelting**, never forming. (The table u/s figures are *nominal full-margin* maxima, Sec 6 - an
under-powered or cold-running rig delivers less.)

---

## 12. Payoffs - what components do outside building machines

Industrialization must reward more than "more machines":

1. **Cheap bulk vanilla parts.** The stamp mass-produces **nails, strips, plates, brackets** far
   cheaper/faster than the anvil -> lowers the cost of vanilla base-building (supports, reinforced blocks,
   doors, chutes) and feeds other mods. **The main reward of going industrial.**
2. **An iron-age decorative/structural block set.** Cast iron -> railings, lamp posts, fences, grates,
   manhole covers, stairs, riveted plating; wrought-iron gates/fences; sheet-metal roofing/cladding;
   **functional cast radiators** (IIa); pipes as visible plumbing; stoves; **gas (Stage II) then electric
   (Stage V) street/interior lamps**; signage.
3. **Lighting, two tiers.** **Gas lamps** (coal gas, Stage II) light a base decades before electricity;
   **electric bulbs** (Stage V) are the late-game civic payoff. Both buffer-backed (gasholder / battery),
   so a settlement stays lit.
4. **Bulk building materials (cement/concrete).** The lime kiln + optional ground slag -> **cement /
   concrete** for mass structural and decorative blocks (foundations, paving, cast-concrete fixtures) -
   a high-volume base-building payoff beyond metal parts.
5. **Tools, armor, trade.** Crucible/HSS for better tools; plates for armor; finished components as
   high-value trade goods.

---

## 13. Tools

**Operating tools** (durability/consumable, manual stations):
- **Rabbling bar / puddler's rod** - stir the puddling furnace, ball up the wrought iron
- **Crucible tongs** - carry fired crucibles between kiln and draft crucible furnace

*(No slag skimmer - slag pours off a separate tap. No hand ladle - the ladle is a stationary multiblock,
Sec 7.)*

**Tooling** (installed in machines to select output / capability): flat / grooved / pipe **rollers**
(+ gap), **steam-hammer dies** (open / shear / nail / rivet-header / bracket / washer / plate-blank), and
**boring-machine drill bits** (cast-iron -> quench-hardened steel -> HSS, gating machinable hardness;
Stage I). **Dies and bits scale with material** - quench-hardened dies/bits are needed to work hadfield
and HSS. Rollers, dies and bits are themselves **bored/turned on the boring machine** (Sec 8) to their
profile - the machine shop makes its own tooling.

---

## 14. Mod & dependency map

| Mod | Contains | Hard deps |
|---|---|---|
| **exlib (ExpandedLib)** | shared framework: config, networks, particles/sounds, orientation, sub-commands | game |
| **IMEX (Ironmaking Expanded)** | Stage I iron line + coke oven (coal gas) + lime kiln/cement | exlib |
| **IMEX - Crucible add-on** | Stage Ia | IMEX |
| **PPEX (Pipes & Power Expanded)** | Stage II + IV steam/power, pipe & MP networks, gas lighting, fluid tank/sprinklers, logistics (elevator/tube) | exlib |
| **PPEX - Heating add-on** | Stage IIa (separate, opt-in, punishing) | PPEX |
| **SMEX (Steelmaking Expanded)** | Stage III-IV steel: Bessemer + ladle + billet pipeline + **open hearth (low-N steel)** | exlib, PPEX (needs steam) |
| **SMEX - Copper add-on** | Stage IIIa + sulfuric-acid plant (SO2 capture) | SMEX |
| **PPEX + SMEX - Electric add-on** | Stage V dynamo/alternator/electrolysis/arc/HSS | PPEX(IV) + SMEX(III) |

Tech-tree order (materials gate construction): cast iron -> Stage II engines; steam -> Stage III hot
blast/Bessemer; hadfield + rolled pipe -> Stage IV HP boilers/engines; Stage IV power + pure copper ->
Stage V. No hard circular dependencies.

---

## 15. Suggested build order

Spine first, branches independent:

1. **Stage I** - coke oven + lime kiln, iron line (sand-cast pigs -> cupola + puddling), **boring machine**
   (water-powered, gates engine quality), cast/forged components, ceramic molds.
2. **Stage II** - Watt engines, blower/pump/flywheel, **fluid tank + sprinklers**, **gas lighting**,
   logistics (bucket elevator / pneumatic tube), component-gated builds.
3. **Stage III** - hot blast + Bessemer + **billet pipeline** (rolling mill(s), steam hammer, ladle) +
   **LP steam crusher** (mass ore + Mn for hadfield).
4. **Branches, any order:** Ia crucible / IIa heating / IIIa copper.
5. **Stage IV** - HP steam, large engine + large blast furnace + **HP crusher** (chromite/wolframite for HSS)
   + **open hearth** (low-N steel for boilers + HSS feedstock + bulk scrap recycling).
6. **Stage V** - electrical network, dynamo (DC) -> alternator (AC), electrolysis, arc furnace, HSS.
   Last, isolated.

---

## 16. Open balance questions

Design is decided; these are **numbers/feel to tune in playtest**, not open mechanics:

- Item-mass divisors (plate 200 u, rod 100 u, ...). Billet sizes **decided**: 800 u small / 2400 u large.
- Production rates via the Stage III formulas: dense-pile charge (~900 u), melt intervals (cold ~30 s ->
  ~30 u/s, hot ~20 s -> ~45 u/s, large ~135 u/s), Bessemer >= 4800 u / ~300 s -> ~16 u/s. These are the
  **nominal full-temp-margin rates**; the dynamic heat balance (Sec 6) scales them with the margin.
- **Build economy now baseline-decided in Sec 11.1:** machine BOMs / unit costs, the reference steel line,
  the pinned ratios (coke-per-pig cold 0.5 / hot 0.25 / large 0.2 u/u; water->steam 1:1 L; line shape
  1 furnace : 3 converters : 1 mill : 1 hammer), and shared-central-asset pricing. All ExRecipeCosts-tunable.
- **Dynamic heat balance (Sec 6) curves:** melt threshold per material; coke%->base-temp and
  blast-temp->buff curves (and the high-coke/cold-blast guaranteed-melt floor); air-flow->temp coupling;
  scrap-mass->cooling rate (furnaces + converter + cupola); cowper/open-hearth regenerator charge->delivered
  temp; arc-furnace power->temp (vs circuit sag); boiler pressure = f(fuel, feedwater) - draw, engine
  power->delivered pressure, blower flow->engine power; ambient/seasonal `T_loss` (and the winter on/off
  config gate) + insulated-canal cooldown factor. All tunable; the model must gate efficiency, not
  possibility.
- LP vs HP pressure bands and the MP/litre efficiency gap between Watt and Cornish engines.
- Engine kW ratings (Watt/Cornish ~4 kW; Corliss ~36 kW) and the load-unit->kW display scale (~8 kW/unit).
- Large-engine output (~160 L/s blower, ~36 L/s pump) vs 6-tuyere demand (144 L/s); exhaust balance
  (4 outlets x 36 = 144 L/s, 3 smoke stacks, 2 cowpers); cowper throughput-cap removal.
- Generator output - dynamo (DC) and alternator (AC), ~36 kW x coil efficiency (~20 % impure -> ~7.2 kW /
  ~80 % pure -> ~28.8 kW); the alternator's AC voltage/current rating; the **DC resistance budget**
  (resistance per wire/block, sag threshold).
- Electrical hardware: per-conductor **current rating** (normal-cable melt vs heavy-cable capacity),
  **transformer step ratios**, **power-pole max span**, and the per-tick cost of solving the
  node-voltage circuit on large networks.
- Engine **stall threshold** (rated vs max power, all engines); the **Corliss governor's steam-per-kW
  curve** (idle-min -> full-max draw + matching condensate scaling), while Watt/Cornish keep a fixed draw.
- AC physics: the **frequency-vs-engine-speed mapping**, the **rectifier's operating frequency band**,
  the **synchroniser's match tolerance**, and the **three-phase DC power gain** vs single-phase.
- Sink **load model** (constant-power assumed) and each consumer's **minimum-voltage cutoff**; the
  **cable heating time-constant** (Joule heat-in vs cooling) setting how long an overload survives.
- Arc furnace: three-phase draw (3 synced alternators ~86 kW); **per-role baselines** (tunable, at full
  ~86 kW): scrap remelt **~30 u/s**, iron-ore smelt **~25 u/s**, copper-ore -> matte **~20 u/s**, HSS alloy
  **~10 u/s** - i.e. comparable to a hot-blast furnace but coke-free and power-bound (scales with delivered
  kW, Sec 6).
- Worldgen for the one new ore (**wolframite**); rhodochrosite/chromite use vanilla worldgen.
- Bessemer over-blow curve: carbon-burn and slag-drop per second, and the mild-steel tap window before
  it runs to ingot iron. Also the **converter cold-scrap cap** (~20-30 % of the heat) before the bath
  freezes, and the **blow-time-vs-scrap** curve (base blow + melt time per scrap unit).
- Open hearth: heat duration (much slower than the Bessemer), the **scrap fraction** it can take (far
  higher than the converter), and its fuel cost.
- Steel nitrogen: the **ppm N per process** (Bessemer ~150 / open-hearth ~50, tunable) and the **gate
  threshold** (~80 ppm) above which steel is barred from boiler plate, heavy cap, staybolts and the HSS
  feedstock (hadfield is exempt, gated by the alloy). Light-flavor tuning: high-N **forge/roll crack-waste
  chance**, the **tool/armor durability** delta, and the cosmetic tell (block-info ppm + tint).
- Recarburising: carbon mass added per **powdered coke**; the coke-cover consumption rate and the
  **zinc boil-off rate** for an uncovered brass/bismuth-bronze melt.
- Waste-alloy crush yield (target: recover ~100 % of base Fe/Cu) and exact bismuth-/black-bronze
  fractions vs the vanilla `metalalloy` ranges.
- Tick-by-tick hand-off pacing in a long rolling train (base mill speed ~3 s/pass).
- HP-steam stamp speed multiplier. *(Stamp batch size **decided** = die cavity count, Sec 9.2.)*
- Drill-bit & die tiers: machinable-hardness thresholds (which bit/die works which metal) and per-tier
  **wear rates**; whether the hardened bit is plain quench-hardened mild steel or crucible steel.
- Steam crusher: the **LP/HP ore-hardness cutoff** (decided: Mn=LP, chromite+wolframite=HP), crush rate
  vs the vanilla pulverizer, and the hardened-scrap throughput.
- Coke oven bulk throughput (coal piles per cycle) vs the vanilla oven.
- Puddling: hearth capacity is **decided** (3-wide hearth = 3 oxide beds x 2 pigs = 6 pigs / 1200 u ->
  12 balls); tune the lid temp-window timing and rabbling cadence.
- Fluid tank capacity (target: days of sprinkler draw per refill); sprinkler **wrench interval range**,
  the 3-block radius (decided), and per-cell water cost (= moisture deficit to 100 %).
- Gas lighting (baselines, tunable): gas yield ~**0.5 L gas / u coal** byproduct (coke oven) or ~**2x**
  gas-primary (gasworks); gasholder ~**6000 L**; gas-lamp draw ~**0.2 L/s** (so one full holder ~ a lamp-
  week, or many lamps a day); small-gas-pipe throughput ~**1 L/s**; battery ~ a few lamp-hours of backup.
- Sulfuric acid (baselines): cell **fill ~50 L once + ~1 L/day top-up** (regenerates); SO2 capture ~**6 L
  acid / 100 u copper-ore smelted** (the byproduct yield); steam-vat fallback ~**4x** the vanilla pot.
- Lime/cement (baselines): lime kiln ~**10 u/s** quicklime (small-furnace scale); cement ~**1 lime : 2
  ground-slag** by mass (slag is the cheap bulk filler).
- Solid logistics: pneumatic-tube throughput, bucket-elevator / skip-hoist lift rate (must meet or beat
  furnace charge consumption), and the ore-mixer bunker -> furnace feed rate.
