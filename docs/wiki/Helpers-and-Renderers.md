# Helpers & Renderers

A grab-bag of static helpers in `Helpers/` and a renderer base in `Renderers/`, shared across the
family so you don't rebuild rotation math, particle/sound catalogues, inventory counting or fluid
surfaces inline. Everything here is reusable from a consuming mod.

## `ExOrientation` - rotation math

Single source of truth for horizontal rotation. Use these instead of hand-rolled angle switches.

```csharp
public static class ExOrientation
{
    public static int AngleFromSide(string? side);                 // north 0, west 90, south 180, east 270
    public static Vec3i RotateOffset(Vec3i off, int angle);        // rotate a structure-local offset (Y untouched)
    public static Vec3i RotateOffset(int x, int y, int z, int angle);
    public static BlockPos GlobalPos(BlockPos origin, int localX, int localY, int localZ, int angle);
    public static Vec3i ReadOffset(JsonObject? node, Vec3i fallback);     // read { x, y, z } from JSON
    public static Vec3d ReadOffsetD(JsonObject? node, Vec3d fallback);    // double-precision (particle anchors)
    public static BlockPos WorldPosFromAttr(BlockPos origin, JsonObject? node, Vec3i fallback, int angle);
    public static Cuboidf[] RotateBoxes(Cuboidf[] boxes, int angle);     // rotate collision/selection boxes around centre
    public static BlockFacing RotateFacing(BlockFacing baseFace, int angle);
    public static void RotateAroundCenter(ref float x, ref float z, int angle, float center = 0.5f);
}
```

`WorldPosFromAttr` is the canonical "JSON offset -> world position": `origin + RotateOffset(ReadOffset(node), angle)`.
`RotateBoxes` returns copies (unchanged for angle 0), so cache the result rather than recomputing
each frame. `AllowedOrientations`-style lookups should likewise be cached props.

## `MPAnim` - phase-locking to a mechanical axle

Frame math for a mega-block part (a rotor, a gear, a piston) that must turn *in step with* an axle
rather than merely at a proportional speed. Read the mechanical network's rotation angle each render
frame - `BEBehaviorMPFillerPort.CurrentAngleRad` for a
[filler-hosted port](Multiblock-Structures) - and derive the animation frame from it.

```csharp
public static class MPAnim
{
    public static float AdvanceFrame(float currentFrame, float lastAngleRad, float angleRad, int totalFrames);
    public static float FrameFromAngle(float angleRad, int totalFrames);
}
```

`FrameFromAngle` maps the absolute angle onto the frame, so the part stays aligned to the axle - use
it for anything whose orientation is visible (a gear, a rotor). `AdvanceFrame` integrates the signed
delta instead, which is what you want when only rate and direction matter (an oscillating beam or
piston) and reversing the network should reverse the animation without a jump.

## `ExParticles` - particle catalogue

Named colour presets plus a configurable core and high-level effect helpers - don't build
`SimpleParticleProperties` inline.

```csharp
public static class ExParticles
{
    // Colour presets (int ARGB):
    public static readonly int Vapor, Exhaust, GasLeakTint, Water, Smoke, GlowSpark, Dust, AirTint;

    public static void Spawn(IWorldAccessor world, int color, Vec3d minPos, Vec3d maxPos,
        Vec3f minVelocity, Vec3f maxVelocity, float minQuantity, float maxQuantity,
        float lifeLength, float gravityEffect, float minSize, float maxSize,
        EnumParticleModel model = EnumParticleModel.Quad,
        EvolvingNatFloat? opacityEvolve = null, EvolvingNatFloat? sizeEvolve = null,
        bool shouldDieInLiquid = false);

    public static Vec3d FaceCenter(BlockPos pos, BlockFacing face);
    public static Vec3f OutVel(BlockFacing face, float speed, float spread);
    public static int? GasColor(string gasType, bool ventAir = true);

    // High-level effects:
    public static void RisingPlume(/* box bounds + timing */);
    public static void ChimneySmoke(IWorldAccessor world, BlockPos chimneyPos, string gasType);
    public static void SteamPlume(IWorldAccessor world, BlockPos cell, int count);
    public static void SteamPuff(IWorldAccessor world, Vec3d pos, int count);
    public static void AirInhale(IWorldAccessor world, Vec3d mouth, int count);
    public static void SmokeCloud(IWorldAccessor world, Vec3d pos, int count);
    public static void GasLeak(IWorldAccessor world, BlockPos pos, BlockFacing face, float intensity = 1f);
    public static void GasVent(IWorldAccessor world, BlockPos pos, BlockFacing face, string gasType);
    public static void WaterJet(IWorldAccessor world, BlockPos pos, BlockFacing face, float intensity = 1f);
    public static void WaterSpill(IWorldAccessor world, BlockPos cell);
    public static void FallingDust(IWorldAccessor world, BlockPos pos);
}
```

## `ExSounds` - sound catalogue

`AssetLocation` constants for every sound the family reuses (many repurposed vanilla sounds), plus
play helpers with the side-gating done right.

```csharp
public static class ExSounds
{
    // Constants (AssetLocation), grouped: molten/heat (Sizzle, MoltenMetal, PourMetal, Embers, Fire,
    // Extinguish, Ignite), mechanical (Latch, Bellows, Ingot, AnvilHit, Build, StoneCrush, ToggleSwitch,
    // CokeOvenDoorOpen/Close, ...), fluids/venting (SmallSplash, WaterPour, Watering, ExtinguishHiss),
    // steam ambience (Cooking, Lava, Creek, MetalGrinding, Swoosh, PlanetaryGears, *Explosion, ...).

    public static void Play(ICoreAPI? api, BlockPos pos, AssetLocation sound, float volume = 1f, float range = 24f);  // server only
    public static void PlayThrottled(ICoreAPI? api, BlockPos pos, AssetLocation sound, ref long lastMs, long intervalMs, float volume = 1f, float range = 24f);
    public static void PlayLocal(IWorldAccessor world, BlockPos pos, AssetLocation sound, float volume = 1f, float range = 16f, bool randomizePitch = true);  // client-safe, no side gate
    public static void PlayLoop(IWorldAccessor world, BlockPos pos, AssetLocation sound, ref long lastMs, long intervalMs, float volume = 1f, float range = 16f);  // client-safe
    public static void PlayAt(IWorldAccessor world, BlockPos pos, AssetLocation sound, IPlayer? byPlayer = null, bool randomizePitch = true, float range = 32f, float volume = 1f);
    public static void PlayChance(IWorldAccessor world, BlockPos pos, AssetLocation sound, double chance, bool randomizePitch = true, float range = 32f, float volume = 1f);
    public static ILoadedSound? CreateLoop(ICoreAPI? api, BlockPos pos, AssetLocation sound, float volume = 1f, float range = 16f, float pitch = 1f);  // client only, gapless loop
    public static void SplashSound(IWorldAccessor world, BlockPos pos);   // quiet splash ~30% of the time
    public static void HissSound(IWorldAccessor world, BlockPos pos);     // soft steam/gas hiss ~30% of the time
}
```

`Play*` (server) vs `PlayLocal`/`PlayLoop` (no side gate, client-safe) is the distinction to get
right; `CreateLoop` returns the `ILoadedSound` for you to start/stop/dispose.

## `ExInventory` - counting & consuming items

```csharp
public static class ExInventory
{
    public static int Count(IPlayer player, Func<ItemStack, bool> matches);            // all inventories
    public static int Take(IPlayer player, Func<ItemStack, bool> matches, int quantity);
    public static int CountHotbar(IPlayer player, Func<ItemStack, bool> matches);      // hotbar only
    public static int TakeHotbar(IPlayer player, Func<ItemStack, bool> matches, int quantity);
}
```

`Take`/`TakeHotbar` remove up to `quantity` matching items and return how many were actually taken
- useful for machine build/operation costs.

## `ExItems`, `ExCreativeTabs`, `ExBlockNames`

```csharp
public static class ExItems
{
    public static ItemStack[] WrenchStacks(IWorldAccessor world);   // one stack per registered wrench, cached, for interaction help
}

public static class ExCreativeTabs
{
    public static void EnsureTab(string tabCode);   // append a custom creative tab (reflection; no-op if internal type absent)
}

public static class ExBlockNames
{
    public static string Decorate(Block block, string baseName);    // append material/rock/brick variant + refractory tier
}
```

## `ExContentGate` - disabling content

Config-gated "turn this content off": hide collectibles from creative + handbook and strip their
recipes. Each method returns how many it affected.

```csharp
public static class ExContentGate
{
    public static int HideFromCreativeAndHandbook(ICoreAPI api, Func<CollectibleObject, bool> match);
    public static int RemoveClayformingRecipes(ICoreAPI api, Func<AssetLocation, bool> outputMatch);
    public static int RemoveGridRecipes(ICoreAPI api, Func<AssetLocation, bool> outputMatch);
}
```

Clearing a block's creative tabs and stacks also removes it from the handbook, so
`HideFromCreativeAndHandbook` does both. Pair it with a config toggle for "disable X" features
(e.g. `smex` tool-mold gating behind `/exmod molds`).

> **Tabs go empty, stacks go null.** `CreativeInventoryTabs` is set to an empty array, never null:
> the asset loader always leaves it an array and game code relies on that -
> `AttachableInteractionHelp` reads `.Length` on it with no null guard while scanning every
> collectible, so a null crashes the client the moment a player looks at any attachable entity.
> `CreativeInventoryStacks` is nulled instead, because null is the loader's own default for a
> collectible that declares no stacks; an empty array there would read as "has stacks" to the same
> non-null tests. Both hide the collectible, since the handbook tests `Length`, not nullity.

## `SurfaceRenderer` - flat fluid surfaces

`Renderers/SurfaceRenderer` is an `IRenderer` base for drawing a flat, textured horizontal surface
(a liquid line) inside a block - water tanks, molten canals. It owns the quad geometry built from
footprint boxes and the standard-shader plumbing; subclasses supply tint, height and texture.

```csharp
public abstract class SurfaceRenderer : IRenderer
{
    protected SurfaceRenderer(BlockPos pos, ICoreClientAPI api, Cuboidf[] footprintBoxes, float rotationY, bool combine);

    public abstract double RenderOrder { get; }
    public virtual int RenderRange => 24;

    // Implement:
    protected abstract bool ShouldRender { get; }                     // draw this frame?
    protected abstract float SurfaceY { get; }                        // absolute surface height in block units
    protected abstract void ConfigureShader(IStandardShaderProgram shader, IRenderAPI render);  // tint/glow
    protected abstract bool BindSurfaceTexture(IRenderAPI render);    // bind texture; false to skip drawing

    // Optionally override:
    protected virtual int SelectMeshIndex() => 0;                     // which box-mesh to draw (combine == false)
    protected virtual bool UseBlend => false;                         // alpha blending for translucent surfaces

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage);
    public virtual void Dispose();
}
```

`combine: true` merges all footprint boxes into one always-drawn mesh; `combine: false` keeps one
mesh per box and lets `SelectMeshIndex` pick the cross-section by fill level.

> **Re-init on `OnExchanged`.** `rotationY` is captured at construction. A wrench-rotated block
> renders its fluid surface in the pre-rotation orientation unless you rebuild the renderer in the
> block entity's `OnExchanged`. Surface glow is push-based, so a hot mold/tap/barrel also needs a
> client tick calling its update path or it freezes hot and snaps cold on interaction.

## Related pages

- [Multiblock Structures](Multiblock-Structures) - `ExOrientation` drives `SetStructureAngle`; `BEBehaviorMPFillerPort` supplies the angle `MPAnim` consumes.
- [Config System](Config-System) - back `ExContentGate` toggles with a live config value.
