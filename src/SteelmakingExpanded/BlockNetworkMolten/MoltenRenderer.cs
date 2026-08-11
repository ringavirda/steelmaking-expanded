using ExpandedLib.Renderers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>
/// Renders the glowing liquid-metal surface inside canals, taps, pedestals and
/// barrels. Draws one quad per footprint box, raised by <see cref="FillRatio"/> and
/// glow-tinted by <see cref="Temperature"/>, using the current metal's texture.
/// </summary>
public class MoltenRenderer : SurfaceRenderer {
  // Derived from block JSON attributes.
  private readonly float _fillStartY;
  private readonly float _fillHeightLevels;

  /// <summary>Fill ratio in [0, 1]; 0 hides the surface entirely.</summary>
  public float FillRatio;

  /// <summary>Metal temperature (°C), drives the surface glow.</summary>
  public float Temperature;

  /// <summary>Optional explicit surface texture; usually derived from <see cref="MetalStack"/>.</summary>
  public AssetLocation? TextureName;

  /// <summary>The metal whose texture is drawn on the surface; <c>null</c> hides it.</summary>
  public ItemStack? MetalStack;

  public override double RenderOrder => 0.5;

  /// <summary>
  /// Creates a renderer whose surface footprint is <paramref name="footprintBoxes"/> (in 0-16
  /// pixel space, NOT 0-1). <paramref name="fillStartY"/> is the surface Y at fill ratio 0;
  /// <paramref name="fillHeightLevels"/> is how many 1/16-unit steps it rises from 0 → 1.
  /// <para>
  /// The boxes are one footprint, not one cross-section per fill level: a molten cell holds a single
  /// pool, so every arm of a bend, tee or cross is one surface at one height and they are combined
  /// into one mesh. Selecting a box by fill level instead left a tee drawing its through-channel
  /// under half full and only its side stub over it.
  /// </para>
  /// </summary>
  public MoltenRenderer(
    BlockPos pos,
    ICoreClientAPI api,
    Cuboidf[] footprintBoxes,
    float rotationY = 0f,
    float fillStartY = 0.125f,
    float fillHeightLevels = 12
  )
    : base(pos, api, footprintBoxes, rotationY, combine: true) {
    _fillStartY = fillStartY;
    _fillHeightLevels = fillHeightLevels - 0.01f;
  }

  protected override bool ShouldRender => FillRatio > 0f && MetalStack != null;

  // Y offset: start from the trough floor (fillStartY) and rise by one 1/16-unit step
  // per fill-ratio unit, up to fillHeightLevels steps at ratio = 1.
  protected override float SurfaceY =>
    _fillStartY + FillRatio * _fillHeightLevels / 16f;

  protected override void ConfigureShader(
    IStandardShaderProgram shader,
    IRenderAPI render
  ) {
    shader.RgbaTint = ColorUtil.WhiteArgbVec;
    shader.AverageColor = ColorUtil.ToRGBAVec4f(
      Api.BlockTextureAtlas.GetAverageColor(
        (
          MetalStack!.Item?.FirstTexture
          ?? MetalStack.Block.FirstTextureInventory
        )
          .Baked
          .TextureSubId
      )
    );
    shader.TempGlowMode = 1;

    float[] incandesence = ColorUtil.GetIncandescenceColorAsColor4f(
      (int)Temperature
    );
    int glowLevel = (int)GameMath.Clamp((Temperature - 550f) / 2f, 0f, 255f);
    shader.RgbaGlowIn = new Vec4f(
      incandesence[0],
      incandesence[1],
      incandesence[2],
      glowLevel / 255f
    );
    shader.ExtraGlow = glowLevel;
  }

  protected override bool BindSurfaceTexture(IRenderAPI render) {
    // Resolve texture from metal stack, falling back to the last explicit TextureName.
    var firstTex =
      MetalStack!.Item?.FirstTexture ?? MetalStack.Block?.FirstTextureInventory;
    if (firstTex != null) {
      TextureName = firstTex
        .Base.Clone()
        .WithPathPrefixOnce("textures/")
        .WithPathAppendixOnce(".png");
    }

    if (TextureName == null)
      return false;

    int texId = render.GetOrLoadTexture(TextureName);
    if (texId == 0)
      return false;

    render.BindTexture2d(texId);
    return true;
  }
}
