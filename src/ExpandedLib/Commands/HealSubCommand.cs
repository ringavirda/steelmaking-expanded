using ExpandedLib.Blocks.Healing;
using ExpandedLib.Registries.Commands;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Commands;

/// <summary>
/// Adds <c>/exmod heal</c>: sweeps every currently loaded chunk and recreates any orphaned block
/// entities - a block still in the world whose <see cref="BlockEntity"/> was discarded (e.g. a load
/// exception or a server desync), leaving an inert, often unbreakable block. Lets an op repair such
/// blocks in already-loaded chunks on demand, instead of waiting for the
/// <see cref="BlockEntityHealModSystem"/>'s automatic on-load pass. Server-side; the <c>/exmod</c>
/// root already requires <c>controlserver</c>.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class HealSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var healer = api.ModLoader.GetModSystem<BlockEntityHealModSystem>();

    parent
      .BeginSubCommand("heal")
      .WithDescription(Lang.Get("exlib:command-heal-desc"))
      .HandleWith(_ =>
        TextCommandResult.Success(
          Lang.Get("exlib:command-heal-result", healer.HealLoadedChunks())
        )
      )
      .EndSubCommand();
  }
}
