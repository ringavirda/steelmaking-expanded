using Vintagestory.API.Common;

namespace SteelmakingExpanded.Networks.Molten;

/// <summary>Defines a horizontal fill footprint (x/z extents) for the molten-surface renderer.</summary>
public struct FillQuadDef
{
  public float x1,
    z1,
    x2,
    z2;
}

/// <summary>Holds the current state of an entire molten-canal network.</summary>
public class MoltenNetworkState
{
  /// <summary>Liquid metal currently held by the network, in units.</summary>
  public float CurrentAmount { get; set; }

  /// <summary>Maximum units the network can hold (sum of canal-block capacities).</summary>
  public float MaxAmount { get; set; }

  /// <summary>Metal temperature (°C); updated each tick from <see cref="MetalStack"/> and used for broadcast/rendering.</summary>
  public float CurrentTemperature { get; set; }

  /// <summary>Full AssetLocation string of the metal, e.g. "game:ingot-iron"; empty when no metal.</summary>
  public string MetalType { get; set; } = "";

  /// <summary>Whether the metal has solidified, requiring the network to be rebuilt.</summary>
  public bool Solidified { get; set; }

  /// <summary>
  /// Carries VS time-based temperature. <c>null</c> until the first metal is pushed
  /// (or lazily reconstructed from <see cref="MetalType"/> + <see cref="CurrentTemperature"/> on world load).
  /// </summary>
  public ItemStack? MetalStack { get; set; }
}
