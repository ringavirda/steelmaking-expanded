using System.Collections.Generic;
using SteelmakingExpanded.Networks.Gas.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// Mechanically-powered blower: a network endpoint that bridges two gas networks,
/// pushing gas from the intake side to the output side at a rate set by the attached
/// mechanical-power network, and converting air to blast when spun fast enough.
/// Accepts axle connections on the two faces perpendicular to its flow axis.
/// </summary>
public class BlockGasBlower : BlockGasPipe, IMechanicalPowerBlock
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "blower", ["ns", "we"] } };

  /// <summary>Accepts an axle on the two faces perpendicular to the blower's gas-flow axis.</summary>
  public bool HasMechPowerConnectorAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face,
    BlockMPBase forBlock
  )
  {
    string orient = Variant["orientation"];
    if (
      orient == "ns"
      && (face == BlockFacing.WEST || face == BlockFacing.EAST)
    )
      return true;
    if (
      orient == "we"
      && (face == BlockFacing.NORTH || face == BlockFacing.SOUTH)
    )
      return true;
    return false;
  }

  public void DidConnectAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
  ) { }

  /// <summary>Returns the mechanical-power network feeding this blower, via its MP behavior.</summary>
  public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos)
  {
    return world
      .BlockAccessor.GetBlockEntity(pos)
      ?.GetBehavior<BEBehaviorMPBlower>()
      ?.Network;
  }

  public override bool IsNetworkEndPoint => true;

  protected override string GetFallbackOrientation(string? type) => "ns";

  /// <summary>
  /// A blower coupled to an axle is locked: its connector faces (and so the axle
  /// line) must stay put, so the wrench neither rotates it nor offers the hint.
  /// </summary>
  protected override bool CanWrenchRotate(IWorldAccessor world, BlockPos pos) =>
    !HasAdjacentMechPower(world, pos) && base.CanWrenchRotate(world, pos);

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  )
  {
    base.OnNeighbourBlockChange(world, pos, neighbour);

    if (world.Side != EnumAppSide.Server)
      return;

    // When the driving axle is removed the vanilla MP rebuild leaves the blower in
    // a lone, source-less network that coasts on its inherited speed for a long
    // time instead of stopping. Once no axle touches either connector face, drop
    // out of the network so the rendered fan halts immediately (and gas transfer,
    // which reads the same network speed, stops with it).
    if (
      !HasAdjacentMechPower(world, pos)
      && world
        .BlockAccessor.GetBlockEntity(pos)
        ?.GetBehavior<BEBehaviorMPBlower>()
        is BEBehaviorMPBlower mp
      && mp.Network != null
    )
      mp.LeaveNetwork();
  }

  /// <summary>True if an axle (or other MP block) couples to either of the blower's
  /// two perpendicular connector faces.</summary>
  private bool HasAdjacentMechPower(IWorldAccessor world, BlockPos pos)
  {
    BlockFacing[] faces =
      Variant["orientation"] == "ns"
        ? [BlockFacing.WEST, BlockFacing.EAST]
        : [BlockFacing.NORTH, BlockFacing.SOUTH];

    foreach (var face in faces)
    {
      BlockPos npos = pos.AddCopy(face);
      if (
        world.BlockAccessor.GetBlock(npos) is IMechanicalPowerBlock mpb
        && mpb.HasMechPowerConnectorAt(world, npos, face.Opposite, null)
      )
        return true;
    }
    return false;
  }
}
