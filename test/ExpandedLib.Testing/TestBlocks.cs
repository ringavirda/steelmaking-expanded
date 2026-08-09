using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>
/// Helpers for turning a bare <see cref="Block"/>/<see cref="BlockEntity"/> instance into one
/// the simulation can read, without the engine's asset-load pipeline. A freshly constructed
/// <see cref="Block"/> has a <c>null</c> <see cref="RegistryObject.Variant"/>, so any code path
/// touching <c>Variant["..."]</c> would throw - <see cref="Configure"/> primes it.
/// </summary>
public static class TestBlocks {
  /// <summary>
  /// Assigns <paramref name="code"/>/<paramref name="id"/> and builds the relaxed variant map
  /// from <paramref name="variants"/>. The relaxed map returns <c>null</c> (not a throw) for
  /// absent keys, matching how the real registry builds it - so e.g. <c>BlockPipe.Material</c>
  /// falls back to iron when no material variant is supplied.
  /// </summary>
  public static T Configure<T>(
    T block,
    string code,
    int id,
    params (string key, string value)[] variants
  )
    where T : Block {
    block.Code = new AssetLocation(code);
    block.BlockId = id;
    foreach (var (key, value) in variants)
      block.VariantStrict[key] = value;
    block.Variant = new RelaxedReadOnlyDictionary<string, string>(
      block.VariantStrict
    );
    return block;
  }
}
