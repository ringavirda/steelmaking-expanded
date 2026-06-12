using ExpandedLib.BlockStructures;
using Vintagestory.API.Common;

namespace ExpandedLib;

/// <summary>
/// Entry point for the shared Expanded Lib mod (<c>exlib</c>). Registers the library's own
/// blocks / block entities / behaviours (the invisible structure filler and the multiblock
/// structure behaviour) and points the <see cref="StructureFillers"/> helper at this mod's
/// filler block, so every dependent mod's mega-blocks reuse a single shared filler.
/// <para>
/// The block-network graph manager and the world block-code migrator are separate
/// <c>ModSystem</c>s in this assembly (<see cref="BlockNetworks.BlockNetworkModSystem"/>,
/// <see cref="BlockMigrations.BlockMigrationModSystem"/>); the game auto-loads them too.
/// </para>
/// </summary>
public class ExpandedLibModSystem : ModSystem
{
  public override void Start(ICoreAPI api)
  {
    // Auto-register the library's [EntityRegister] classes (filler block + entity, the
    // MultiblockStructure behaviour) under the exlib domain.
    EntityRegistry.EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // The shared filler block this lib ships; dependent mods' mega-blocks reserve their
    // footprint cells with it (see StructureFillers).
    StructureFillers.FillerCode = new AssetLocation(
      Mod.Info.ModID,
      "structurefiller"
    );
  }
}
