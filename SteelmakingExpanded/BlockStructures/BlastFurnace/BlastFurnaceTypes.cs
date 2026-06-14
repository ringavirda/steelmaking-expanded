namespace SteelmakingExpanded.BlockStructures.BlastFurnace;

/// <summary>Operating state of the blast furnace.</summary>
public enum BlastFurnaceState
{
  /// <summary>Not lit.</summary>
  Idle,

  /// <summary>Lit and heating up, but not yet hot enough to melt iron.</summary>
  Firing,

  /// <summary>Hot enough to melt iron; producing molten iron and slag.</summary>
  Melting,
}
