using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>
/// Renders the glowing liquid-metal surface inside canals, taps, pedestals and
/// barrels. Draws one quad per footprint box, raised by <see cref="FillRatio"/> and
/// glow-tinted by <see cref="Temperature"/>, using the current metal's texture.
/// </summary>
public class MoltenRenderer : IRenderer
{
  private readonly ICoreClientAPI _api;
  private readonly BlockPos _pos;

  // One uploaded quad per footprint box. fillQuadsByLevel may list one cross-section
  // PER fill level (e.g. the anvil mold's 10 levels); only the box for the current
  // fill level is drawn, so the surface matches the cavity at its current height
  // instead of the union of every level (which covered the whole block).
  private readonly MeshRef[] _meshRefs;
  private readonly float _rotationY;

  // Derived from block JSON attributes.
  private readonly float _fillStartY;
  private readonly float _fillHeightLevels;

  public Matrixf ModelMat = new();

  /// <summary>
  /// Fill ratio in [0, 1].  Set to 0 to hide the surface entirely.
  /// Replaces the old <c>FillHeight</c> field (which was confusingly named and
  /// stored the ratio, not an absolute height).
  /// </summary>
  public float FillRatio;

  /// <summary>Metal temperature (°C), drives the surface glow.</summary>
  public float Temperature;

  /// <summary>Optional explicit surface texture; usually derived from <see cref="MetalStack"/>.</summary>
  public AssetLocation? TextureName;

  /// <summary>The metal whose texture is drawn on the surface; <c>null</c> hides it.</summary>
  public ItemStack? MetalStack;

  public double RenderOrder => 0.5;
  public int RenderRange => 24;

  /// <summary>
  /// Creates a renderer whose liquid-surface footprint is defined by
  /// <paramref name="footprintBoxes"/>.  Coordinates in those boxes must be in
  /// 0-16 pixel space (same scale as block-model pixel coordinates), NOT 0-1
  /// relative space.
  /// </summary>
  /// <param name="fillStartY">
  /// The Y world-offset (in 0-1 block-height units) at which the liquid surface
  /// sits at fill ratio 0.  Defaults to 0.125 (= 2/16), which was the old
  /// hard-coded base.
  /// </param>
  /// <param name="fillHeightLevels">
  /// How many 1/16-unit steps the surface can rise as fill ratio goes from 0 → 1.
  /// Defaults to 12, which reproduces the old hard-coded range of 0.75 block-height.
  /// </param>
  public MoltenRenderer(
    BlockPos pos,
    ICoreClientAPI api,
    Cuboidf[] footprintBoxes,
    float rotationY = 0f,
    float fillStartY = 0.125f,
    float fillHeightLevels = 12
  )
  {
    _pos = pos;
    _api = api;
    _rotationY = rotationY;
    _fillStartY = fillStartY;
    _fillHeightLevels = fillHeightLevels - 0.01f;

    // Upload one quad per box so OnRenderFrame can pick the single level to draw.
    _meshRefs = new MeshRef[footprintBoxes.Length];
    for (int i = 0; i < footprintBoxes.Length; i++)
    {
      Cuboidf box = footprintBoxes[i];

      MeshData quad = QuadMeshUtil.GetQuad();
      quad.Rgba = new byte[16];
      quad.Rgba.Fill(byte.MaxValue);
      quad.Flags = new int[4];

      // UV mapped from the top-down projection of the box.
      // Coordinates are in 0-16 pixel space; dividing by 16 gives 0-1 UV range.
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

      // GetQuad() returns a ±1 unit square centred at the origin (total extent = 2 in
      // each axis).  To land the quad exactly on [X1/16 … X2/16] × [Z1/16 … Z2/16]:
      //
      //   Scale: divide by 32 (= 16 × 2) so the ±1 extent maps to ±(W/32), giving a
      //          total width of W/16 in block-local units.
      //   Translate: use the mid-point of the box ((X1+X2)/32) so the centred quad
      //              lands at the right position after scaling.
      //
      // (Matrixf chains are post-multiplied: the rightmost call is applied first to the
      // vertex — Scale is applied before RotateX, which is applied before Translate.)
      float[] matrix = new Matrixf()
        .Translate((box.X1 + box.X2) / 32f, 0f, (box.Z1 + box.Z2) / 32f)
        .RotateX((float)Math.PI / 2f)
        .Scale((box.X2 - box.X1) / 32f, (box.Z2 - box.Z1) / 32f, 1f)
        .Values;

      quad.MatrixTransform(matrix);
      _meshRefs[i] = api.Render.UploadMesh(quad);
    }
  }

  public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
  {
    if (FillRatio <= 0f || MetalStack == null || _meshRefs.Length == 0)
      return;

    IRenderAPI render = _api.Render;
    Vec3d camPos = _api.World.Player.Entity.CameraPos;

    render.GlDisableCullFace();

    IStandardShaderProgram shader = render.StandardShader;
    shader.Use();

    shader.RgbaAmbientIn = render.AmbientColor;
    shader.RgbaFogIn = render.FogColor;
    shader.FogMinIn = render.FogMin;
    shader.FogDensityIn = render.FogDensity;
    shader.RgbaTint = ColorUtil.WhiteArgbVec;
    shader.DontWarpVertices = 0;
    shader.AddRenderFlags = 0;
    shader.ExtraGodray = 0f;
    shader.NormalShaded = 0;

    shader.AverageColor = ColorUtil.ToRGBAVec4f(
      _api.BlockTextureAtlas.GetAverageColor(
        (
          MetalStack.Item?.FirstTexture
          ?? MetalStack.Block.FirstTextureInventory
        )
          .Baked
          .TextureSubId
      )
    );
    shader.TempGlowMode = 1;

    Vec4f lightRGBs = _api.World.BlockAccessor.GetLightRGBs(
      _pos.X,
      _pos.Y,
      _pos.Z
    );
    float[] incandesence = ColorUtil.GetIncandescenceColorAsColor4f(
      (int)Temperature
    );
    int glowLevel = (int)GameMath.Clamp((Temperature - 550f) / 2f, 0f, 255f);

    shader.RgbaLightIn = lightRGBs;
    shader.RgbaGlowIn = new Vec4f(
      incandesence[0],
      incandesence[1],
      incandesence[2],
      glowLevel / 255f
    );
    shader.ExtraGlow = glowLevel;

    // Resolve texture from metal stack, falling back to lava.
    var firstTex =
      MetalStack.Item?.FirstTexture ?? MetalStack.Block?.FirstTextureInventory;
    if (firstTex != null)
    {
      TextureName = firstTex
        .Base.Clone()
        .WithPathPrefixOnce("textures/")
        .WithPathAppendixOnce(".png");
    }

    if (TextureName != null)
    {
      int texId = render.GetOrLoadTexture(TextureName);
      if (texId == 0)
        return; // or bind a known-good fallback
      render.BindTexture2d(texId);
    }
    else
      return;

    // Y offset: start from the trough floor (fillStartY) and rise by one 1/16-unit step
    // per fill-ratio unit, up to fillHeightLevels steps at ratio = 1.
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

    // Draw only the cross-section at the current fill level. Single-box footprints
    // (canals, barrels, simple molds) always resolve to box 0; multi-level molds
    // (anvil) show the cavity shape at the surface instead of every level at once.
    int level = (int)(FillRatio * _meshRefs.Length);
    level = GameMath.Clamp(level, 0, _meshRefs.Length - 1);
    render.RenderMesh(_meshRefs[level]);
    shader.Stop();

    render.GlEnableCullFace();
  }

  public void Dispose()
  {
    _api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    foreach (MeshRef meshRef in _meshRefs)
      meshRef?.Dispose();
  }
}
