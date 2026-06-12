using System.Collections.Generic;
using ExpandedLib.BlockMigrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

/// <summary>
/// Migrates the bessemer machine family that was renamed to "converter". The control, body
/// and transmission blocks kept their horizontal-orientation variant and only changed code
/// base (<c>bessemercontrol → convertercontrol</c>, <c>bessemerconverter → converterbessemer</c>,
/// <c>bessemertransmission → convertertransmission</c>). The separate gas-intake block was
/// folded into the converter as a <c>type</c> variant and switched from the short
/// <c>n/s/w/e</c> orientation to the <c>horizontalorientation</c> side words
/// (<c>bessemer-gasintake-n → converter-intake-north</c>).
/// </summary>
public class BessemerToConverterMigration : IBlockCodeMigration
{
  public string Name => "Bessemer machines renamed to converter";

  // The four horizontal-orientation sides shared by both code versions.
  private static readonly string[] Sides = ["north", "south", "east", "west"];

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  )
  {
    // Same-side machines: only the code base changed, the side variant is untouched.
    (string Old, string New)[] renames =
    [
      ("bessemercontrol", "convertercontrol"),
      ("bessemerconverter", "converterbessemer"),
      ("bessemertransmission", "convertertransmission"),
    ];
    foreach (var (oldBase, newBase) in renames)
    foreach (string side in Sides)
      yield return (
        new AssetLocation("smex", $"{oldBase}-{side}"),
        new AssetLocation("smex", $"{newBase}-{side}")
      );

    // The gas intake became a converter variant and the short orientation became side words.
    (string Orient, string Side)[] intake =
    [
      ("n", "north"),
      ("s", "south"),
      ("w", "west"),
      ("e", "east"),
    ];
    foreach (var (orient, side) in intake)
      yield return (
        new AssetLocation("smex", $"bessemer-gasintake-{orient}"),
        new AssetLocation("smex", $"converter-intake-{side}")
      );
  }
}
