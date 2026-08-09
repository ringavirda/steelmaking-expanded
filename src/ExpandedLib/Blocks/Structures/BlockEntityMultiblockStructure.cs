using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Blocks.Machines;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Blocks.Structures;

/// <summary>
/// Base block entity for the mod's multiblock machines (blast furnace, cowper stove,
/// bessemer control). Runs a slow monitor tick that detects when the structure is
/// completed or broken, and a production tick that fires only while complete.
/// Subclasses supply the orientation logic, production behavior, and status messages.
/// </summary>
public abstract class BlockEntityMultiblockStructure
  : BlockEntityProductionMachine {
  protected MultiblockStructure? _structure;
  protected MultiblockStructure? _highlightedStructure;
  protected int _currentAngle = -1;
  private long _completionTickId;

  /// <summary>Whether every block of the multiblock structure is currently in place.</summary>
  public bool StructureComplete { get; protected set; }

  /// <summary>Interval (ms) of the structure-completion monitor tick.</summary>
  protected virtual int CompletionTickMs => 3000;

  /// <summary>The production tick runs only while the structure is complete.</summary>
  protected override bool CanRunProduction => StructureComplete;

  /// <summary>Register the production tick on load only if the structure is already complete;
  /// the monitor tick starts/stops it across completion transitions.</summary>
  protected override bool AutoStartProduction => StructureComplete;

  public override void Initialize(ICoreAPI api) {
    // Base registers the production tick (only when already complete, via AutoStartProduction).
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server) {
      // Establish the structure angle up front. _currentAngle is not persisted, and the only other
      // caller is the 3000 ms monitor - so a structure that loads already complete would take three
      // production ticks at angle -1, which ExOrientation silently reads as unrotated. Every
      // structure-local offset (hearth cells, tuyeres, gas outlets) would resolve to the wrong
      // block for those ticks, which is long enough for a furnace to declare itself disrupted.
      UpdateStructureRotation();
      // The monitor tick runs unconditionally to detect both completion and breakage.
      StartMonitorTick();
    }
  }

  /// <summary>Starts both the completion monitor and the production tick.</summary>
  protected void StartStructureTick() {
    StartMonitorTick();
    StartProductionTick();
  }

  protected void StartMonitorTick() {
    if (_completionTickId == 0 && Api.Side == EnumAppSide.Server)
      _completionTickId = RegisterGameTickListener(
        OnMonitorStructureTick,
        CompletionTickMs
      );
  }

  /// <summary>Stops both ticks (used on block removal).</summary>
  protected void StopStructureTick() {
    StopProductionTick();
    if (_completionTickId != 0) {
      UnregisterGameTickListener(_completionTickId);
      _completionTickId = 0;
    }
  }

  private void OnMonitorStructureTick(float dt) {
    UpdateStructureRotation();
    if (_structure == null)
      return;

    bool nowComplete = _structure.InCompleteBlockCount(Api.World, Pos) == 0;
    if (nowComplete == StructureComplete)
      return;

    StructureComplete = nowComplete;
    if (nowComplete) {
      OnStructureCompleted();
      StartProductionTick();
    } else {
      OnStructureLost();
      StopProductionTick();
    }
    MarkDirty(true);
  }

  /// <summary>Called when a previously complete structure becomes incomplete. Default: no-op.</summary>
  protected virtual void OnStructureLost() { }

  /// <summary>Recomputes the structure's rotation/angle from the block orientation.</summary>
  protected abstract void UpdateStructureRotation();

  /// <summary>
  /// Canonical body for <see cref="UpdateStructureRotation"/>: (re)loads the
  /// <c>multiblockStructure</c> JSON when missing or <paramref name="angle"/> changed, calls
  /// <c>InitForUse(angle + initAngleOffset)</c>, caches the angle, and clears any stale build
  /// projection. <paramref name="initAngleOffset"/> covers machines whose local frame faces
  /// opposite the stored angle (e.g. the bessemer control at <c>angle + 180</c>).
  /// </summary>
  protected void SetStructureAngle(int angle, int initAngleOffset = 0) {
    if (_structure != null && _currentAngle == angle)
      return;

    _structure = Block.Attributes?[
      "multiblockStructure"
    ]?.AsObject<MultiblockStructure>();
    _structure?.InitForUse(angle + initAngleOffset);
    _currentAngle = angle;

    if (Api is ICoreClientAPI capi && _highlightedStructure != null) {
      _highlightedStructure.ClearHighlights(Api.World, capi.World.Player);
      _highlightedStructure = null;
    }
  }

  /// <summary>Converts a structure-local offset into a world position for the current rotation.</summary>
  protected virtual BlockPos GetGlobalPos(int localX, int localY, int localZ) =>
    ExOrientation.GlobalPos(Pos, localX, localY, localZ, _currentAngle);

  /// <summary>
  /// Player interaction entry point (the structure-projection toggle): re-checks completeness,
  /// fires the completed/lost callbacks, and client-side shows the build outline + missing count
  /// or clears it once complete. <see cref="FromTreeAttributes"/> also auto-clears the projection
  /// the moment the structure completes.
  /// </summary>
  public virtual void Interact(IPlayer byPlayer) {
    UpdateStructureRotation();
    if (_structure == null)
      return;

    // Tally missing blocks by wanted code while counting, to both draw the projection and
    // print an exact shopping list.
    var missingByCode = new Dictionary<AssetLocation, int>();
    int missingCount = _structure.InCompleteBlockCount(
      Api.World,
      Pos,
      (haveBlock, wantBlockCode) => {
        // Air-satisfied or auto-filled slots aren't player-gathered, so leave them out.
        if (IsAutoFilled(wantBlockCode))
          return;
        missingByCode.TryGetValue(wantBlockCode, out int count);
        missingByCode[wantBlockCode] = count + 1;
      }
    );
    bool wasComplete = StructureComplete;
    StructureComplete = missingCount == 0;

    if (Api.Side == EnumAppSide.Server) {
      if (StructureComplete && !wasComplete) {
        OnStructureCompleted();
        StartStructureTick();
        MarkDirty(true);
      } else if (!StructureComplete && wasComplete) {
        OnStructureLost();
        StopProductionTick();
        MarkDirty(true);
      }

      if (!StructureComplete && byPlayer is IServerPlayer serverPlayer)
        SendMissingBlocksReport(serverPlayer, missingByCode);
    }

    if (Api is ICoreClientAPI clientApi) {
      if (missingCount > 0) {
        _highlightedStructure = _structure;
        clientApi.TriggerIngameError(
          this,
          "incomplete",
          GetIncompleteMessage(missingCount)
        );
        HighlightIncompleteSafe(_highlightedStructure, byPlayer);
      } else {
        clientApi.TriggerIngameError(this, "complete", GetCompleteMessage());
        _highlightedStructure?.ClearHighlights(Api.World, byPlayer);
        _highlightedStructure = null;
      }
    }
  }

  /// <summary>
  /// Crash-safe replacement for vanilla <see cref="MultiblockStructure.HighlightIncompleteParts"/>,
  /// which tints each empty slot with <c>SearchBlocks(wantedCode)[0]</c> and throws
  /// <see cref="System.IndexOutOfRangeException"/> when a (wildcard) code resolves to no block.
  /// This mirrors the vanilla logic but falls back to a neutral tint for unresolvable slots.
  /// </summary>
  private void HighlightIncompleteSafe(
    MultiblockStructure structure,
    IPlayer player
  ) {
    var offsets = structure.TransformedOffsets;
    if (offsets == null)
      return;

    // Vanilla keeps its number→code map private; rebuild it from the public BlockNumbers.
    var codeByNumber = new Dictionary<int, AssetLocation>();
    foreach (var kv in structure.BlockNumbers)
      codeByNumber[kv.Value] = kv.Key;

    var positions = new List<BlockPos>();
    var colors = new List<int>();

    foreach (var offset in offsets) {
      if (!codeByNumber.TryGetValue(offset.W, out AssetLocation? wanted))
        continue;

      Block actual = Api.World.BlockAccessor.GetBlockRaw(
        Pos.X + offset.X,
        Pos.InternalY + offset.Y,
        Pos.Z + offset.Z
      );
      if (WildcardUtil.Match(wanted, actual.Code))
        continue;

      positions.Add(new BlockPos(offset.X, offset.Y, offset.Z).Add(Pos));

      if (actual.Id != 0) {
        // A wrong solid block occupies the slot - vanilla tints these red.
        colors.Add(ColorUtil.ColorFromRgba(215, 94, 94, 0x60));
        continue;
      }

      // Empty slot: tint with the wanted block's color when it resolves, otherwise
      // fall back to a neutral blue instead of crashing on an empty SearchBlocks.
      Block[] matches = Api.World.SearchBlocks(wanted);
      if (matches.Length == 0) {
        colors.Add(ColorUtil.ColorFromRgba(94, 94, 215, 0x60));
        continue;
      }

      int color = matches[0].GetColor(Api as ICoreClientAPI, Pos) & 0xFFFFFF;
      color |= 0x60 << 24;
      colors.Add(color);
    }

    Api.World.HighlightBlocks(
      player,
      MultiblockStructure.HighlightSlotId,
      positions,
      colors
    );
  }

  private static readonly AssetLocation AirCode = new("game:air");

  /// <summary>
  /// True when a slot is satisfied without the player gathering a block - an air slot (open shaft,
  /// "@(air|coalpile)" fuel) or a structure-filler cell. Excluded from the materials report.
  /// </summary>
  private static bool IsAutoFilled(AssetLocation wantBlockCode) =>
    WildcardUtil.Match(wantBlockCode, AirCode)
    || WildcardUtil.Match(wantBlockCode, StructureFillers.FillerCode);

  /// <summary>
  /// Sends the player a chat breakdown of every block still missing from the
  /// structure and how many of each, resolving (possibly wildcard) codes to
  /// readable block names.
  /// </summary>
  private void SendMissingBlocksReport(
    IServerPlayer player,
    Dictionary<AssetLocation, int> missingByCode
  ) {
    if (missingByCode.Count == 0)
      return;

    // ExpandedLib ships inside ppex, so the shared report strings live in ppex's lang.
    var sb = new StringBuilder();
    sb.Append(Lang.Get("ppex:structure-missing-header"));

    foreach (
      var entry in missingByCode
        .OrderByDescending(e => e.Value)
        .ThenBy(e => ResolveBlockName(e.Key))
    ) {
      sb.Append('\n');
      sb.Append(
        Lang.Get(
          "ppex:structure-missing-line",
          entry.Value,
          ResolveBlockName(entry.Key)
        )
      );
    }

    player.SendMessage(
      GlobalConstants.GeneralChatGroup,
      sb.ToString(),
      EnumChatType.Notification
    );
  }

  /// <summary>
  /// Resolves a structure block code - which may be a wildcard such as
  /// "smex:blastfurnacedoor*" - to a human-readable display name.
  /// </summary>
  private string ResolveBlockName(AssetLocation wantBlockCode) {
    Block? block = Api.World.GetBlock(wantBlockCode);
    if (block == null) {
      Block[] matches = Api.World.SearchBlocks(wantBlockCode);
      if (matches.Length > 0)
        block = matches[0];
    }

    return block != null
      ? new ItemStack(block).GetName()
      : wantBlockCode.ToShortString();
  }

  /// <summary>Called when the structure transitions to complete. Default: no-op.</summary>
  protected virtual void OnStructureCompleted() { }

  /// <summary>Returns the ingame-error message shown when the structure is missing <paramref name="missingCount"/> blocks.</summary>
  protected abstract string GetIncompleteMessage(int missingCount);

  /// <summary>Returns the ingame-error message shown when the structure is complete.</summary>
  protected abstract string GetCompleteMessage();

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    StopStructureTick();
    if (Api is ICoreClientAPI capi)
      _highlightedStructure?.ClearHighlights(Api.World, capi.World.Player);
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetBool("structureComplete", StructureComplete);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    bool wasComplete = StructureComplete;
    StructureComplete = tree.GetBool("structureComplete");

    // Auto-hide the build projection the moment the structure finishes.
    if (
      !wasComplete
      && StructureComplete
      && Api is ICoreClientAPI capi
      && _highlightedStructure != null
    ) {
      _highlightedStructure.ClearHighlights(Api.World, capi.World.Player);
      _highlightedStructure = null;
    }
  }
}
