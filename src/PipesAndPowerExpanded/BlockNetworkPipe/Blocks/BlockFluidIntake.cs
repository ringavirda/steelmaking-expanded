using System.Collections.Generic;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

[BlockRegister]
public partial class BlockFluidIntake : BlockNetworkNode {
  public override string NetworkType => "pipe";

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "fluidintake", ["s", "n", "e", "w"] } };

  protected override string GetFallbackOrientation(string? type) => "s";

  /// <summary>
  /// The intake rests on water in any horizontal facing, so the wrench must cycle all four
  /// facings rather than the one it snapped to. Opting in makes <c>GetWrenchOrientations</c>
  /// recompute the cycle on the fly.
  /// </summary>
  protected override bool IsFullCube => true;

  /// <summary>
  /// The intake may only be placed on top of a water block. The full functional check (whole cube
  /// below is water, no crowding) lives in <see cref="BlockEntities.BlockEntityFluidIntake"/>.
  /// </summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    Block below = world.BlockAccessor.GetBlock(
      blockSel.Position.DownCopy(),
      BlockLayersAccess.Fluid
    );
    if (below.LiquidCode != "water") {
      // Shown as Lang.Get("placefailure-" + code), so this must be a plain code with a
      // matching "game:placefailure-…" lang entry, not text.
      failureCode = "ppex-fluidintake-nowater";
      return false;
    }

    return base.TryPlaceBlock(
      world,
      byPlayer,
      itemstack,
      blockSel,
      ref failureCode
    );
  }

  /// <summary>
  /// The intake is a standalone source block resting on the water it pumps. Water is not an
  /// attachable surface, so the base self-break would wrongly destroy a freshly placed intake.
  /// Keep its orientation in sync but never self-break; losing the water just disables it.
  /// </summary>
  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  ) {
    if (Orientation == null)
      return;

    RecalculateAndSyncOrientations(world, pos);
  }
}
