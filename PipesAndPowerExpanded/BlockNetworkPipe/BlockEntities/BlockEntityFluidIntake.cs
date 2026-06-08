using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// A pipe node that draws water from the world: it scans the 3×3×3 cube directly
/// below itself and, when every cell is a water block, reports <see cref="HasWater"/>
/// so a connected <c>BlockEntityCornishFluidPump</c> may pump water into the network.
/// </summary>
[EntityRegister]
public class BlockEntityFluidIntake : BlockEntityNetworkNode
{
  public override string NetworkType { get; set; } = "pipe";

  private long _scanId;

  /// <summary>True when the 3×3×3 cube below the intake is fully water.</summary>
  public bool HasWater { get; private set; }

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      _scanId = RegisterGameTickListener(ScanForWater, 2000);
  }

  private void ScanForWater(float dt)
  {
    var ba = Api.World.BlockAccessor;
    bool all = true;
    for (int dx = -1; dx <= 1 && all; dx++)
    for (int dy = -1; dy >= -3 && all; dy--)
    for (int dz = -1; dz <= 1 && all; dz++)
    {
      Block b = ba.GetBlock(Pos.AddCopy(dx, dy, dz), BlockLayersAccess.Fluid);
      if (b.LiquidCode != "water")
        all = false;
    }
    HasWater = all;
  }

  public override void OnBlockRemoved()
  {
    if (_scanId != 0)
      UnregisterGameTickListener(_scanId);
    base.OnBlockRemoved();
  }
}
