using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.BlockStructures;

/// <summary>
/// Optional contract for a principal (controller) block whose interactions depend on
/// WHICH cell of its mega-block footprint the player clicked — not merely that some
/// footprint cell was clicked.
/// <para>
/// <see cref="BlockStructureFiller"/> forwards interaction to these methods (instead of
/// the plain whole-footprint <c>Block.OnBlockInteract*</c> forwarding) when the
/// principal implements this interface, passing both the principal-repointed selection
/// (so block-entity lookup still resolves the controller) and the originally-clicked
/// world cell. Principals that don't implement it keep the undifferentiated forwarding.
/// </para>
/// </summary>
public interface IFillerInteractionTarget
{
  bool OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  );

  bool OnFillerInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  );

  void OnFillerInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  );

  WorldInteraction[] GetFillerInteractionHelp(
    IWorldAccessor world,
    BlockSelection principalSel,
    IPlayer forPlayer,
    BlockPos clickedCell
  );
}
