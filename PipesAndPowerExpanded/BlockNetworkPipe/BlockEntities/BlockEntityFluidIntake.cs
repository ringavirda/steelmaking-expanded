using System.Text;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// A pipe node that draws water from the world. Once a second it scans the cube
/// directly below itself; it only feeds water into the network (<see cref="CanIntake"/>)
/// when that whole cube is water (the frozen pond skin on the top-layer edges is tolerated,
/// but not directly below the intake) AND no other intake sits within the exclusion range
/// — the latter stops players packing intakes onto one pond. The status is re-checked
/// on a timer so relocating intakes (or draining the pond) updates it live, and it is
/// surfaced in the block info HUD.
/// </summary>
[EntityRegister]
public class BlockEntityFluidIntake : BlockEntityNetworkNode
{
  public override string NetworkType { get; set; } = "pipe";

  private long _scanId;

  /// <summary>True when the cube directly below the intake is fully water.</summary>
  public bool HasWater { get; private set; }

  /// <summary>True when another fluid intake sits within the exclusion range.</summary>
  public bool Crowded { get; private set; }

  /// <summary>True when this intake may actually draw water right now.</summary>
  public bool CanIntake => HasWater && !Crowded;

  /// <summary>
  /// Draws up to <paramref name="amount"/> litres from the pond below and injects them
  /// into this intake's own network — the intake, not the pump, is the generator of
  /// water. Called by a powered fluid pump on the network below it; returns the litres
  /// actually produced (0 when the intake cannot draw, see <see cref="CanIntake"/>, or
  /// when its network is already full).
  /// </summary>
  public float ProduceWater(float amount, float temperature, IBlockAccessor ba)
  {
    if (!CanIntake || amount <= 0f)
      return 0f;
    if (NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork net)
      return 0f;

    // Gravity-fed source head; the output-side pressure is set by the pump's engine.
    return net.ProduceLiquidMeasured(amount, temperature, 1f, ba);
  }

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
    {
      Rescan(0);
      _scanId = RegisterGameTickListener(Rescan, 1000);
    }
  }

  /// <summary>Server-side periodic validity check; syncs to clients only on change.</summary>
  private void Rescan(float dt)
  {
    bool water = ScanWaterBelow();
    bool crowded = HasNearbyIntake();
    if (water != HasWater || crowded != Crowded)
    {
      HasWater = water;
      Crowded = crowded;
      MarkDirty();
    }
  }

  /// <summary>
  /// True when every cell of the <c>depth³</c> cube directly below is water, with one
  /// concession: the eight outer cells of the top layer may be frozen over (ice) and still
  /// count — a skin of lake ice on the pond surface doesn't stop the intake. The cell
  /// directly below the intake must stay liquid, so once it freezes the intake stops drawing.
  /// </summary>
  private bool ScanWaterBelow()
  {
    var ba = Api.World.BlockAccessor;
    int depth = PpexValues.FluidIntakeWaterDepth;
    int half = depth / 2;
    var p = new BlockPos(Pos.X, Pos.Y, Pos.Z, Pos.dimension);
    for (int dx = -half; dx <= half; dx++)
    for (int dy = -1; dy >= -depth; dy--)
    for (int dz = -half; dz <= half; dz++)
    {
      p.Set(Pos.X + dx, Pos.Y + dy, Pos.Z + dz);
      if (ba.GetBlock(p, BlockLayersAccess.Fluid).LiquidCode == "water")
        continue;
      // The top-layer outer cells may be frozen over; everything else (and the cell
      // directly below the intake) must be liquid water.
      bool topOuter = dy == -1 && (dx != 0 || dz != 0);
      if (topOuter && ba.GetBlock(p).BlockMaterial == EnumBlockMaterial.Ice)
        continue;
      return false;
    }
    return true;
  }

  /// <summary>True when another fluid intake sits within the exclusion range (excludes self).</summary>
  private bool HasNearbyIntake()
  {
    var ba = Api.World.BlockAccessor;
    int r = (int)System.Math.Ceiling(PpexValues.FluidIntakeExclusionRange);
    float rSq =
      PpexValues.FluidIntakeExclusionRange
      * PpexValues.FluidIntakeExclusionRange;
    var p = new BlockPos(Pos.X, Pos.Y, Pos.Z, Pos.dimension);
    for (int dx = -r; dx <= r; dx++)
    for (int dy = -r; dy <= r; dy++)
    for (int dz = -r; dz <= r; dz++)
    {
      if (dx == 0 && dy == 0 && dz == 0)
        continue;
      if (dx * dx + dy * dy + dz * dz > rSq)
        continue;
      p.Set(Pos.X + dx, Pos.Y + dy, Pos.Z + dz);
      if (ba.GetBlock(p) is BlockFluidIntake)
        return true;
    }
    return false;
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);

    if (Crowded)
      dsc.AppendLine(Lang.Get("ppex:fluidintake-info-crowded"));
    else if (!HasWater)
      dsc.AppendLine(Lang.Get("ppex:fluidintake-info-nowater"));
    else
      dsc.AppendLine(Lang.Get("ppex:fluidintake-info-active"));
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("intakeHasWater", HasWater);
    tree.SetBool("intakeCrowded", Crowded);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    HasWater = tree.GetBool("intakeHasWater");
    Crowded = tree.GetBool("intakeCrowded");
  }

  public override void OnBlockRemoved()
  {
    if (_scanId != 0)
      UnregisterGameTickListener(_scanId);
    base.OnBlockRemoved();
  }
}
