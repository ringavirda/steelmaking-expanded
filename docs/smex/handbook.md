<strong>Advanced Steelmaking</strong>
<br>
<i>One step for a seraph, a thousand plates for all of humanity.</i>
<br>
<br>
Steelmaking Expanded adds a full industrial pipeline that works in two stages.
<strong>Stage I</strong> stands up a <strong>blast furnace</strong> and its
support machinery to smelt iron in bulk. <strong>Stage II</strong> bolts a
<strong>Bessemer converter</strong> onto that line to refine the molten iron into
steel. Build the iron stage first and get it running reliably — the steel stage is
expensive and feeds directly off it.
<br>
<strong>To construct most of these structures you need to have tier 3 refractory bricks,
which means you need to achieve steel age before this!</strong> <br>
<br>
<br><strong>════ STAGE I — IRON PRODUCTION ════</strong>
<br>
<br><strong>1. The Blast Furnace</strong>
<br>The <a href=\"handbooksearch://blast furnace door\">blast furnace</a> is the
heart of the operation: a tall multiblock built mostly from refractory bricks,
with an iron door, two <a href=\"handbooksearch://tuyere\">tuyeres</a> (air
intakes) low on the shaft and two gas outlets high up. Place the door block and
<hk>shift</hk> + <hk>rightmouse</hk> it to see the build outline, then fill in
every highlighted block until the structure reports complete.
<br>
<br>The furnace does not eat ore directly; it burns <a href=\"handbooksearch://blast
mix\">blast mix</a>, a packed pile of ore and fuel. <strong>You never craft blast
mix by hand</strong> — it is produced for you by a pair of hoppers stacked above
the shaft. The <a href=\"handbooksearch://reinforced hopper\">Reinforced
Hopper</a> is the magazine: open it (<hk>rightmouse</hk>) and load three things —
<strong>crushed iron ore</strong>, <strong>crushed coke</strong>, and
<strong>lime</strong> (flux). Crush iron ore and coke in a helve hammer or by
hand; coke itself is baked from coal in a <a href=\"handbooksearch://coke
oven\">coke oven</a>. The hopper combines 12 crushed iron + 3 crushed coke + 1
lime into 16 blast mix at a time and stockpiles it internally.
<br>
<br><i>Nothing is wasted if you fumble a pile.</i> Blast mix behaves just like a
coal pile: should you ever end up holding loose blast mix — knocking a pile out of
the shaft, breaking a hopper, scooping spillage off the floor — simply
<hk>rightmouse</hk> the ground to lay it back down and stack more onto it, then
feed it back into the furnace. It also still burns as a fuel in a pinch, so a
stray stack is never lost.
<br>
<br>Below it sits the <a href=\"handbooksearch://bell hopper\">Bell Hopper</a>,
which drips that stockpiled blast mix down into the furnace shaft. Toggle
dropping on or off with <hk>ctrl</hk> + <hk>rightmouse</hk> on the reinforced
hopper; it stops on its own once the furnace is full. So loading the furnace is
really just keeping the reinforced hopper supplied with crushed ore, coke and
lime — the hoppers handle the rest. <br>
<br>To fire it, let the bell hopper fill the furnace, <strong>ignite the blast
mix</strong> inside, and <strong>close the door</strong>. Once every pile in the
hearth is burning, the furnace is full, the door is shut and the exhaust outlets
are not choked, it will begin firing. Internal temperature climbs toward about
1400 °C on its own, so we will help the furnace to rise to higher temps in the
next step. Hold it above iron's melting point (~1482 °C) for a few
minutes and the furnace enters its <strong>Melting</strong> phase, steadily
converting blast mix into a reservoir of <strong>molten iron</strong> and
<strong>molten slag</strong>.
<br>
<br>The fire is fragile. Opening the door, running out of blast mix, a choked or
backfed exhaust, or letting the molten reservoir overflow will all snuff it out —
keep the exhaust flowing and the door shut while it works.
<br>
<br><strong>Blocks required:</strong>
<br>• 98 × Refractory Bricks (tier 3)
<br>• 1 × Blast Furnace Door (the control block, placed first)
<br>• 2 × Tuyere
<br>• 2 × Molten Metal Tap
<br>• 2 × Gas Outlet
<br>• 1 × Reinforced Hopper
<br>• 1 × Bell Hopper
<br><i>The hollow shaft inside is left empty — the hoppers fill it with blast mix.</i>
<br>
<br><strong>2. The Cowper Stove — hot blast</strong> <br>A raw blast furnace
tops out around 1400 °C, which is not enough. To push past that you reclaim the
heat it throws away. A <a href=\"handbooksearch://cowper stove intake\">Cowper
Stove</a> is a regenerator: pipe the furnace's <strong>Exhaust</strong> into it
and it soaks that heat into its brick core (\"charging\"); burning a coal pile
beneath it charges it faster, with anthracite fastest. Be ware, that burning low
grades of coal results in production of exhaust from the cowper stove main
output! <br>Once charged, route plain <strong>Blast</strong> (air under
pressure) through the same stove and it pours back out scorching hot. Feed that
blast into the furnace tuyeres and the internal temperature jumps toward ~1700
°C, melting iron far faster. <br>
<br>The catch: a single stove can only do one job at a time, and a charged core
runs down as you draw blast from it. The intended setup is therefore <strong>two
cowper stoves working as a pair</strong>, cycling between themselves — while one
<strong>charges</strong> on exhaust, the other <strong>discharges</strong> hot
blast into the furnace; when the discharging one cools, swap their roles (with
valves) so the freshly charged stove takes over. Alternating like this keeps a
steady stream of hot blast on the tuyeres without ever stalling. Keep exhaust and
air on their separate paths — if they mix inside a stove it cannot heat.
<br>
<br><strong>Blocks required (per stove — you want two):</strong>
<br>• 48 × Refractory Bricks (tier 3)
<br>• 1 × Cowper Stove Intake (the control block)
<br>• 4 × Heat Sink
<br>• 2 × Gas Outlet
<br>• 1 × Gas Passthrough
<br>• 1 × Iron Hatch Door
<br><i>One interior slot is left open for the coal pile that charges the core.</i>
<br>
<br><strong>3. The Gas Network</strong>
<br>Everything above breathes through a network of <a href=\"handbooksearch://gas
piping\">gas pipes</a>. Gas comes in three kinds: ordinary <strong>Air</strong>,
hot <strong>Exhaust</strong> spilling from the furnace and converter, and forced
<strong>Blast</strong> (pressurised air). Straight pipes, bends, T- and
X-junctions carry it, and a few special blocks shape the flow:
<br>• <a href=\"handbooksearch://gas intake\">Intakes</a> draw fresh Air into the
network from the open world.
<br>• <a href=\"handbooksearch://gas blower\">Blowers</a> are powered by an axle
and pressurise Air into <strong>Blast</strong> — this is what forces gas through
the cowper stove and into the tuyeres.
<br>• <a href=\"handbooksearch://gas valve\">Valves</a> open and close a line by
hand, letting you redirect flow — essential for swapping the two cowper stoves
between charge and discharge.
<br>• <a href=\"handbooksearch://pressure valve\">Pressure valves</a> only open
once the gas behind them builds enough pressure, regulating a line automatically.
<br>• <a href=\"handbooksearch://gas outlet\">Outlets</a> connect the network to a
structure's face (a furnace tuyere, a stove, the converter intake).
<br>Each network tracks its gas type, volume and temperature; an open-ended pipe
<strong>leaks</strong>.
<br>
<br><strong>4. The Smoke Stack</strong>
<br>A blast furnace produces more exhaust than the cowper stoves can swallow,
especially when only one is charging. The <a href=\"handbooksearch://smoke stack
intake\">Smoke Stack</a> vents the surplus to the sky, keeping the gas network
from backing up — a backed-up exhaust line chokes and extinguishes the furnace,
so treat the smoke stack as a safety valve for the whole system.
<br>
<br><strong>Blocks required:</strong>
<br>• 1 × Smoke Stack Intake (the control block)
<br>• 60 × Fire Clay Bricks
<br>
<br><strong>5. Molten Canals</strong>
<br>Liquid metal is plumbed, not carried. Lay a <a href=\"handbooksearch://molten
canal start\">Molten Canal (Start)</a> as the anchor of a network, then run <a
href=\"handbooksearch://molten canal straight\">straights</a>, bends and
junctions from it. Drain the furnace with its <a href=\"handbooksearch://molten
metal tap\">Molten Metal Taps</a> — the lower tap pours iron, the upper tap pours
slag — each into the start of a canal below it; toggle a tap with
<hk>rightmouse</hk>. A <a href=\"handbooksearch://molten canal tap\">Canal Tap</a>
at the far end pours the metal back out.
<br>
<br><strong>Sealing — a manual valve.</strong> A <a href=\"handbooksearch://molten
canal straight\">straight canal</a> can be turned into a shut-off. <hk>rightmouse</hk>
an empty straight with <strong>4 fire clay</strong> and it caps over on both ends,
<strong>severing the network</strong> at that block — flow stops there and the two
sides become independent runs, so you can isolate a section without tearing it up.
You can only seal a canal that has been <strong>drained</strong> first; a canal with
metal still in it refuses. To reopen it, <hk>rightmouse</hk> the sealed block with a
<strong>chisel</strong> to chip the plug out and recover <strong>2 fire clay</strong>.
<br>
<br><strong>Mind the temperature.</strong> Metal sitting in the canals slowly
cools, and if it drops too far it <strong>solidifies in place</strong> — a
hardened canal stops flowing and must be broken to clear it (you recover most of
the metal as bits). Keep runs short, keep them fed, and empty them before they
or the furnace go cold. <br>
<br>A canal tap can pour into two destinations. A <a href=\"handbooksearch://mold
pedestal\">Mold Pedestal</a> casts the metal into shape: place a fired mold on it
and it fills from the network. This mod adds larger casting molds —
<strong>plate</strong>, <strong>quad rod</strong> and <strong>double ingot</strong>
— that you clay-form and fire like any tool mold. A freshly filled mold is
dangerously hot: holding one bare-handed will burn you (wear heavy leather or
blacksmith's gloves), and a still-molten mold will spill if you pick it up or
rack it before it sets.
<br>
<br>A <a href=\"handbooksearch://molten barrel\">Molten Barrel</a> is the other
destination — bulk storage for liquid metal. Use it as a buffer for
<strong>excess iron</strong> the molds can't take fast enough, or to hold
<strong>slag</strong> drained off the top tap. Slag is not waste — solidified
slag grinds into <strong>powdered slag</strong>, which has two uses: mixed with
slaked lime in a barrel it yields <strong>mortar</strong> (the powder reacts with
the lime to set, so it stands in for the sand in an ordinary mortar mix), and
spread on farmland it acts as a <strong>phosphate fertilizer</strong> with a
lasting soil boost — a tidy way to turn a furnace byproduct into building stock
and crop yield.
<br>
<br><strong>Operating the furnace — the short version</strong>
<br>Once everything is built and piped, a production run goes:
<br>1. Stock the <strong>reinforced hopper</strong> with crushed iron ore,
crushed coke and lime, and enable dropping on the <strong>bell hopper</strong>.
<br>2. Let the shaft fill with blast mix, <strong>ignite</strong> it, and
<strong>close the door</strong>.
<br>3. Run a <strong>blower</strong> (powered by an axle) to push Air through a
<strong>charged cowper stove</strong> and into the tuyeres as hot blast; vent
spare exhaust through the <strong>smoke stack</strong>.
<br>4. Watch the temperature climb past ~1482 °C into the <strong>Melting</strong>
phase; alternate your two cowper stoves so the blast never goes cold.
<br>5. When iron and slag have pooled, open the <strong>molten metal taps</strong>
to drain them down the canals into <strong>molds</strong> (for ingots/plates) or
<strong>barrels</strong> (for storage and slag) — before anything solidifies.
<br>6. Keep the hopper supplied, the exhaust flowing and repeat.
<br>
<br>
<br><strong>════ STAGE II — STEEL PRODUCTION ════</strong>
<br>
<br><strong>The Bessemer Converter</strong>
<br>The <a href=\"handbooksearch://bessemer converter\">Bessemer Converter</a>
takes the molten iron your furnace produces and blows it into <strong>steel</strong>.
It is the most demanding machine in the mod — it costs a lot of materials, draws
both mechanical power and a Blast gas supply, and only works as the final stage of
an already-running molten line. <strong>Do not attempt it until your iron tier is
established</strong> and reliably filling canals; the converter is fed by that
line, not a replacement for it.
<br>
<br><strong>Building it.</strong> The converter is a 3×3×3 vessel raised through
right-click construction rather than placed in one piece. Place its
<strong>control block</strong> first, then <hk>rightmouse</hk> it with <strong>4
rusty gears and 12 iron or steel rods</strong> in your hotbar to start the vessel,
and build up the construction stages until it reports complete. Around the vessel
sit its service ports: a <strong>gas intake</strong>, a mechanical
<strong>transmission</strong>, an <strong>input tap</strong>, and an output.
<br>
<br><strong>Blocks required:</strong>
<br>• 1 × Bessemer Control (the control block, placed first)
<br>• 1 × Bessemer Converter vessel — raised in place from <strong>4 rusty gears + 12 iron or steel rods</strong>
<br>• 1 × Bessemer Transmission
<br>• 1 × Bessemer Gas Intake
<br>• 1 × Molten Canal (Start)
<br>• 2 × Molten Canal (Straight)
<br>• 1 × Molten Canal (Tap)
<br>
<br><strong>Construction cost — raising the vessel.</strong> Placing the vessel costs
<strong>4 rusty gears + 12 iron/steel rods</strong>; then <hk>rightmouse</hk> it
through its build stages, each drawing materials from your hotbar. All stages
together consume:
<br>• 40 × Iron/Steel Plate
<br>• 42 × Iron/Steel Nails & Strips
<br>• 18 × Iron/Steel Rod
<br>• 84 × Refractory Brick (tier 3)
<br>• 84 × Fire Clay
<br>• 3 × Gas Piping (Straight)
<br>
<br><strong>Plugging it into the line.</strong> The converter needs two services
wired in from the systems you already built in Tier I:
<br>• <strong>Mechanical power</strong> — connect an axle to the converter's
transmission so the vessel can tilt and blow.
<br>• <strong>Blast</strong> — pipe a Blast gas supply (from a blower, the same
kind that feeds the furnace) into its <strong>gas intake</strong>. The intake
<strong>must face the same way as the control block</strong> or it won't couple.
<br>Then run a molten canal from a furnace tap into the converter's
<strong>input tap</strong>, and a second canal off its output toward your molds.
<br>
<br><strong>Operating it.</strong> With power and blast connected:
<br>1. Set the converter to <strong>Filling</strong> and pour molten iron into its
input tap from the canal until charged.
<br>2. Switch to <strong>Normal</strong>. With power and blast flowing, it blows
air through the charge at ~1800 °C for about five minutes, refining the iron into
<strong>steel</strong>.
<br>3. Set it to <strong>Pouring</strong> to drain the finished steel into the
output canal.
<br>From there the steel travels the molten network exactly like iron — pour it
through canals into <strong>mold pedestals</strong> to cast plates, rods and
ingots, or into a barrel to bank it.
<br>
<br>The same temperature rule applies inside the vessel: if the charge ever cools
and <strong>solidifies</strong> before you pour it, break the converter with a
steel pickaxe to recover the metal, then rebuild and try again. Keep the blast on
and the charge moving, and the Bessemer will turn out steel as fast as your iron
line can feed it.
