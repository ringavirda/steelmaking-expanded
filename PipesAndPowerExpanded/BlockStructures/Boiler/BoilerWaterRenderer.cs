using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Draws the translucent water surface inside the Lancashire boiler. One quad per
/// footprint box is raised by <see cref="FillRatio"/> (water level) and tinted toward
/// a faint glow as <see cref="Temperature"/> approaches the boiling point, so a hot
/// boiler shows shimmering water through the open lid. Modelled on
/// <see cref="Networks.Molten.MoltenRenderer"/> but blended (water is see-through and
/// has no metal stack / incandescence).
/// </summary>
public class BoilerWaterRenderer : IRenderer
{
  private readonly ICoreClientAPI _api;
  private readonly BlockPos _pos;
  private readonly MeshRef _meshRef;
  private readonly float _rotationY;
  private readonly float _fillStartY;
  private readonly float _fillHeightLevels;
  private readonly int _textureId;

  public Matrixf ModelMat = new();

  /// <summary>Fill ratio in [0, 1]; 0 hides the surface.</summary>
  public float FillRatio;

  /// <summary>Water temperature (°C); drives the faint hot-water glow.</summary>
  public float Temperature;

  public double RenderOrder => 0.5;
  public int RenderRange => 24;

  /// <param name="footprintBoxes">Surface footprint boxes in 0-16 pixel space.</param>
  /// <param name="rotationY">Y rotation (radians) matching the block's visual shape.</param>
  /// <param name="fillStartY">Block-height (0-1) of the surface at fill ratio 0.</param>
  /// <param name="fillHeightLevels">1/16-unit steps the surface rises from 0 → 1.</param>
  public BoilerWaterRenderer(
    BlockPos pos,
    ICoreClientAPI api,
    Cuboidf[] footprintBoxes,
    float rotationY,
    float fillStartY,
    float fillHeightLevels
  )
  {
    _pos = pos;
    _api = api;
    _rotationY = rotationY;
    _fillStartY = fillStartY;
    _fillHeightLevels = fillHeightLevels - 0.01f;

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
    if (FillRatio <= 0f || _textureId == 0)
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
    // Translucent blue tint over the water texture.
    shader.RgbaTint = new Vec4f(0.55f, 0.7f, 0.95f, 0.72f);
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

    float yLevel = _fillStartY + FillRatio * _fillHeightLevels / 16f;

    shader.ModelMatrix = ModelMat
      .Identity()
      .Translate(_pos.X - camPos.X, _pos.Y - camPos.Y, _pos.Z - camPos.Z)
      .Translate(0.5f, 0f, 0.5f)
      .RotateY(_rotationY)
      .Translate(-0.5f, 0f, -0.5f)
      .Translate(0f, yLevel, 0f)
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
    _api.Event.UnregisterRenderer(this, EnumRenderStage.Done);
    _meshRef?.Dispose();
  }
}
