using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Overrides;

/// <summary>
/// Replacement for the vanilla coal pile that adds blast-mix behavior: a pile of
/// blast mix that burns down into a solidified-slag block after a fixed time, unless
/// the pile is being managed (consumed) directly by a blast furnace.
/// </summary>
[EntityRegister("CoalPile", PrefixModId = false)]
public class CustomBlockEntityCoalPile : BlockEntityCoalPile
{
  private int _blastmixBurnTimer = 0;
  private long _tickId;

  /// <summary>When <c>true</c>, a blast furnace is consuming this pile, so it does not burn to slag on its own.</summary>
  public bool IsManagedByFurnace { get; set; } = false;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
    {
      _tickId = RegisterGameTickListener(OnCheckBurn, 1000);
    }
  }

  private void OnCheckBurn(float dt)
  {
    if (IsManagedByFurnace)
      return;

    if (
      IsBurning
      && inventory != null
      && inventory.Count > 0
      && !inventory[0].Empty
    )
    {
      if (inventory[0].Itemstack?.Collectible.Code.Path == "blastmix")
      {
        _blastmixBurnTimer++;
        if (_blastmixBurnTimer >= SmexValues.BlastmixBurnTime)
        {
          ConvertToSlag();
        }
      }
    }
  }

  /// <summary>Replaces this burnt-out blast-mix pile with a solidified-slag block carrying the same count.</summary>
  public void ConvertToSlag()
  {
    if (inventory != null && inventory.Count > 0 && !inventory[0].Empty)
    {
      int amount = inventory[0].StackSize;
      Block? slagBlock = Api.World.GetBlock(new AssetLocation("smex", "slag"));

      if (slagBlock != null)
      {
        Api.World.BlockAccessor.SetBlock(slagBlock.BlockId, Pos);
        if (
          Api.World.BlockAccessor.GetBlockEntity(Pos) is BlockEntitySlag beSlag
        )
        {
          beSlag.SlagCount = amount;
          beSlag.MarkDirty(true);
        }
      }
    }
  }

  public override void OnBlockRemoved()
  {
    base.OnBlockRemoved();
    if (_tickId != 0)
      UnregisterGameTickListener(_tickId);
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetInt("blastmixBurnTimer", _blastmixBurnTimer);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _blastmixBurnTimer = tree.GetInt("blastmixBurnTimer", 0);
  }
}
