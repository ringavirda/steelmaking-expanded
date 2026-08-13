namespace SteelmakingExpanded.BlockStructures.BlastFurnace;

/// <summary>
/// The blast furnace burden's exchange rates: what each accepted feed item is worth when the bell
/// hopper counts a batch, and what one item finally yields in molten iron. Both the hopper and the
/// item tooltips read them from here, so the rate is stated once.
/// </summary>
public static class BurdenValue {
  #region Carbon

  // The two fuels are counted in a shared unit rather than as separate quotas, so a batch can be
  // fed coke, charcoal or any mix of the two at one exchange rate. Scaling by BOTH configured
  // amounts keeps the arithmetic exact in integers whatever the two are set to: at the defaults
  // (2 coke or 4 charcoal) a batch costs 8 units, coke is worth 4 and charcoal 2.

  /// <summary>Carbon units one piece of coke contributes.</summary>
  public static int CarbonPerCoke => SmexValues.HopperCharcoalRequired;

  /// <summary>Carbon units one piece of charcoal contributes.</summary>
  public static int CarbonPerCharcoal => SmexValues.HopperCokeRequired;

  /// <summary>Carbon units one burden batch costs.</summary>
  public static int CarbonPerBatch =>
    SmexValues.HopperCokeRequired * SmexValues.HopperCharcoalRequired;

  #endregion

  #region Ore

  // Crushed ore and raw nuggets are counted in a shared unit, so a batch can be fed either or any
  // mix of the two. At the defaults the two rates are equal - 12 of either makes a batch of 144
  // units, and each piece is worth 12 - so the furnace takes its iron however it comes. The
  // cross-multiplication still holds the arithmetic exact in integers if the two are retuned apart.

  /// <summary>Ore units one piece of crushed iron ore contributes.</summary>
  public static int OrePerCrushed => SmexValues.HopperNuggetRequired;

  /// <summary>Ore units one iron nugget contributes.</summary>
  public static int OrePerNugget => SmexValues.HopperIronOreRequired;

  /// <summary>
  /// Ore units one piece of roasted iron ore contributes. Roasting is a heat step that returns the
  /// ore one for one, so it pays a premium over the raw feed rather than a rate of its own.
  /// </summary>
  public static int OrePerRoasted =>
    OrePerCrushed + SmexValues.HopperRoastedOreBonus;

  /// <summary>Ore units one burden batch costs.</summary>
  public static int OrePerBatch =>
    SmexValues.HopperIronOreRequired * SmexValues.HopperNuggetRequired;

  #endregion

  #region Iron yield

  /// <summary>Molten iron one burden batch is worth once it has been melted down.</summary>
  public static float IronPerBatch =>
    SmexValues.BfBurdenPerMeltCycle <= 0
      ? 0f
      : SmexValues.BfIronPerMeltCycle
        * SmexValues.HopperBurdenProduced
        / SmexValues.BfBurdenPerMeltCycle;

  /// <summary>
  /// Molten iron a single feed item worth <paramref name="oreUnits"/> yields, for the item tooltips.
  /// </summary>
  public static float IronPerOreUnits(int oreUnits) =>
    OrePerBatch <= 0 ? 0f : IronPerBatch * oreUnits / OrePerBatch;

  #endregion
}
