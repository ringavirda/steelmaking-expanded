#if GAME_GE_1_22
using System.Reflection;
using HarmonyLib;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.Patches;

/// <summary>
/// Harmony patch on the vanilla water wheel, holding its gearing ratio steady across the water
/// check.
/// <para>
/// A mechanical network's speed is a single figure held in the frame of whichever node seeded the
/// network, and each node's <c>GearedRatio</c> converts that figure into its own shaft speed. The
/// wheel's periodic water check calls <c>SetPropagationDirection</c> whenever the sensed flow
/// direction changes, and that path assigns a hardcoded ratio of 1 along with the direction. When
/// the network was seeded by any other node - a second rotor, or one of this pack's machines, which
/// discover their own networks - the wheel's true ratio is not 1, and overwriting it leaves the
/// wheel producing torque in a frame the rest of the network does not share. The network then
/// settles where the wheel's own speed reads correct, so every other node turns at the wheel's ratio
/// times its proper speed - across a large gear, off by 5.5.
/// </para>
/// <para>
/// The sensed direction is not persisted, so it always counts as changed on the first check after a
/// world load, which is why this shows up on reload and why rebuilding the network - breaking any
/// axle - clears it until the next load. The direction change itself is wanted; only the ratio is
/// restored here.
/// </para>
/// </summary>
[HarmonyPatch]
public static class WaterWheelGearedRatioPatch {
  // Named rather than nameof'd: the method is protected. Resolving it up front keeps a rename in a
  // later game version a skipped patch instead of a Harmony exception during mod load.
  private static MethodInfo? Target =>
    AccessTools.Method(
      typeof(BEBehaviorMPWaterWheel),
      "CheckWater",
      [typeof(float)]
    );

  public static bool Prepare() => Target != null;

  public static MethodBase TargetMethod() => Target!;

  public static void Prefix(
    BEBehaviorMPWaterWheel __instance,
    out float __state
  ) => __state = __instance.GearedRatio;

  public static void Postfix(
    BEBehaviorMPWaterWheel __instance,
    float __state
  ) => __instance.GearedRatio = __state;
}
#endif
