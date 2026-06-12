namespace SteelmakingExpanded.BlockStructures.Converter;

/// <summary>Player-selected operating mode of the Bessemer converter.</summary>
public enum ConverterOpState
{
  /// <summary>Holding the charge; refines molten iron into steel when blast and power are present.</summary>
  Normal,

  /// <summary>Draining molten iron from the input tap into the vessel.</summary>
  Filling,

  /// <summary>Emptying the finished charge into the output canal.</summary>
  Pouring,
}
