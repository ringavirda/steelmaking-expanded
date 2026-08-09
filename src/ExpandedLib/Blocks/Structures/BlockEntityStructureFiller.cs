using System.Collections.Generic;
using System.Text;
using ExpandedLib.Registries.Entities;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks.Structures;

/// <summary>
/// Block entity for an invisible structure-filler block. Stores the position of the "principal"
/// (controller) block this footprint cell belongs to and reroutes the looked-at block info to it.
/// The filler block forwards interaction and break to the principal; this entity only carries the
/// link and HUD passthrough.
/// </summary>
[BlockEntityRegister]
public class BlockEntityStructureFiller : BlockEntity {
  /// <summary>The controller block this filler cell belongs to, or null if orphaned.</summary>
  public BlockPos? Principal { get; set; }

  /// <summary>
  /// Whether other blocks may attach to this cell. Defaults to <c>false</c> so the footprint
  /// behaves like empty space; a <c>fillerOffsets</c> entry can opt a cell back in via its
  /// <c>allowAttach</c> flag. Honoured by <see cref="BlockStructureFiller.CanAttachBlockAt"/>.
  /// </summary>
  public bool AllowAttach { get; set; }

  /// <summary>
  /// Single-char face code of the network port this cell exposes, or null for a plain filler. Lets
  /// a principal turn one footprint cell into a fixed connector (e.g. the boiler's steam outlet).
  /// </summary>
  public string? PortFace { get; set; }

  /// <summary>Network type of the exposed port (e.g. "pipe"), or null when this cell has no port.</summary>
  public string? PortNetworkType { get; set; }

  /// <summary>
  /// Behaviours this cell hosts on the principal's behalf (declared in <c>fillerOffsets</c>), each
  /// carrying its connector face already rotated into the placed orientation. Recreated by
  /// <see cref="ApplyHostedBehaviors"/> on placement and on load; null for a plain filler.
  /// </summary>
  public FillerBehavior[]? HostedBehaviors { get; set; }

  /// <summary>
  /// The behaviour instances created from <see cref="HostedBehaviors"/>, so a re-apply can detach
  /// the previous set before recreating it.
  /// </summary>
  private readonly List<BlockEntityBehavior> _hosted = [];

  // The most recent save/sync tree, kept so a hosted behaviour can still be handed it. FromTree
  // restores HostedBehaviors and Initialize instantiates them afterwards, so
  // BlockEntity.FromTreeAttributes has no behaviour to route the tree to on first load. A
  // client-side mechanical-power port joins its network purely from the synced NetworkId in it.
  private ITreeAttribute? _savedTree;

  private static readonly JsonObject EmptyProps = new(new JObject());

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    // FromTreeAttributes runs before Initialize and restores HostedBehaviors; the instances are
    // recreated here, once the class registry and Api are available.
    ApplyHostedBehaviors();
  }

  /// <summary>
  /// Stores the cell's hosted-behaviour declarations and recreates them. Called by
  /// <see cref="StructureFillers.PlaceFillers"/> right after the principal link is set, so an MP
  /// port joins the network at placement. Passing null clears any existing hosted behaviours.
  /// </summary>
  public void SetHostedBehaviors(FillerBehavior[]? behaviors) {
    HostedBehaviors = behaviors is { Length: > 0 } ? behaviors : null;
    ApplyHostedBehaviors();
    MarkDirty(true);
  }

  /// <summary>
  /// Instantiates each declared behaviour by its registered class code, hands it the principal link
  /// and rotated connector face (<see cref="IFillerHostedBehavior"/>), adds it to this block entity
  /// and initialises it. Detaches any previously created set first, so it is safe to call more than
  /// once. Does nothing until <see cref="BlockEntity.Api"/> is set; the load path runs it from
  /// <see cref="Initialize"/>.
  /// </summary>
  private void ApplyHostedBehaviors() {
    if (Api == null)
      return;

    foreach (BlockEntityBehavior previous in _hosted)
      Behaviors.Remove(previous);
    _hosted.Clear();

    if (HostedBehaviors == null)
      return;

    foreach (FillerBehavior spec in HostedBehaviors) {
      BlockEntityBehavior? beh = Api.ClassRegistry.CreateBlockEntityBehavior(
        this,
        spec.Code
      );
      if (beh == null) {
        Api.Logger.Warning(
          "[exlib] StructureFiller at {0}: unknown hosted behaviour class '{1}'.",
          Pos,
          spec.Code
        );
        continue;
      }
      // Configure before Initialize so the behaviour's SetOrientations sees the principal's face.
      (beh as IFillerHostedBehavior)?.ConfigureFromFiller(
        Principal,
        spec.ConnectorFace,
        spec.Properties
      );
      Behaviors.Add(beh);
      _hosted.Add(beh);
      beh.Initialize(Api, spec.Properties ?? EmptyProps);
      // Replays the loaded tree so the behaviour restores the state it would normally read in
      // FromTreeAttributes, having been created too late for that loop. Client only: on the server
      // the behaviour establishes its own state in Initialize (an MP port discovers its network),
      // which a stale saved NetworkId would override.
      if (Api.Side == EnumAppSide.Client && _savedTree != null)
        beh.FromTreeAttributes(_savedTree, Api.World);
    }
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    // -1,-1,-1 is the "no principal" sentinel.
    tree.SetInt("cx", Principal?.X ?? -1);
    tree.SetInt("cy", Principal?.Y ?? -1);
    tree.SetInt("cz", Principal?.Z ?? -1);
    tree.SetBool("allowAttach", AllowAttach);
    if (PortFace != null && PortNetworkType != null) {
      tree.SetString("portFace", PortFace);
      tree.SetString("portNet", PortNetworkType);
    }
    if (HostedBehaviors is { Length: > 0 } hosted) {
      var bt = new TreeAttribute();
      bt.SetInt("n", hosted.Length);
      for (int i = 0; i < hosted.Length; i++) {
        bt.SetString($"c{i}", hosted[i].Code);
        bt.SetString($"f{i}", hosted[i].ConnectorFace?.Code ?? "");
        bt.SetString($"p{i}", hosted[i].Properties?.ToString() ?? "");
      }
      tree["hostedBehaviors"] = bt;
    }
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    int cx = tree.GetInt("cx", -1);
    int cy = tree.GetInt("cy", -1);
    int cz = tree.GetInt("cz", -1);
    Principal =
      cx == -1 && cy == -1 && cz == -1 ? null : new BlockPos(cx, cy, cz);
    AllowAttach = tree.GetBool("allowAttach", false);
    PortFace = tree.GetString("portFace", null);
    PortNetworkType = tree.GetString("portNet", null);
    HostedBehaviors = tree["hostedBehaviors"] is ITreeAttribute bt
      ? ReadHostedBehaviors(bt)
      : null;
    // Kept so behaviours created below, or in Initialize, can still read their state from it; the
    // base loop above already fed any behaviour that existed at this point.
    _savedTree = tree;
    // When a mega-block is placed while a client is watching, the filler block is set first (the
    // client creates and initialises this entity with no hosted behaviours) and the principal
    // assigns HostedBehaviors a moment later, arriving here as a sync update. Initialize does not
    // run again, so the behaviours are created here instead. A cell's behaviour set is fixed at
    // placement, so only the first non-empty arrival needs handling.
    if (Api != null && _hosted.Count == 0 && HostedBehaviors is { Length: > 0 })
      ApplyHostedBehaviors();
  }

  /// <summary>Rebuilds the hosted-behaviour specs from the save tree (faces stay rotated as stored).</summary>
  private static FillerBehavior[]? ReadHostedBehaviors(ITreeAttribute bt) {
    int n = bt.GetInt("n", 0);
    if (n <= 0)
      return null;
    var list = new List<FillerBehavior>(n);
    for (int i = 0; i < n; i++) {
      string code = bt.GetString($"c{i}", "");
      if (string.IsNullOrEmpty(code))
        continue;
      string faceCode = bt.GetString($"f{i}", "");
      string propsJson = bt.GetString($"p{i}", "");
      list.Add(
        new FillerBehavior(
          code,
          string.IsNullOrEmpty(faceCode)
            ? null
            : BlockFacing.FromCode(faceCode),
          string.IsNullOrEmpty(propsJson)
            ? null
            : new JsonObject(JToken.Parse(propsJson))
        )
      );
    }
    return list.Count > 0 ? [.. list] : null;
  }

  /// <summary>Reroutes the HUD readout to the principal block entity.</summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb) {
    if (Principal == null)
      return;
    Api.World.BlockAccessor.GetBlockEntity(Principal)
      ?.GetBlockInfo(forPlayer, sb);
  }
}
