using System.Collections.Generic;
using BlockNetworkLib;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// The base gas-pipe block: a self-orienting node of the "gas" network. Provides
/// the orientation tables shared by every straight/bend/junction pipe variant.
/// </summary>
public class BlockGasPipe : BlockNetworkNode
{
  public override string NetworkType => "gas";

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
