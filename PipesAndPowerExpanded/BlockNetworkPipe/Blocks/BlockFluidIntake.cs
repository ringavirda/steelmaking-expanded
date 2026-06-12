using System.Collections.Generic;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

[EntityRegister]
public class BlockFluidIntake : BlockNetworkNode
{
  public override string NetworkType => "pipe";

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "fluidintake", ["s", "n", "e", "w"] } };

  protected override string GetFallbackOrientation(string? type) => "s";

  /// <summary>
  /// The intake rests on the water below in any horizontal facing, so the wrench
  /// must rotate it through the full topology-derived cycle (all four when
  /// standalone) rather than the single facing it snapped to at placement. Opting in
  /// here makes <c>GetWrenchOrientations</c> recompute the cycle on the fly.
  /// </summary>
  protected override bool IsFullCube => true;

  /// <summary>
  /// The intake may only be placed directly on top of a water block — it draws from
  /// the pond it sits on. The full functional check (whole cube below is water, no
  /// crowding) lives in <see cref="BlockEntities.BlockEntityFluidIntake"/>.
  /// </summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  )
  {
    Block below = world.BlockAccessor.GetBlock(
      blockSel.Position.DownCopy(),
      BlockLayersAccess.Fluid
    );
    if (below.LiquidCode != "water")
    {
      // Shown to the player as Lang.Get("placefailure-" + code), so this must be
      // a plain code with a matching "game:placefailure-…" lang entry, not text.
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
  /// Unlike thin pipes, the intake is a standalone source block that rests on the
  /// water it pumps. Water is not an attachable surface, so the base self-break (no
  /// network neighbour + no solid support → break) would wrongly destroy a freshly
  /// placed intake before pipes are run to it. Keep its orientation in sync but never
  /// self-break; losing the water just disables intake (handled by the block entity).
  /// </summary>
  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  )
  {
    if (Orientation == null)
      return;

    RecalculateAndSyncOrientations(world, pos);
  }
}
