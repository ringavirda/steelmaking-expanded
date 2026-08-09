# Multiblock Structures

`Blocks/Structures/` provides everything a multi-cell machine needs: completion monitoring, a
build-outline projection (ctrl+shift+right-click), crash-safe incomplete-part highlighting, and a
shared invisible **filler** block that gives a single-cell mega-block real per-cell collision and
can carry ports and behaviours on the controller's behalf.

There are two independent tools here - use one or both:

1. **The filler system** - make one logical block occupy several world cells with solid
   collision/selection, while all interaction routes back to the controller block. A footprint cell
   can additionally expose a network port or host block entity behaviours (a mechanical-power
   intake, say) that the controller's own cell is in the wrong place for.
2. **`BlockEntityMultiblockStructure`** - a base block entity that monitors whether a designed
   multiblock pattern is complete, runs production only while complete, and shows a build outline
   of missing parts.

## The filler system

A "mega-block" is one block whose model spans more than its own cell. By default the engine only
gives it collision in its own cell. The filler system fixes that by placing invisible
`exlib:structurefiller` blocks in the other footprint cells.

### Declaring the footprint

Your controller block implements `IFillerHost` and declares its footprint cells in JSON via a
`fillerOffsets` attribute. The simplest implementation is to let the
[attribute generator](Source-Generators) surface the attribute:

```csharp
public interface IFillerHost
{
    JsonObject? FillerOffsets { get; }   // the block's `fillerOffsets` JSON node, or null
}
```

```jsonc
// in your blocktype attributes
"fillerOffsets": [
  { "x": 1, "y": 0, "z": 0, "allowAttach": true },
  { "x": 0, "y": 1, "z": 0 },
  {
    "x": 0, "y": 2, "z": 0,
    "behaviors": [
      {
        "code": "exlib.BEBehaviorMPFillerPort",
        "face": "east",
        "properties": { "resistance": 0.8 }
      }
    ]
  }
]
```

`allowAttach` (default `false`) controls whether other blocks may attach to that filler cell.
`behaviors` (optional) makes the cell host block entity behaviours on the controller's behalf -
see [Per-cell hosted behaviours](#per-cell-hosted-behaviours-optional).

### Placing and removing fillers

`StructureFillers` is the helper that resolves and manages footprint cells. Offsets are declared
in the block's north orientation and rotated to the placed angle for you.

```csharp
public static class StructureFillers
{
    public static AssetLocation FillerCode { get; set; }   // default "exlib:structurefiller"

    public static List<FillerOffset> ReadOffsets(JsonObject? offsetsNode);
    public static List<FillerCell> FootprintCells(IFillerHost principal, BlockPos principalPos, int angle);
    public static bool CanPlace(IWorldAccessor world, IEnumerable<FillerCell> cells);
    public static void PlaceFillers(IWorldAccessor world, BlockPos principalPos, IEnumerable<FillerCell> cells);
    public static void RemoveFillers(IWorldAccessor world, BlockPos principalPos, IEnumerable<FillerCell> cells);
}

public readonly record struct FillerOffset(Vec3i Offset, bool AllowAttach, FillerBehavior[]? Behaviors = null);
public readonly record struct FillerCell(BlockPos Pos, bool AllowAttach, FillerBehavior[]? Behaviors = null);
```

Typical flow in the controller block:

```csharp
// In TryPlaceBlock: bail if the footprint isn't clear.
var cells = StructureFillers.FootprintCells(this, blockSel.Position, placeAngle);
if (!StructureFillers.CanPlace(world, cells)) { failureCode = "notenoughspace"; return false; }
// ...place the controller block, then:
StructureFillers.PlaceFillers(world, blockSel.Position, cells);

// In OnBlockBroken: clear the fillers linked to this controller.
StructureFillers.RemoveFillers(world, pos, cells);
```

`PlaceFillers`/`RemoveFillers` are server-side; `RemoveFillers` only clears cells actually linked
to the given principal, so neighbouring mega-blocks are safe.

> **Per-cell collision gotcha.** The filler block must declare `sidesolid: true` (and a real
> collision box) for the engine to treat each cell as solid. Without it the mega-block has only
> single-cell collision regardless of fillers.

### Per-cell interactions (optional)

If clicking different footprint cells should do different things, the controller implements
`IFillerInteractionTarget`. The filler forwards the click to the controller with the clicked cell:

```csharp
public interface IFillerInteractionTarget
{
    bool OnFillerInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection principalSel, BlockPos clickedCell);
    bool OnFillerInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection principalSel, BlockPos clickedCell);
    void OnFillerInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection principalSel, BlockPos clickedCell);
    WorldInteraction[] GetFillerInteractionHelp(IWorldAccessor world, BlockSelection principalSel, IPlayer forPlayer, BlockPos clickedCell);
}
```

`BlockStructureFiller` / `BlockEntityStructureFiller` (the invisible block and its entity) reroute
break, pick, drops, sounds, HUD info and interaction help to the controller automatically; the BE
stores the `Principal` position link plus optional network-port config (`PortFace`,
`PortNetworkType`) so a filler cell can even expose a network connector on the controller's behalf.

### Per-cell hosted behaviours (optional)

A footprint cell can also carry real block entity behaviours. This is how a mega-block presents a
connector the controller block cannot: the controller occupies one cell, and a machine port often
has to sit two cells away. Declare them on the `fillerOffsets` entry:

```jsonc
{
  "x": 0, "y": 1, "z": 0,
  "behaviors": [
    {
      "code": "exlib.BEBehaviorMPFillerPort",   // registered BE-behaviour class code
      "face": "east",                            // optional, in the block's NORTH orientation
      "properties": { "resistance": 0.8 }        // optional, passed to the behaviour
    }
  ]
}
```

```csharp
public readonly record struct FillerBehavior(string Code, BlockFacing? ConnectorFace, JsonObject? Properties);

public interface IFillerHostedBehavior
{
    void ConfigureFromFiller(BlockPos? principal, BlockFacing? connectorFace, JsonObject? properties);
}
```

`StructureFillers.FootprintCells` rotates each declared `face` into the placed orientation, and
`PlaceFillers` hands the array to `BlockEntityStructureFiller.SetHostedBehaviors` immediately after
the `Principal` link is set. The BE instantiates each behaviour by class code, calls
`ConfigureFromFiller` **before** the behaviour's own `Initialize` (so `SetOrientations` already sees
the right face), then adds and initialises it. The declarations are saved on the BE and the
instances recreated on load.

`face` takes either spelling - `"east"` or `"e"` - so a declaration can reuse the single-letter form
the network-port strings use. Omit it entirely for a behaviour that needs no connector.

> **Orientation comes from the principal.** `exlib:structurefiller` is one shared block with no
> variants, so its own facing is always north. A hosted behaviour that needs a direction must take
> it from `ConfigureFromFiller`, never from `Block.Variant`.

### `BEBehaviorMPFillerPort` - a mechanical-power intake on a filler cell

The one hosted behaviour exlib ships. It is a minimal `BEBehaviorMPBase` that joins the vanilla
mechanical-power network at its cell, renders nothing, and only loads the network with a
configurable resistance. The controller reads the resulting rotation back and drives its own parts.

```csharp
public class BEBehaviorMPFillerPort : BEBehaviorMPBase, IFillerHostedBehavior
{
    public const float DefaultResistance = 0.5f;   // override with the "resistance" property

    public BlockFacing PortFacing { get; }         // the coupling face, already rotated
    public bool  IsTurning { get; }                // network present and turning
    public float Speed { get; }                    // absolute network speed
    public bool  IsReversed { get; }               // true while the network runs negative
    public float CurrentAngleRad { get; }          // for phase-locking an animation
}
```

`BlockStructureFiller` implements `IMechanicalPowerBlock` for this: a cell reports an axle connector
when it hosts an MP behaviour whose declared face matches the queried face **or its opposite** -
both ends of one axle line, so an axle can run straight through the cell and attach from either
side. The port itself also connects its opposite face on `Initialize`, so a row of ports merges into
a single network instead of fracturing it.

> **One `AxisSign` per axis, not per facing.** Opposite facings share an axle line, so signing them
> separately makes the two ends counter-rotate. The port derives its sign from `PortFacing.Axis`.

Read it from the controller's block entity like any other port:

```csharp
var port = Api.World.BlockAccessor
    .GetBlockEntity(block.MpPortWorldPos(Pos))
    ?.GetBehavior<BEBehaviorMPFillerPort>();
float speed = port is { IsTurning: true } ? port.Speed : 0f;
```

Machines scale their work by that speed between a minimum (below which they do nothing) and a
maximum (full rate) - `smex:mpblower` and `ppex:mpfluidpump` both do. To turn a driven part in step
with the axle rather than merely at the same rate, feed `CurrentAngleRad` to
[`MPAnim`](Helpers-and-Renderers).

## Completion monitoring: `BlockEntityMultiblockStructure`

For designed multiblock machines (blast furnace, cowper stove, bessemer control) subclass
`BlockEntityMultiblockStructure`. It extends [`BlockEntityProductionMachine`](Production-Machines),
adding a monitor tick that detects completion/breakage and gates production on it.

```csharp
public abstract class BlockEntityMultiblockStructure : BlockEntityProductionMachine
{
    public bool StructureComplete { get; protected set; }
    protected virtual int CompletionTickMs { get; }          // monitor interval, default 3000ms
    protected override bool CanRunProduction { get; }        // production runs only while complete
    protected virtual bool AutoStartProduction { get; }      // register production tick on load if already complete

    public virtual void Interact(IPlayer byPlayer);          // toggle the build-outline projection

    // You implement these:
    protected abstract void UpdateStructureRotation();
    protected abstract string GetIncompleteMessage(int missingCount);
    protected abstract string GetCompleteMessage();

    // Optional hooks:
    protected virtual void OnStructureLost();                // complete -> incomplete
    protected virtual void OnStructureCompleted();           // incomplete -> complete
    protected virtual BlockPos GetGlobalPos(int localX, int localY, int localZ);

    protected void SetStructureAngle(int angle, int initAngleOffset = 0);   // canonical UpdateStructureRotation body
}
```

A minimal subclass:

```csharp
[BlockEntityRegister]
public class BlockEntityBlastFurnace : BlockEntityMultiblockStructure
{
    protected override void UpdateStructureRotation()
        => SetStructureAngle(ExOrientation.AngleFromSide(Block.Variant["side"]));

    protected override string GetIncompleteMessage(int missingCount)
        => Lang.Get("smex:blastfurnace-incomplete", missingCount);

    protected override string GetCompleteMessage()
        => Lang.Get("smex:blastfurnace-complete");

    protected override void OnProductionTick(float dt) { /* smelt while complete */ }
}
```

### How completion is wired

The actual pattern (which cells must be which blocks) is a vanilla **`multiblockStructure`**
JSON definition referenced by your block. `SetStructureAngle` loads that JSON, calls the engine's
`InitForUse` at the right angle, and clears any stale projection - this is the canonical body for
`UpdateStructureRotation`. The monitor tick re-checks completeness on `CompletionTickMs` and fires
`OnStructureCompleted` / `OnStructureLost` on transitions.

> **`GetGlobalPos` / `_currentAngle` invariant.** The base `GetGlobalPos(angle)` is equivalent to
> `InitForUse(angle)`, so the angle you pass to `SetStructureAngle` must match the structure's
> `_currentAngle`. A mismatch shows up as the build outline appearing rotated 180°.

## The build-outline behaviour

`BlockBehaviorMultiblockStructure` is a `BlockBehavior` (not a base class) that centralises the
ctrl+shift+right-click toggle of the missing-block hologram. Add it to your block's behaviours,
**before** any other right-click consumer, and gate it on the structure being incomplete:

```jsonc
"behaviors": [ { "name": "MultiblockStructure" } ]
```

It calls back into the BE's `Interact`, which re-checks completeness, shows the build outline of
missing parts (or clears it on completion). The highlighting is a **crash-safe reimplementation**:
vanilla's `HighlightIncompleteParts` throws `IndexOutOfRange` when a wanted `blockNumbers` code
resolves to no block, so the base falls back to a neutral tint instead of crashing the client.

> **`blockNumbers` validity.** Every offset in your `multiblockStructure` definition needs a
> `blockNumbers` entry that resolves to at least one real block, or the build outline (and vanilla
> highlighting) misbehaves.

## Related pages

- [Production Machines](Production-Machines) - the tick lifecycle this builds on.
- [Block Networks](Block-Networks) - a structure that is also a network node must call `AddNode`/`RemoveNode` itself.
- [Helpers & Renderers](Helpers-and-Renderers) - `ExOrientation` for the rotation math; `MPAnim` for phase-locking a driven part to an axle; `SurfaceRenderer` for fluid surfaces.
