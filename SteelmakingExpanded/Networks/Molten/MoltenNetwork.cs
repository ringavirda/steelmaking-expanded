using System;
using System.Linq;
using BlockNetworkLib;
using SteelmakingExpanded.Networks.Molten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SteelmakingExpanded.Networks.Molten;

/// <summary>
/// Concrete <see cref="BlockNetwork"/> for the molten-canal system. Holds a single
/// liquid metal as a <see cref="MoltenNetworkState"/>, tracks its temperature and
/// solidification via the VS time-based temperature, and implements the push/drain,
/// merge/split, and per-tick cooling logic.
/// </summary>
public class MoltenNetwork(BlockNetworkModSystem system) : BlockNetwork(system)
{
  #region Metal
  /// <summary>Sums the per-block capacities of every canal node into the network's total capacity (units).</summary>
  public int CalculateCapacity(IBlockAccessor blockAccessor) =>
    Nodes.Sum(pos =>
      (
        blockAccessor.GetBlockEntity(pos) as BlockEntityMoltenCanal
      )?.MaxUnitCapacity
      ?? BlockEntityMoltenCanal.DefaultUnitCapacity
    );

  /// <summary>
  /// Pushes up to <paramref name="amount"/> units of <paramref name="metalStack"/> into the
  /// network, temperature-averaging with any existing metal of the same type. Returns the
  /// amount accepted (0 if the metal type mismatches or the network is full).
  /// </summary>
  public float TryPushMetal(
    float amount,
    ItemStack metalStack,
    IWorldAccessor world,
    IBlockAccessor blockAccessor
  )
  {
    string metalType = metalStack.Collectible.Code.ToString();
    float temperature = metalStack.Collectible.GetTemperature(
      world,
      metalStack
    );

    if (State == null)
    {
      float capacity = CalculateCapacity(blockAccessor);
      float accepted = Math.Min(amount, capacity);
      var stored = new ItemStack(metalStack.Collectible, 1);
      stored.Collectible.SetTemperature(
        world,
        stored,
        temperature,
        delayCooldown: false
      );
      (stored.Attributes["temperature"] as ITreeAttribute)?.SetFloat(
        "cooldownSpeed",
        40f
      );
      State = new MoltenNetworkState
      {
        MaxAmount = capacity,
        CurrentAmount = accepted,
        CurrentTemperature = temperature,
        MetalType = metalType,
        MetalStack = stored,
      };
      BroadcastUpdate(blockAccessor);
      return accepted;
    }

    var state = (MoltenNetworkState)State;

    // A solidified network must be broken to clear (OnTick only ever latches
    // Solidified to true). Reject pushes so fresh hot metal can't accumulate in a
    // canal that still reads as solid — which would strand the metal (consumers
    // refuse to drain a solidified network) and never re-liquefy.
    if (state.Solidified)
      return 0f;

    if (state.MetalType != metalType)
      return 0f;

    float space = state.MaxAmount - state.CurrentAmount;
    float accepted2 = Math.Min(amount, space);
    if (accepted2 <= 0f)
      return 0f;

    float existingTemp =
      state.MetalStack != null
        ? state.MetalStack.Collectible.GetTemperature(world, state.MetalStack)
        : state.CurrentTemperature;
    float totalAmount = state.CurrentAmount + accepted2;
    float newTemp =
      totalAmount > 0f
        ? (state.CurrentAmount * existingTemp + accepted2 * temperature)
          / totalAmount
        : temperature;

    state.MetalStack ??= new ItemStack(metalStack.Collectible, 1);
    state.MetalStack.Collectible.SetTemperature(
      world,
      state.MetalStack,
      newTemp,
      delayCooldown: false
    );
    if (
      state.MetalStack.Attributes["temperature"] is ITreeAttribute tempTree
      && tempTree.GetFloat("cooldownSpeed") <= 0f
    )
      tempTree.SetFloat("cooldownSpeed", 40f);
    state.CurrentTemperature = newTemp;
    state.CurrentAmount += accepted2;

    BroadcastUpdate(blockAccessor);
    return accepted2;
  }

  /// <summary>Removes up to <paramref name="amount"/> units from the network. Returns the amount actually drained.</summary>
  public float DrainMetal(float amount, IBlockAccessor blockAccessor)
  {
    if (State is not MoltenNetworkState state)
      return 0f;

    float actual = Math.Min(amount, state.CurrentAmount);
    state.CurrentAmount -= actual;
    if (state.CurrentAmount <= 0f)
      State = null;

    BroadcastUpdate(blockAccessor);
    return actual;
  }

  /// <summary>Returns the solid metal-bit drop for a solidified network, or <c>null</c> if there is nothing to drop.</summary>
  public ItemStack? GetSolidifiedDrops(IWorldAccessor world)
  {
    if (
      State is not MoltenNetworkState state
      || !state.Solidified
      || state.CurrentAmount <= 0f
      || state.MetalType.Length == 0
    )
      return null;

    int randLoss = Random.Shared.Next(3) * 5;
    float remaining = state.CurrentAmount - randLoss;
    if (remaining <= 0f)
      return null;

    int count = Math.Max(1, (int)(remaining / 5f));
    var solidLoc = SolidDropLocation(new AssetLocation(state.MetalType));
    Item? item = world.GetItem(solidLoc);
    return item != null ? new ItemStack(item, count) : null;
  }

  /// <summary>Maps a molten metal item to its solid drop ("game:ingot-iron" → "game:metalbit-iron"); non-ingot items drop as themselves.</summary>
  internal static AssetLocation SolidDropLocation(AssetLocation metalItemLoc)
  {
    if (metalItemLoc.Path.StartsWith("ingot-"))
      return new AssetLocation(
        metalItemLoc.Domain,
        "metalbit-" + metalItemLoc.Path[6..]
      );
    return metalItemLoc;
  }

  private IWorldAccessor? GetWorld(IBlockAccessor blockAccessor)
  {
    foreach (var pos in Nodes)
    {
      if (
        blockAccessor.GetBlockEntity(pos) is BlockEntity be
        && be.Api?.World != null
      )
        return be.Api.World;
    }
    return null;
  }
  #endregion

  protected override void OnBeforeBroadcast(IBlockAccessor blockAccessor)
  {
    if (State is not MoltenNetworkState state)
      return;

    state.MaxAmount = CalculateCapacity(blockAccessor);
  }

  #region Tick
  public override void OnTick(
    IBlockAccessor blockAccessor,
    float dt,
    BlockNetworkModSystem manager
  )
  {
    if (State is not MoltenNetworkState state || state.CurrentAmount <= 0f)
      return;

    var world = GetWorld(blockAccessor);
    if (world == null)
      return;

    // Lazily reconstruct MetalStack after world load (DeserializeNetworkState has no world access).
    if (state.MetalStack == null && state.MetalType.Length > 0)
    {
      var item = world.GetItem(new AssetLocation(state.MetalType));
      if (item != null)
      {
        state.MetalStack = new ItemStack(item, 1);
        state.MetalStack.Collectible.SetTemperature(
          world,
          state.MetalStack,
          state.CurrentTemperature,
          delayCooldown: false
        );
        (
          state.MetalStack.Attributes["temperature"] as ITreeAttribute
        )?.SetFloat("cooldownSpeed", 40f);
      }
    }

    if (state.MetalStack == null)
      return;

    // VS handles cooling automatically via time-based temperature decay.
    float temp = state.MetalStack.Collectible.GetTemperature(
      world,
      state.MetalStack
    );
    float meltPoint = state.MetalStack.Collectible.GetMeltingPoint(
      world,
      null,
      new DummySlot(state.MetalStack)
    );

    bool changed = false;
    if (state.CurrentTemperature != temp)
    {
      state.CurrentTemperature = temp;
      changed = true;
    }
    if (!state.Solidified && temp < meltPoint)
    {
      state.Solidified = true;
      changed = true;
    }
    if (state.CurrentAmount > state.MaxAmount)
    {
      state.CurrentAmount = state.MaxAmount;
      changed = true;
    }

    if (changed)
      BroadcastUpdate(blockAccessor);
  }
  #endregion

  #region Network
  public override string NetworkType => "molten";

  public override bool CanMerge(BlockNetwork other, IBlockAccessor world)
  {
    if (other is not MoltenNetwork otherMolten)
      return false;

    // A solidified network on either side blocks the merge.
    if (State is MoltenNetworkState s && s.Solidified)
      return false;
    if (otherMolten.State is MoltenNetworkState os && os.Solidified)
      return false;

    // An empty network (no state, or state with no metal type yet) can always
    // absorb / be absorbed — important on world load, where every node except
    // the start comes up with a null-state network and must merge into the one
    // that restored the metal. Note: a null State yields a null MetalType, which
    // is NOT "", so this must be checked explicitly rather than via string equality.
    string? thisType = (State as MoltenNetworkState)?.MetalType;
    string? otherType = (otherMolten.State as MoltenNetworkState)?.MetalType;
    if (string.IsNullOrEmpty(thisType) || string.IsNullOrEmpty(otherType))
      return true;

    // Both sides carry metal — only merge when the metal types match.
    return thisType == otherType;
  }

  public override void OnMerge(BlockNetwork other, IBlockAccessor world)
  {
    if (
      State is MoltenNetworkState state
      && other.State is not MoltenNetworkState
    )
      state.MaxAmount = CalculateCapacity(world);

    if (
      State is not MoltenNetworkState
      && other.State is MoltenNetworkState otherState
    )
    {
      otherState.MaxAmount = CalculateCapacity(world);
      State = otherState;
    }

    if (
      State is MoltenNetworkState stateMerge
      && other.State is MoltenNetworkState otherStateMerge
    )
    {
      stateMerge.MaxAmount = CalculateCapacity(world);

      var iworld = GetWorld(world);
      float temp1 =
        iworld != null && stateMerge.MetalStack != null
          ? stateMerge.MetalStack.Collectible.GetTemperature(
            iworld,
            stateMerge.MetalStack
          )
          : stateMerge.CurrentTemperature;
      float temp2 =
        iworld != null && otherStateMerge.MetalStack != null
          ? otherStateMerge.MetalStack.Collectible.GetTemperature(
            iworld,
            otherStateMerge.MetalStack
          )
          : otherStateMerge.CurrentTemperature;

      float totalAmount =
        stateMerge.CurrentAmount + otherStateMerge.CurrentAmount;
      float mergedTemp =
        totalAmount > 0f
          ? (
            stateMerge.CurrentAmount * temp1
            + otherStateMerge.CurrentAmount * temp2
          ) / totalAmount
          : temp1;

      if (iworld != null && stateMerge.MetalStack != null)
        stateMerge.MetalStack.Collectible.SetTemperature(
          iworld,
          stateMerge.MetalStack,
          mergedTemp,
          delayCooldown: false
        );
      stateMerge.CurrentTemperature = mergedTemp;
      stateMerge.CurrentAmount += otherStateMerge.CurrentAmount;
    }
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  )
  {
    // Give each fragment its proportional share of the metal (by capacity)
    // rather than wiping the whole network — only the broken block's portion is
    // lost. Temperature/solidified state carry over; the metal stack is cloned
    // so fragments don't share one mutable ItemStack.
    if (
      original is not MoltenNetwork orig
      || orig.State is not MoltenNetworkState os
      || os.CurrentAmount <= 0f
    )
    {
      State = null;
      return;
    }

    float origCapacity = Math.Max(1f, orig.CalculateCapacity(world));
    float fragCapacity = CalculateCapacity(world);
    float share = Math.Min(
      os.CurrentAmount * (fragCapacity / origCapacity),
      fragCapacity
    );
    if (share <= 0f)
    {
      State = null;
      return;
    }

    State = new MoltenNetworkState
    {
      MaxAmount = fragCapacity,
      CurrentAmount = share,
      CurrentTemperature = os.CurrentTemperature,
      MetalType = os.MetalType,
      Solidified = os.Solidified,
      MetalStack = os.MetalStack?.Clone(),
    };
  }

  public override void InheritStateFrom(BlockNetwork source)
  {
    if (source is MoltenNetwork other)
      State = other.State;
  }

  public override void RestoreState(object? state)
  {
    State = state as MoltenNetworkState;
  }
  #endregion
}
