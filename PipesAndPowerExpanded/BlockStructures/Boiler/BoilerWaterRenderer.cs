using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Draws the translucent water surface inside a steam boiler. One quad per footprint
/// box is drawn at an absolute block-height set by <see cref="SurfaceLevel"/> and tinted
/// toward a faint glow as <see cref="Temperature"/> approaches the boiling point. The
/// surface is rendered see-through (blended, like vanilla barrel water) so a hot boiler
/// shows shimmering water through the open lid.
/// <para>
/// The level is driven by the boiler in discrete steps rather than a continuous fill
/// ratio — hidden when empty, low (below the flue tubes) while filling, and high (above
/// the flues) once the vessel holds enough water to operate — so the flat quad always
/// lands at a sensible height inside the vessel instead of slicing through the geometry.
/// </para>
/// </summary>
public class BoilerWaterRenderer : IRenderer
{
  private readonly ICoreClientAPI _api;
  private readonly BlockPos _pos;
  private readonly MeshRef _meshRef;
  private readonly float _rotationY;
  private readonly int _textureId;

  public Matrixf ModelMat = new();

  /// <summary>
  /// Absolute surface height in block units above the boiler's base cell. A value of
  /// <c>0</c> (or less) hides the surface entirely.
  /// </summary>
  public float SurfaceLevel;

  /// <summary>Water temperature (°C); drives the faint hot-water glow.</summary>
  public float Temperature;

  // Must render AFTER the boiler's animated geometry (AnimationUtil renders in the
  // Opaque stage at RenderOrder 1.0). If the translucent surface drew first it would
  // write depth and cull the flue tubes / vessel floor sitting below the water line,
  // so the camera saw straight through to the terrain. Drawing last lets the water
  // blend over that already-rendered interior instead.
  public double RenderOrder => 1.5;
  public int RenderRange => 24;

  /// <param name="footprintBoxes">Surface footprint boxes in 0-16 pixel space.</param>
  /// <param name="rotationY">Y rotation (radians) matching the block's visual shape.</param>
  public BoilerWaterRenderer(
    BlockPos pos,
    ICoreClientAPI api,
    Cuboidf[] footprintBoxes,
    float rotationY
  )
  {
    _pos = pos;
    _api = api;
    _rotationY = rotationY;

    MeshData combined = new(
      4 * footprintBoxes.Length,
      6 * footprintBoxes.Length
    );

    foreach (Cuboidf box in footprintBoxes)
    {
      MeshData quad = QuadMeshUtil.GetQuad();
      quad.Rgba = new byte[16];
      quad.Rgba.Fill(byte.MaxValue);
      quad.Flags = new int[4];

      // UV mapped from the top-down projection of the box (0-16 px → 0-1 UV).
      quad.Uv =
      [
        box.X2 / 16f,
        box.Z2 / 16f,
        box.X1 / 16f,
        box.Z2 / 16f,
        box.X1 / 16f,
        box.Z1 / 16f,
        box.X2 / 16f,
        box.Z1 / 16f,
      ];

      // GetQuad() is a ±1 unit square at the origin; scale + translate it onto the box.
      float[] matrix = new Matrixf()
        .Translate((box.X1 + box.X2) / 32f, 0f, (box.Z1 + box.Z2) / 32f)
        .RotateX((float)Math.PI / 2f)
        .Scale((box.X2 - box.X1) / 32f, (box.Z2 - box.Z1) / 32f, 1f)
        .Values;

      quad.MatrixTransform(matrix);
      combined.AddMeshData(quad);
    }

    _meshRef = api.Render.UploadMesh(combined);
    _textureId = api.Render.GetOrLoadTexture(
      new AssetLocation("game:textures/block/liquid/water.png")
    );
  }

  public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
  {
    if (SurfaceLevel <= 0f || _textureId == 0)
      return;

    IRenderAPI render = _api.Render;
    Vec3d camPos = _api.World.Player.Entity.CameraPos;

    render.GlDisableCullFace();
    render.GlToggleBlend(true);

    IStandardShaderProgram shader = render.StandardShader;
    shader.Use();

    shader.RgbaAmbientIn = render.AmbientColor;
    shader.RgbaFogIn = render.FogColor;
    shader.FogMinIn = render.FogMin;
    shader.FogDensityIn = render.FogDensity;
    // Translucent blue tint over the water texture — see-through like vanilla barrel
    // water. The boiler keeps SurfaceLevel at a height that sits inside the vessel, so
    // what shows through the water is the vessel interior, not the world below.
    shader.RgbaTint = new Vec4f(0.55f, 0.7f, 0.95f, 0.7f);
    shader.DontWarpVertices = 0;
    shader.AddRenderFlags = 0;
    shader.ExtraGodray = 0f;
    shader.NormalShaded = 0;
    shader.TempGlowMode = 0;

    shader.RgbaLightIn = _api.World.BlockAccessor.GetLightRGBs(
      _pos.X,
      _pos.Y,
      _pos.Z
    );

    // A faint glow grows as the water nears/passes the boiling point.
    int glow = (int)GameMath.Clamp((Temperature - 60f) / 4f, 0f, 80f);
    shader.RgbaGlowIn = new Vec4f(0.6f, 0.7f, 0.95f, glow / 255f);
    shader.ExtraGlow = glow;

    render.BindTexture2d(_textureId);

    shader.ModelMatrix = ModelMat
      .Identity()
      .Translate(_pos.X - camPos.X, _pos.Y - camPos.Y, _pos.Z - camPos.Z)
      .Translate(0.5f, 0f, 0.5f)
      .RotateY(_rotationY)
      .Translate(-0.5f, 0f, -0.5f)
      .Translate(0f, SurfaceLevel, 0f)
      .Values;

    shader.ViewMatrix = render.CameraMatrixOriginf;
    shader.ProjectionMatrix = render.CurrentProjectionMatrix;

    render.RenderMesh(_meshRef);
    shader.Stop();

    render.GlToggleBlend(false);
    render.GlEnableCullFace();
  }

  public void Dispose()
  {
    _api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    _meshRef?.Dispose();
  }
}
