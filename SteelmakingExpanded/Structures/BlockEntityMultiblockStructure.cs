using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Structures;

/// <summary>
/// Base block entity for the mod's multiblock machines (blast furnace, cowper stove,
/// bessemer control). Runs a slow monitor tick that detects when the structure is
/// completed or broken, and a production tick that fires only while complete.
/// Subclasses supply the orientation logic, production behavior, and status messages.
/// </summary>
public abstract class BlockEntityMultiblockStructure : BlockEntity
{
  protected MultiblockStructure? _structure;
  protected MultiblockStructure? _highlightedStructure;
  protected int _currentAngle = -1;
  private long _completionTickId;
  private long _productionTickId;

  /// <summary>Whether every block of the multiblock structure is currently in place.</summary>
  public bool StructureComplete { get; protected set; }

  /// <summary>Interval (ms) of the structure-completion monitor tick.</summary>
  protected virtual int CompletionTickMs => 3000;

  /// <summary>Interval (ms) of the production tick (runs only while complete).</summary>
  protected virtual int ProductionTickMs => 1000;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
    {
      // The monitor tick runs unconditionally so the structure is detected both
      // when it is completed (gain) and when it is broken (loss). The production
      // tick only runs while complete — and each OnProductionTick also guards on
      // StructureComplete, so it is harmless if it briefly runs otherwise.
      StartMonitorTick();
      if (StructureComplete)
        StartProductionTick();
    }
  }

  /// <summary>Starts both the completion monitor and the production tick.</summary>
  protected void StartStructureTick()
  {
    StartMonitorTick();
    StartProductionTick();
  }

  protected void StartMonitorTick()
  {
    if (_completionTickId == 0 && Api.Side == EnumAppSide.Server)
      _completionTickId = RegisterGameTickListener(
        OnMonitorStructureTick,
        CompletionTickMs
      );
  }

  protected void StartProductionTick()
  {
    if (_productionTickId == 0 && Api.Side == EnumAppSide.Server)
      _productionTickId = RegisterGameTickListener(
        OnProductionTick,
        ProductionTickMs
      );
  }

  protected void StopProductionTick()
  {
    if (_productionTickId != 0)
    {
      UnregisterGameTickListener(_productionTickId);
      _productionTickId = 0;
    }
  }

  /// <summary>Stops both ticks (used on block removal).</summary>
  protected void StopStructureTick()
  {
    StopProductionTick();
    if (_completionTickId != 0)
    {
      UnregisterGameTickListener(_completionTickId);
      _completionTickId = 0;
    }
  }

  private void OnMonitorStructureTick(float dt)
  {
    UpdateStructureRotation();
    if (_structure == null)
      return;

    bool nowComplete = _structure.InCompleteBlockCount(Api.World, Pos) == 0;
    if (nowComplete == StructureComplete)
      return;

    StructureComplete = nowComplete;
    if (nowComplete)
    {
      OnStructureCompleted();
      StartProductionTick();
    }
    else
    {
      OnStructureLost();
      StopProductionTick();
    }
    MarkDirty(true);
  }

  /// <summary>Called when a previously complete structure becomes incomplete. Default: no-op.</summary>
  protected virtual void OnStructureLost() { }

  /// <summary>Per-tick production logic; runs server-side only while the structure is complete.</summary>
  protected abstract void OnProductionTick(float dt);

  /// <summary>Recomputes the structure's rotation/angle from the block orientation.</summary>
  protected abstract void UpdateStructureRotation();

  /// <summary>Converts a structure-local offset into a world position for the current rotation.</summary>
  protected virtual BlockPos GetGlobalPos(int localX, int localY, int localZ)
  {
    var (dx, dz) = _currentAngle switch
    {
      90 => (-localZ, localX),
      180 => (-localX, -localZ),
      270 => (localZ, -localX),
      _ => (localX, localZ), // Default case covers 0 degrees
    };

    return Pos.AddCopy(dx, localY, dz);
  }

  /// <summary>
  /// Player interaction entry point: re-checks completeness, fires the completed/lost
  /// callbacks, and (client-side) shows the build outline or a status message.
  /// </summary>
  public virtual void Interact(IPlayer byPlayer)
  {
    UpdateStructureRotation();
    if (_structure == null)
      return;

    if (byPlayer.WorldData.EntityControls.ShiftKey)
    {
      if (Api is ICoreClientAPI capi)
      {
        _highlightedStructure?.ClearHighlights(Api.World, byPlayer);
        _highlightedStructure = null;
      }
      return;
    }

    int missingCount = _structure.InCompleteBlockCount(Api.World, Pos);
    bool wasComplete = StructureComplete;
    StructureComplete = missingCount == 0;

    if (Api.Side == EnumAppSide.Server)
    {
      if (StructureComplete && !wasComplete)
      {
        OnStructureCompleted();
        StartStructureTick();
        MarkDirty(true);
      }
      else if (!StructureComplete && wasComplete)
      {
        OnStructureLost();
        StopProductionTick();
        MarkDirty(true);
      }
    }

    if (Api is ICoreClientAPI clientApi)
    {
      if (missingCount > 0)
      {
        _highlightedStructure = _structure;
        clientApi.TriggerIngameError(
          this,
          "incomplete",
          GetIncompleteMessage(missingCount)
        );
        _highlightedStructure.HighlightIncompleteParts(
          Api.World,
          byPlayer,
          Pos
        );
      }
      else
      {
        clientApi.TriggerIngameError(this, "complete", GetCompleteMessage());
        _highlightedStructure?.ClearHighlights(Api.World, byPlayer);
        _highlightedStructure = null;
      }
    }
  }

  /// <summary>Called when the structure transitions to complete. Default: no-op.</summary>
  protected virtual void OnStructureCompleted() { }

  /// <summary>Returns the ingame-error message shown when the structure is missing <paramref name="missingCount"/> blocks.</summary>
  protected abstract string GetIncompleteMessage(int missingCount);

  /// <summary>Returns the ingame-error message shown when the structure is complete.</summary>
  protected abstract string GetCompleteMessage();

  public override void OnBlockRemoved()
  {
    base.OnBlockRemoved();
    StopStructureTick();
    if (Api is ICoreClientAPI capi)
      _highlightedStructure?.ClearHighlights(Api.World, capi.World.Player);
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("structureComplete", StructureComplete);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    StructureComplete = tree.GetBool("structureComplete");
  }
}
