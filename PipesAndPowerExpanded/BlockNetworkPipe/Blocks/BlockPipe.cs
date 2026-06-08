using System.Collections.Generic;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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
  /// Pipe metal from the <c>material</c> variant (copper/tinbronze/brass/iron/steel).
  /// Blocks without the variant (brick passthrough/outlet) read as iron.
  /// </summary>
  public string Material => Variant["material"] ?? "iron";

  /// <summary>
  /// Pressure (atm) above which this pipe bursts — the weakest pipe limits a run.
  /// Copper 1.5, bronze/brass 3, iron 5, steel 15.
  /// </summary>
  public float BurstPressure =>
    Material switch
    {
      "copper" => PpexValues.CopperPipeBurstPressure,
      "brass" => PpexValues.BrassPipeBurstPressure,
      "steel" => PpexValues.SteelPipeBurstPressure,
      _ => PpexValues.IronPipeBurstPressure,
    };

  /// <summary>
  /// Temperature (°C) above which this pipe melts. Copper/bronze/brass have real
  /// melting points; ferric pipes return an unreachable value (they never melt).
  /// </summary>
  public float MeltingPoint =>
    Material switch
    {
      "copper" => 1084f,
      "brass" => 920f,
      _ => 99999f,
    };

  /// <summary>
  /// True for iron/steel pipes, which glow dimmer (0.6×) and only burst on pressure.
  /// Copper/bronze/brass are non-ferric: full glow and a thermal melt-burst.
  /// </summary>
  public bool IsFerric => Material is not ("copper" or "brass");

  /// <summary>
  /// Emits incandescent block light scaled to the pipe's local content temperature
  /// (same scheme as the molten canals); the cell owns the scaling via
  /// <see cref="BlockEntityPipe.GlowLightLevel"/> and re-lights on shift.
  /// </summary>
  public override byte[] GetLightHsv(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    ItemStack? stack = null
  )
  {
    if (pos != null && blockAccessor.GetBlockEntity(pos) is BlockEntityPipe be)
    {
      byte val = be.GlowLightLevel;
      if (val > 0)
        return [8, 7, val];
    }
    return base.GetLightHsv(blockAccessor, pos, stack);
  }

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
