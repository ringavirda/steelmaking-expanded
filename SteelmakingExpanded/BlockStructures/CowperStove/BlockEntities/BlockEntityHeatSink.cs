using System.Text;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.CowperStove.BlockEntities;

/// <summary>
/// Block entity for the cowper-stove heat sink. Holds the regenerator temperature
/// pushed in by the stove and renders an incandescent glow above ~500 °C.
/// </summary>
[EntityRegister]
public class BlockEntityHeatSink : BlockEntity
{
  private float _temperature = 20f;

  /// <summary>Current heat-sink temperature (°C); changing it re-lights the block when the glow level shifts.</summary>
  public float Temperature
  {
    get => _temperature;
    set
    {
      byte oldLight = GetLightLevel(_temperature);
      byte newLight = GetLightLevel(value);
      _temperature = value;

      if (oldLight != newLight && Api != null)
      {
        Api.World.BlockAccessor.MarkBlockDirty(Pos);
      }
    }
  }

  // The shared incandescence scale (canals, barrels, heat sink all glow alike).
  private static byte GetLightLevel(float temp) =>
    BlockNetworkMolten.MoltenMetal.GlowLevel(temp);

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetFloat("temperature", Temperature);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);

    float newTemp = tree.GetFloat("temperature");
    byte oldLight = GetLightLevel(_temperature);
    byte newLight = GetLightLevel(newTemp);
    _temperature = newTemp;

    if (oldLight != newLight && Api?.Side == EnumAppSide.Client)
    {
      Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(Lang.Get("smex:heatsink-info-temp", Temperature));
  }

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  )
  {
    if (Temperature <= 500f)
      return false; // Let the engine render the block normally

    tesselator.TesselateBlock(Block, out MeshData mesh);

    float[] color = ColorUtil.GetIncandescenceColorAsColor4f((int)Temperature);
    byte r = (byte)(color[0] * 255f);
    byte g = (byte)(color[1] * 255f);
    byte b = (byte)(color[2] * 255f);

    int vertexCount = mesh.Rgba.Length / 4;
    for (int i = 0; i < vertexCount; i++)
    {
      mesh.Rgba[i * 4 + 0] = (byte)((mesh.Rgba[i * 4 + 0] * b) / 255); // Blue
      mesh.Rgba[i * 4 + 1] = (byte)((mesh.Rgba[i * 4 + 1] * g) / 255);
      mesh.Rgba[i * 4 + 2] = (byte)((mesh.Rgba[i * 4 + 2] * r) / 255); // Red
    }

    int glow = (int)GameMath.Clamp((Temperature - 500f) / 2f, 0, 255);
    for (int i = 0; i < mesh.Flags.Length; i++)
    {
      mesh.Flags[i] |= glow; // Forces the engine to bypass ambient occlusion/shadows!
    }

    mesher.AddMeshData(mesh);
    return true; // Tells the engine we handled the chunk drawing
  }
}
