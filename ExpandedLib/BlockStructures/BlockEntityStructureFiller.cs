using System.Text;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.BlockStructures;

/// <summary>
/// Block entity for an invisible structure-filler block. Stores the position of
/// the "principal" (controller) block whose mega-block footprint this cell is part
/// of, and reroutes the looked-at block info back to that principal — mirroring
/// vanilla's <c>BEMPMultiblock</c>.
/// <para>
/// The filler block itself (<see cref="BlockStructureFiller"/>) forwards
/// interaction and break to the principal; this entity only carries the link and
/// the HUD passthrough.
/// </para>
/// </summary>
[EntityRegister]
public class BlockEntityStructureFiller : BlockEntity
{
  /// <summary>The controller block this filler cell belongs to, or null if orphaned.</summary>
  public BlockPos? Principal { get; set; }

  /// <summary>
  /// Whether other blocks may attach to this filler cell. Defaults to <c>false</c>
  /// so the invisible footprint behaves like empty space for placement of torches,
  /// vines, slabs, etc.; a <c>fillerOffsets</c> entry can opt a cell back in via its
  /// <c>allowAttach</c> flag. Honoured by <see cref="BlockStructureFiller.CanAttachBlockAt"/>.
  /// </summary>
  public bool AllowAttach { get; set; }

  /// <summary>
  /// Single-char face code ("u","d","n","s","e","w") of the network port this filler cell
  /// exposes, or null for a plain (non-port) filler. Lets a principal turn one footprint cell
  /// into a fixed pipe/molten connector — e.g. the boiler's steam outlet sits on the filler
  /// directly below the steam pipe. Read by <see cref="BlockStructureFiller"/>'s
  /// <c>INetworkConnector</c> implementation.
  /// </summary>
  public string? PortFace { get; set; }

  /// <summary>Network type of the exposed port (e.g. "pipe"), or null when this cell has no port.</summary>
  public string? PortNetworkType { get; set; }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    // -1,-1,-1 is the "no principal" sentinel (a real principal is never at a
    // negative world Y inside loaded chunks for our structures).
    tree.SetInt("cx", Principal?.X ?? -1);
    tree.SetInt("cy", Principal?.Y ?? -1);
    tree.SetInt("cz", Principal?.Z ?? -1);
    tree.SetBool("allowAttach", AllowAttach);
    if (PortFace != null && PortNetworkType != null)
    {
      tree.SetString("portFace", PortFace);
      tree.SetString("portNet", PortNetworkType);
    }
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    int cx = tree.GetInt("cx", -1);
    int cy = tree.GetInt("cy", -1);
    int cz = tree.GetInt("cz", -1);
    Principal =
      cx == -1 && cy == -1 && cz == -1 ? null : new BlockPos(cx, cy, cz);
    AllowAttach = tree.GetBool("allowAttach", false);
    PortFace = tree.GetString("portFace", null);
    PortNetworkType = tree.GetString("portNet", null);
  }

  /// <summary>Reroutes the HUD readout to the principal block entity.</summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
  {
    if (Principal == null)
      return;
    Api.World.BlockAccessor.GetBlockEntity(Principal)
      ?.GetBlockInfo(forPlayer, sb);
  }
}
