using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Drop-in replacement for the vanilla <see cref="AnimatableRenderer"/> that samples
/// block light at <see cref="LightPos"/> rather than at the render origin.
/// <para>
/// The boiler is a single-cell mega-block rendered across its whole footprint through
/// one animator. Vanilla lights that entire mesh from the light at the block-entity's
/// own cell — which sits directly against the firebox. At night the firebox's warm
/// burning light is the dominant source there, so the whole vessel takes on a uniform
/// red tint. Pointing the light sample at a cell on the vessel body instead (away from
/// the firebox, exposed to the sky) restores the boiler's natural colour while leaving
/// the render origin, rotation and animation exactly as vanilla draws them.
/// </para>
/// <para>
/// This re-declares <see cref="IRenderer"/> so the engine dispatches to this
/// <see cref="OnRenderFrame"/> override; the body is a faithful copy of the base
/// renderer's, differing only in the one line that chooses the light cell.
/// </para>
/// </summary>
public class BoilerAnimatableRenderer(
  ICoreClientAPI capi,
  Vec3d pos,
  Vec3f rotationDeg,
  AnimatorBase animator,
  System.Collections.Generic.Dictionary<
    string,
    AnimationMetaData
  > activeAnimationsByAnimCode,
  MeshData meshdata,
  EnumRenderStage renderStage = EnumRenderStage.Opaque
)
  : AnimatableRenderer(
    capi,
    pos,
    rotationDeg,
    animator,
    activeAnimationsByAnimCode,
    meshdata,
    renderStage
  ),
    IRenderer
{
  /// <summary>World cell the mesh is lit from (defaults to the render origin).</summary>
  public Vec3d LightPos = pos;

  public new void OnRenderFrame(float dt, EnumRenderStage stage)
  {
    if (
      !ShouldRender
      || (
        mtmeshrefOpaque != null
        && (mtmeshrefOpaque.Disposed || !mtmeshrefOpaque.Initialized)
      )
    )
      return;

    bool oit = stage == EnumRenderStage.OIT;
    bool shadow =
      stage == EnumRenderStage.ShadowFar || stage == EnumRenderStage.ShadowNear;
    if (!oit)
      capi.Render.GLDepthMask(on: true);

    EntityPlayer player = capi.World.Player.Entity;
    Mat4f.Identity(ModelMat);
    Mat4f.Translate(
      ModelMat,
      ModelMat,
      (float)(pos.X - player.CameraPos.X),
      (float)(pos.Y - player.CameraPos.Y),
      (float)(pos.Z - player.CameraPos.Z)
    );
    if (CustomTransform != null)
    {
      Mat4f.Multiply(ModelMat, ModelMat, CustomTransform);
    }
    else
    {
      Mat4f.Translate(ModelMat, ModelMat, 0.5f, 0f, 0.5f);
      Mat4f.Scale(ModelMat, ModelMat, ScaleX, ScaleY, ScaleZ);
      Mat4f.RotateY(
        ModelMat,
        ModelMat,
        rotationDeg.Y * ((float)Math.PI / 180f)
      );
      Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0f, -0.5f);
    }

    IRenderAPI render = capi.Render;
    IShaderProgram currentActiveShader = render.CurrentActiveShader;
    currentActiveShader?.Stop();
    if (!oit)
      capi.Render.GlDisableCullFace();

    IShaderProgram engineShader = render.GetEngineShader(
      shadow
        ? EnumShaderProgram.Shadowmapentityanimated
        : (
          oit
            ? EnumShaderProgram.Entityanimated_Oit
            : EnumShaderProgram.Entityanimated
        )
    );
    engineShader.Use();

    // The only departure from vanilla: light the mesh from the vessel-body cell
    // instead of the firebox-adjacent render-origin cell.
    Vec4f light = LightAffected
      ? capi.World.BlockAccessor.GetLightRGBs(
        (int)LightPos.X,
        (int)LightPos.Y,
        (int)LightPos.Z
      )
      : ColorUtil.WhiteArgbVec;

    if (!oit)
      render.GlToggleBlend(blend: true);

    if (!shadow)
    {
      engineShader.Uniform("extraGlow", 0);
      engineShader.Uniform("rgbaAmbientIn", render.AmbientColor);
      engineShader.Uniform("rgbaFogIn", render.FogColor);
      engineShader.Uniform("fogMinIn", render.FogMin * FogAffectedness);
      engineShader.Uniform("fogDensityIn", render.FogDensity * FogAffectedness);
      engineShader.Uniform("rgbaLightIn", light);
      engineShader.Uniform("renderColor", renderColor);
      engineShader.Uniform("alphaTest", 0.1f);
      engineShader.UniformMatrix("modelMatrix", ModelMat);
      engineShader.UniformMatrix("viewMatrix", render.CameraMatrixOriginf);
      engineShader.Uniform("windWaveIntensity", 0f);
      engineShader.Uniform("glitchEffectStrength", 0f);
      engineShader.Uniform("frostAlpha", 0f);
      if (!StabilityAffected)
      {
        engineShader.Uniform("globalWarpIntensity", 0f);
        engineShader.Uniform("glitchWaviness", 0f);
      }
    }
    else
    {
      engineShader.UniformMatrix(
        "modelViewMatrix",
        Mat4f.Mul(new float[16], render.CurrentModelviewMatrix, ModelMat)
      );
    }

    engineShader.UniformMatrix(
      "projectionMatrix",
      render.CurrentProjectionMatrix
    );
    engineShader.Uniform("addRenderFlags", 0);
    engineShader
      .UBOs["Animation"]
      .Update(animator.Matrices, 0, animator.MaxJointId * 16 * 4);

    if (
      (stage == EnumRenderStage.Opaque || stage == EnumRenderStage.ShadowNear)
      && !backfaceCulling
      && !oit
    )
      capi.Render.GlDisableCullFace();

    capi.Render.RenderMultiTextureMesh(
      oit ? mtmeshrefTransparent : mtmeshrefOpaque,
      "entityTex"
    );

    if (
      (stage == EnumRenderStage.Opaque || stage == EnumRenderStage.ShadowNear)
      && !backfaceCulling
      && !oit
    )
      capi.Render.GlEnableCullFace();

    engineShader.Stop();
    currentActiveShader?.Use();
  }
}
