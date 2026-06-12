using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>Coarse thermal state of a metal stack relative to its melting point.</summary>
public enum MoltenState
{
  /// <summary>Above 80% of the melting point — flows freely.</summary>
  Liquid,

  /// <summary>Between the hardened and liquid thresholds — no longer flows, still hot.</summary>
  Cooling,

  /// <summary>Below 30% of the melting point — fully hardened (chisellable).</summary>
  Hardened,
}

/// <summary>
/// The single source of truth for how the mod treats an <see cref="ItemStack"/> as a
/// carrier of molten metal: creating the temperature-tracked stack (with the mod's
/// cooldown speed), reading/writing its temperature, classifying its thermal state
/// against the melting point, the incandescent block-light scale, and the
/// player-facing metal/state formatting. Every canal cell, tap, pedestal, barrel and
/// the bessemer charge used to carry private copies of these rules.
/// </summary>
public static class MoltenMetal
{
  /// <summary>Fraction of the melting point above which metal counts as liquid.</summary>
  public const float LiquidThreshold = 0.8f;

  /// <summary>Fraction of the melting point below which metal counts as fully hardened.</summary>
  public const float HardenedThreshold = 0.3f;

  /// <summary>Below this temperature (°C) hot metal emits no block light.</summary>
  public const float GlowMinTemp = 500f;

  /// <summary>
  /// Creates a single-item temperature carrier for <paramref name="itemCode"/> at
  /// <paramref name="temperature"/> °C, cooling at <paramref name="cooldownSpeed"/>
  /// (default: the mod's molten cooldown). Returns <c>null</c> when the item does not
  /// resolve.
  /// </summary>
  public static ItemStack? CreateStack(
    IWorldAccessor world,
    string itemCode,
    float temperature,
    float? cooldownSpeed = null
  )
  {
    Item? item =
      itemCode.Length > 0 ? world.GetItem(new AssetLocation(itemCode)) : null;
    if (item == null)
      return null;
    var stack = new ItemStack(item, 1);
    SetCooldownSpeed(stack, cooldownSpeed ?? SmexValues.MoltenCooldownSpeed);
    SetTemperature(world, stack, temperature);
    return stack;
  }

  /// <summary>Sets the VS time-based cooldown speed on an existing temperature carrier.</summary>
  public static void SetCooldownSpeed(ItemStack stack, float cooldownSpeed) =>
    (stack.Attributes["temperature"] as ITreeAttribute)?.SetFloat(
      "cooldownSpeed",
      cooldownSpeed
    );

  /// <summary>Sets the stack temperature without delaying the cooldown (the mod-wide convention).</summary>
  public static void SetTemperature(
    IWorldAccessor world,
    ItemStack stack,
    float temperature
  ) =>
    stack.Collectible.SetTemperature(
      world,
      stack,
      temperature,
      delayCooldown: false
    );

  /// <summary>Current stack temperature (°C).</summary>
  public static float GetTemperature(IWorldAccessor world, ItemStack stack) =>
    stack.Collectible.GetTemperature(world, stack);

  /// <summary>The stack's melting point (°C), resolved through a dummy slot.</summary>
  public static float MeltingPointOf(IWorldAccessor world, ItemStack stack) =>
    stack.Collectible.GetMeltingPoint(world, null, new DummySlot(stack));

  /// <summary>Classifies the stack against its melting point (liquid / cooling / hardened).</summary>
  public static MoltenState StateOf(IWorldAccessor world, ItemStack stack)
  {
    float temp = GetTemperature(world, stack);
    float meltPoint = MeltingPointOf(world, stack);
    if (temp > LiquidThreshold * meltPoint)
      return MoltenState.Liquid;
    if (temp < HardenedThreshold * meltPoint)
      return MoltenState.Hardened;
    return MoltenState.Cooling;
  }

  /// <summary>True when the stack has cooled below the hardened threshold (chisellable).</summary>
  public static bool IsHardened(IWorldAccessor world, ItemStack stack) =>
    StateOf(world, stack) == MoltenState.Hardened;

  /// <summary>True when the stack is hot enough to flow (above the liquid threshold).</summary>
  public static bool IsLiquid(IWorldAccessor world, ItemStack stack) =>
    StateOf(world, stack) == MoltenState.Liquid;

  /// <summary>
  /// Incandescent block-light level (0–24) for metal at <paramref name="temperature"/> —
  /// the shared scale used by canals, barrels and the cowper heat sink.
  /// </summary>
  public static byte GlowLevel(float temperature) =>
    temperature > GlowMinTemp
      ? (byte)GameMath.Clamp((temperature - GlowMinTemp) / 30f, 0, 24)
      : (byte)0;

  /// <summary>
  /// Human-readable metal name from an item code:
  /// "game:ingot-iron" → "Iron", "smex:slag" → "Slag".
  /// </summary>
  public static string DisplayName(string metalItemCode)
  {
    if (metalItemCode.Length == 0)
      return Lang.Get("smex:metal-unknown");
    string path = new AssetLocation(metalItemCode).Path;
    string name = path.StartsWith("ingot-") ? path[6..] : path;
    return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;
  }

  /// <summary>"Cold" below room temperature, otherwise the rounded "650°C" form.</summary>
  public static string FormatTemperature(float temperature) =>
    temperature < 21f
      ? Lang.Get("smex:metalstate-cold")
      : $"{temperature:F0}°C";
}
