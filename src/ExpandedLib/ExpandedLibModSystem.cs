using ExpandedLib.Blocks.Structures;
using ExpandedLib.Registries.Commands;
using ExpandedLib.Registries.Entities;
using ExpandedLib.Registries.Preferences;
using ExpandedLib.Registries.Recipes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib;

/// <summary>
/// Entry point for the shared Expanded Lib mod (<c>exlib</c>). Registers the library's own
/// blocks / block entities / behaviours (the invisible structure filler and the multiblock
/// structure behaviour) and points the <see cref="StructureFillers"/> helper at this mod's
/// filler block, so every dependent mod's mega-blocks reuse a single shared filler.
/// <para>
/// On the client it owns the generic per-player display-preferences store
/// (<see cref="Registries.Preferences.ExPreferences"/>): it loads the per-player <c>exmod.json</c>
/// and applies each player's saved choices on join. The preference <em>definitions</em> themselves
/// (and their <c>.exmod</c> sub-commands) live in the dependent mods - e.g. ppex owns the
/// metric/imperial unit preference - so a mod that only needs the library's framework pulls in none
/// of that.
/// </para>
/// <para>
/// The block-network graph manager and the world block-code migrator are separate
/// <c>ModSystem</c>s in this assembly (<see cref="Blocks.Networks.BlockNetworkModSystem"/>,
/// <see cref="Blocks.Migrations.BlockMigrationModSystem"/>); the game auto-loads them too.
/// </para>
/// </summary>
public class ExpandedLibModSystem : ModSystem {
  public override void Start(ICoreAPI api) {
    // Auto-register the library's [BlockRegister]/[BlockEntityRegister]/[BlockBehaviorRegister] classes (filler block + entity, the
    // MultiblockStructure behaviour) under the exlib domain.
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // The shared filler block this lib ships; dependent mods' mega-blocks reserve their
    // footprint cells with it (see StructureFillers).
    StructureFillers.FillerCode = new AssetLocation(
      Mod.Info.ModID,
      "structurefiller"
    );
  }

  public override void StartClientSide(ICoreClientAPI api) {
    // Load the per-player display-preference store (writes the file on first run). The preference
    // definitions are contributed by the dependent mods in their own StartClientSide; applying on
    // LevelFinalize (after every mod has started) picks up whatever they registered.
    ExPreferences.LoadConfig(api);

    // Apply the local player's saved choices once the world (and player) are ready.
    api.Event.LevelFinalize += () =>
      ExPreferences.ApplyForPlayer(api.World.Player.PlayerUID);

    // Register the library's own client commands: the shared .exmod root and its network-highlight
    // sub-command. Dependent mods attach their own sub-commands to the same root.
    CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // Apply every dependent mod's selected recipe-cost level (registered in their Start) to the live
    // recipes, so the client handbook/grid agree with the server. Runs after all mods' Start.
    ExRecipeProfiles.ApplyAll(api);
  }

  public override void StartServerSide(ICoreServerAPI api) {
    // Register the server-side counterpart: the universal exmod root surfaces here as /exmod, plus the
    // generic /exmod recipes <mod> <level> switch over the recipe profiles dependent mods register.
    CommandRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // Apply every registered mod's selected recipe-cost level to the live (host-authoritative) recipes.
    ExRecipeProfiles.ApplyAll(api);
  }
}
