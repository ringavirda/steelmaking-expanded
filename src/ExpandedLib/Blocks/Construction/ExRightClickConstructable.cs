// exlib-owned right-click construction behavior, referenced by the mega-blocks (engines,
// boilers, bessemer converter) under the JSON behavior name "ExRightClickConstructable".
//
// On 1.22 it is a thin subclass of the vanilla BEBehaviorRightClickConstructable, so the
// primary build reuses vanilla's well-tested logic unchanged and only adds a clean drops
// accessor (replacing the old reflection into the protected rcc field). On 1.20/1.21, where
// the vanilla behavior does not exist, it is a full reimplementation backed by
// ExRightClickConstruction. Owning the JSON name on all versions lets the mod C# reference a
// single type and keeps vanilla blocks (e.g. the waterwheel) on vanilla's own behavior.
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Common;

namespace ExpandedLib.Blocks.Construction;

#if GAME_GE_1_22
using System;
using Vintagestory.GameContent;

[BlockEntityBehaviorRegister("ExRightClickConstructable", PrefixModId = false)]
public class ExRightClickConstructable(BlockEntity blockentity)
  : BEBehaviorRightClickConstructable(blockentity) {
  /// <summary>
  /// The materials this block would scatter at <paramref name="ratio"/> (0..1) of the consumed stacks,
  /// across EVERY completed stage. Vanilla <c>rcc.GetDrops</c> loops <c>i &lt; CurrentCompletedStage</c>,
  /// which silently omits the LAST built stage's materials - for the mega-blocks that's the most
  /// expensive stage (e.g. the Lancashire casing: 16 plate / 8 nails / 6 rod / 48 brick), so a fully
  /// built structure refunded far less than its <c>brokenDropsRatio</c>. Advancing the counter by one
  /// across the call (it's a public field) makes the loop reach the final stage, then we restore it.
  /// </summary>
  public ItemStack[] GetConstructionDrops(float ratio, Random rand) {
    int built = rcc.CurrentCompletedStage;
    rcc.CurrentCompletedStage = built + 1;
    try {
      return rcc.GetDrops(ratio, rand);
    } finally {
      rcc.CurrentCompletedStage = built;
    }
  }

  // The salvage fraction, taken from the owning mod's (player-tunable) config when it registered one,
  // else the JSON/default brokenDropsRatio. Read live so a /exmod config change applies immediately.
  private float EffectiveBrokenDropsRatio =>
    ExRccSettings.BrokenDropsRatio(Block.Code.Domain) ?? brokenDropsRatio;

  /// <summary>
  /// Replaces vanilla's break handler (which scatters the off-by-one <c>rcc.GetDrops</c>) so a broken
  /// structure refunds all completed stages at the configured salvage fraction. Null-safe on the breaker
  /// (an explosion-broken structure still drops its salvage), unlike the vanilla override.
  /// </summary>
  public override void OnBlockBroken(IPlayer? byPlayer = null) {
    if (byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative)
      return;
    foreach (
      var drop in GetConstructionDrops(
        EffectiveBrokenDropsRatio,
        Api.World.Rand
      )
    )
      Api.World.SpawnItemEntity(drop, Pos, null);
  }
}
#else
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

[BlockEntityBehaviorRegister("ExRightClickConstructable", PrefixModId = false)]
public class ExRightClickConstructable : BlockEntityBehavior, IInteractable
{
  private readonly ExRightClickConstruction rcc = new();
  private float brokenDropsRatio = 1f;

  public CompositeShape shape { get; protected set; }
  public bool IsComplete => rcc.CurrentCompletedStage == rcc.Stages.Length - 1;
  public event Action<CompositeShape>? OnShapeChanged;

  public ExRightClickConstructable(BlockEntity blockentity)
    : base(blockentity)
  {
    shape = blockentity.Block.Shape;
  }

  public override void Initialize(ICoreAPI api, JsonObject properties)
  {
    base.Initialize(api, properties);
    brokenDropsRatio = properties["brokenDropsRatio"].AsFloat(1f);
    var stages = properties["stages"].AsObject<ExConstructionStage[]>(null);
    rcc.LateInit(stages, api, "Block " + Block.Code);
    UpdateShape();
  }

  public bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref EnumHandling handling
  )
  {
    handling = EnumHandling.PreventDefault;
    if (rcc.OnInteract(byPlayer.Entity, byPlayer.Entity.RightHandItemSlot))
    {
      UpdateShape();
      Blockentity.MarkDirty(true);
    }
    return true;
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor world
  )
  {
    base.FromTreeAttributes(tree, world);
    rcc.FromTreeAttributes(tree);
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    rcc.ToTreeAttributes(tree);
    base.ToTreeAttributes(tree);
    UpdateShape();
  }

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tessThreadTesselator
  ) => true;

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  )
  {
    base.GetBlockInfo(forPlayer, dsc);
    if (Api.World.EntityDebugMode)
      dsc.AppendLine(
        $"<font color='#ccc'>construction stage= {rcc.CurrentCompletedStage} of {rcc.Stages.Length}</font>"
      );
  }

  // The salvage fraction, taken from the owning mod's (player-tunable) config when it registered one,
  // else the JSON/default brokenDropsRatio. Read live so a /exmod config change applies immediately.
  private float EffectiveBrokenDropsRatio =>
    ExRccSettings.BrokenDropsRatio(Block.Code.Domain) ?? brokenDropsRatio;

  public override void OnBlockBroken(IPlayer? byPlayer = null)
  {
    if (byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
      foreach (
        var drop in rcc.GetDrops(EffectiveBrokenDropsRatio, Api.World.Rand)
      )
        Api.World.SpawnItemEntity(drop, Pos.ToVec3d());
  }

  /// <summary>The materials this block would scatter at <paramref name="ratio"/> (0..1) of the
  /// consumed stacks.</summary>
  public ItemStack[] GetConstructionDrops(float ratio, Random rand) =>
    rcc.GetDrops(ratio, rand);

  /// <summary>The next-stage build-material hover help. On 1.22 vanilla supplies this via
  /// IInteractableWithHelp; on legacy the host block surfaces it through
  /// <see cref="AppendConstructionHelp"/> from its GetPlacedBlockInteractionHelp override.</summary>
  public WorldInteraction[]? GetConstructionInteractionHelp() =>
    rcc.GetInteractionHelp();

  /// <summary>Prepends the construction help of the block-entity at the selection (if it has this
  /// behavior) to <paramref name="baseHelp"/>. For legacy block GetPlacedBlockInteractionHelp overrides.</summary>
  public static WorldInteraction[] AppendConstructionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    WorldInteraction[] baseHelp
  )
  {
    var help = world
      .BlockAccessor.GetBlockEntity(selection.Position)
      ?.GetBehavior<ExRightClickConstructable>()
      ?.GetConstructionInteractionHelp();
    if (help == null || help.Length == 0)
      return baseHelp;
    if (baseHelp == null || baseHelp.Length == 0)
      return help;
    var combined = new WorldInteraction[help.Length + baseHelp.Length];
    help.CopyTo(combined, 0);
    baseHelp.CopyTo(combined, help.Length);
    return combined;
  }

  private void UpdateShape()
  {
    shape = new CompositeShape
    {
      Base = Block.Shape.Base,
      rotateY = Block.Shape.rotateY,
      SelectiveElements = rcc.getShapeElements(),
    };
    OnShapeChanged?.Invoke(shape);
  }
}
#endif
