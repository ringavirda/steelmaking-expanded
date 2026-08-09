using ExpandedLib.Renderers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Draws the translucent water surface inside a steam boiler: one quad per footprint box at the
/// block-height in <see cref="SurfaceLevel"/>, tinted toward a faint glow as
/// <see cref="Temperature"/> nears boiling, blended see-through like vanilla barrel water. The
/// boiler drives the level in discrete steps (hidden/low/high), so the flat quad always lands at
/// a sensible height inside the vessel rather than slicing through the geometry.
/// </summary>
public class BoilerWaterRenderer : SurfaceRenderer {
  private readonly int _textureId;

  /// <summary>
  /// Absolute surface height in block units above the boiler's base cell. A value of
  /// <c>0</c> (or less) hides the surface entirely.
  /// </summary>
  public float SurfaceLevel;

  /// <summary>Water temperature (°C); drives the faint hot-water glow.</summary>
  public float Temperature;

  // Must render AFTER the animated geometry (RenderOrder 1.0): drawing the translucent surface
  // first would write depth and cull the interior below the water line. Drawing last blends over it.
  public override double RenderOrder => 1.5;

  /// <param name="footprintBoxes">Surface footprint boxes in 0-16 pixel space.</param>
  /// <param name="rotationY">Y rotation (radians) matching the block's visual shape.</param>
  public BoilerWaterRenderer(
    BlockPos pos,
    ICoreClientAPI api,
    Cuboidf[] footprintBoxes,
    float rotationY
  )
    : base(pos, api, footprintBoxes, rotationY, combine: true) {
    _textureId = api.Render.GetOrLoadTexture(
      new AssetLocation("game:textures/block/liquid/water.png")
    );
  }

  protected override bool ShouldRender => SurfaceLevel > 0f && _textureId != 0;

  protected override float SurfaceY => SurfaceLevel;

  protected override bool UseBlend => true;

  protected override void ConfigureShader(
    IStandardShaderProgram shader,
    IRenderAPI render
  ) {
    // Translucent blue tint - see-through like vanilla barrel water; what shows through is the
    // vessel interior, not the world below.
    shader.RgbaTint = new Vec4f(0.55f, 0.7f, 0.95f, 0.7f);
    shader.TempGlowMode = 0;

    // A faint glow grows as the water nears/passes the boiling point.
    int glow = (int)GameMath.Clamp((Temperature - 60f) / 4f, 0f, 80f);
    shader.RgbaGlowIn = new Vec4f(0.6f, 0.7f, 0.95f, glow / 255f);
    shader.ExtraGlow = glow;
  }

  protected override bool BindSurfaceTexture(IRenderAPI render) {
    render.BindTexture2d(_textureId);
    return true;
  }
}
