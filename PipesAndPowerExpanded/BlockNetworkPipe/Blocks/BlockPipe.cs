using System.Collections.Generic;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// The base gas-pipe block: a self-orienting node of the unified "pipe" network.
/// Provides the orientation tables shared by every straight/bend/junction pipe variant.
/// </summary>
[EntityRegister]
public class BlockPipe : BlockNetworkNode
{
  public override string NetworkType => "pipe";

  /// <summary>
  /// Pipe metal from the <c>material</c> variant (iron/steel). Blocks without the
  /// variant (brick passthrough/outlet) read as iron.
  /// </summary>
  public string Material => Variant["material"] ?? "iron";

  /// <summary>
  /// Pressure (atm) above which this pipe bursts — the weakest pipe limits a run.
  /// Iron 5, steel 10.
  /// </summary>
  public virtual float BurstPressure =>
    Material switch
    {
      "steel" => PpexValues.SteelPipeBurstPressure,
      _ => PpexValues.IronPipeBurstPressure,
    };

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
    type switch
    {
      "bend" => "nw",
      "tjunction" => "uns",
      "xjunction" => "nswe",
      _ => "ns",
    };
}
