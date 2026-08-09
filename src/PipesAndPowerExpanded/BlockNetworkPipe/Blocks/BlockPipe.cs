using System.Collections.Generic;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Registries.Entities;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// The base pipe block: a self-orienting node of the unified "pipe" network. Provides the
/// orientation tables shared by every straight/bend/junction variant.
/// </summary>
[BlockRegister]
public partial class BlockPipe : BlockNetworkNode {
  public override string NetworkType => "pipe";

  /// <summary>
  /// Pipe metal from the <c>material</c> variant (iron/steel). Blocks without the
  /// variant (brick passthrough/outlet) read as iron.
  /// </summary>
  public string Material => Variant["material"] ?? "iron";

  /// <summary>
  /// Pressure (atm) above which this pipe bursts - the weakest pipe limits a run.
  /// Iron 5, steel 10.
  /// </summary>
  public virtual float BurstPressure =>
    Material switch {
      "steel" => PpexValues.SteelPipeBurstPressure,
      _ => PpexValues.IronPipeBurstPressure,
    };

  /// <summary>
  /// Whether this pipe takes part in over-pressure failure. Only a plain pipe segment - the four
  /// structural variants of the base <see cref="BlockPipe"/> class (straight/bend/tjunction/
  /// xjunction) - bursts and caps a run's pressure. Every specialised pipe (valve, outlet,
  /// passthrough, tuyere, …) is a subclass and is exempt: it neither bursts nor limits the
  /// pressure, so a new subclass is non-bursting by default unless it deliberately opts back in.
  /// </summary>
  public virtual bool CanBurst => GetType() == typeof(BlockPipe);

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new()
    {
      { "straight", ["ns", "we", "ud"] },
      {
        "bend",
        ["nw", "se", "en", "ws", "un", "us", "uw", "ue", "dn", "ds", "dw", "de"]
      },
      {
        "tjunction",
        [
          "uns",
          "uwe",
          "dns",
          "dwe",
          "nes",
          "esw",
          "swn",
          "wne",
          "dnu",
          "deu",
          "dsu",
          "dwu",
        ]
      },
      { "xjunction", ["nswe", "nsud", "weud"] },
    };

  protected override string GetFallbackOrientation(string? type) =>
    type switch {
      "bend" => "nw",
      "tjunction" => "uns",
      "xjunction" => "nswe",
      _ => "ns",
    };
}
