using ExpandedLib.Blocks.Construction;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Makes any machine entity that gates on an <see cref="ExRightClickConstructable"/> (boiler,
/// engine) read as fully constructed. On 1.22 that behavior subclasses the vanilla
/// <see cref="BEBehaviorRightClickConstructable"/>, so its <c>IsComplete</c> is
/// <c>rcc.CurrentCompletedStage == rcc.Stages.Length - 1</c> off the inherited <c>rcc</c> field and
/// isn't virtual; a real instance is built with a single, already-completed stage and dropped into
/// the entity's private <c>_rcc</c> field (typed <see cref="ExRightClickConstructable"/>). Re-apply
/// after a real <c>Initialize</c> (which re-reads <c>_rcc</c> from the absent behaviors and clears it).
/// </summary>
internal static class RccFake {
  public static void Complete(BlockEntity be) {
    // The single, already-completed construction state set into the behavior's "rcc" field. On
    // 1.22 ExRightClickConstructable subclasses vanilla, so this is vanilla's RightClickConstruction;
    // on 1.20/1.21 it is exlib's ExRightClickConstruction port (vanilla's type doesn't exist there).
#if GAME_GE_1_22
    var construction = new RightClickConstruction {
      Stages = [new ConstructionStage()],
      CurrentCompletedStage = 0,
    };
#else
    var construction = new ExRightClickConstruction
    {
      Stages = [new ExConstructionStage()],
      CurrentCompletedStage = 0,
    };
#endif
    var rcc = new ExRightClickConstructable(be);
    ReflectionHelpers.SetField(rcc, "rcc", construction);
    ReflectionHelpers.SetField(be, "_rcc", rcc);
  }
}
