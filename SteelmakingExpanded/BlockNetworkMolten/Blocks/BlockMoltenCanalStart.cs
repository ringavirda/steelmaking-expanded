using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.BlockNetworkMolten.Blocks;

/// <summary>
/// The canal network's anchor block. Acts as an <see cref="BlockEntityMoltenCanalStart"/>
/// sink that liquid metal is poured into — from a furnace/converter tap above, or
/// directly from a smelted crucible held by the player.
/// </summary>
[EntityRegister]
public class BlockMoltenCanalStart : BlockMoltenCanal
{
  // Smelted crucibles cached once on load, used only for the pour interaction help.
  private ItemStack[] _smeltedCrucibles = [];

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "start", ["n", "s", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) =>
    type switch
    {
      "start" => "s",
      _ => "s",
    };

  public override void OnLoaded(ICoreAPI api)
  {
    base.OnLoaded(api);

    var crucibles = new List<ItemStack>();
    foreach (var block in api.World.Blocks)
    {
      if (
        block.Code != null
        && block.Code.Path.StartsWith("crucible-")
        && block.Code.Path.EndsWith("-smelted")
      )
        crucibles.Add(new ItemStack(block));
    }
    _smeltedCrucibles = crucibles.ToArray();
  }

  // Return false so the held item's interaction runs instead of being swallowed.
  // The block entity implements ILiquidMetalSink, so vanilla BlockSmeltedContainer
  // (a smelted crucible) pours its molten metal into the canal network from here.
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) => false;

  protected override void GetRotations(
    string orientation,
    out float rotX,
    out float rotY,
    out float rotZ
  )
  {
    rotX = 0;
    rotY = 0;
    rotZ = 0;

    switch (orientation)
    {
      case "n":
        rotY = 180;
        break;
      case "s":
        rotY = 0;
        break;
      case "w":
        rotY = 90;
        break;
      case "e":
        rotY = 270;
        break;
      default:
        base.GetRotations(orientation, out rotX, out rotY, out rotZ);
        break;
    }
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    // Only advertise pouring while the network here can still take metal.
    if (
      world.BlockAccessor.GetBlockEntity(selection.Position)
        is not BlockEntityMoltenCanalStart be
      || !be.CanReceiveAny
    )
      return baseHelp;

    return
    [
      .. baseHelp,
      new WorldInteraction
      {
        ActionLangCode = "smex:blockhelp-canalstart-pour",
        MouseButton = EnumMouseButton.Right,
        Itemstacks = _smeltedCrucibles,
      },
    ];
  }
}
