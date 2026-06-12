using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.BlockNetworks;

/// <summary>
/// Base class for all <c>Block</c> types that can auto-orient based on surrounding
/// blocks of the same network to form a complete block network.
/// Currently used for gas pipes and molten canals.
/// </summary>
public abstract class BlockNetworkNode
  : Block,
    IWrenchOrientable,
    INetworkConnector
{
  /// <summary>Populated from the block's variant map; specifies the shape family (e.g. "straight", "bend").</summary>
  public string? Type { get; protected set; }

  /// <summary>
  /// Populated from the block's variant map; encodes which faces have network
  /// connectors using single-character codes (e.g. "ns" = north + south).
  /// </summary>
  public string? Orientation { get; protected set; }

  /// <summary>
  /// The mod system that governs all block networks and nodes.
  /// </summary>
  public BlockNetworkModSystem? NetworkSystem { get; protected set; }

  public override void OnLoaded(ICoreAPI api)
  {
    base.OnLoaded(api);
    // Pre-compute rotated collision/selection boxes for all orientation variants.
    PrecomputeRotatedBoxes();

    Type = Variant["type"] != null ? string.Intern(Variant["type"]) : null;
    Orientation =
      Variant["orientation"] != null
        ? string.Intern(Variant["orientation"])
        : null;

    if (api.Side == EnumAppSide.Server)
      NetworkSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
  }

  #region Placement and orientation
  /// <summary>
  /// Temporary store that passes the computed orientation choices from
  /// <see cref="TryPlaceBlock"/> to <see cref="OnBlockPlaced"/> within the same
  /// placement call.  Keyed by world position.
  /// </summary>
  private static readonly ConcurrentDictionary<
    BlockPos,
    string[]
  > _tempOrientationsStore = new();

  /// <summary>
  /// Determines the best orientation for this block at the target position by
  /// examining neighbouring network blocks, then delegates to <c>DoPlaceBlock</c>.
  /// </summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  )
  {
    if (!world.BlockAccessor.GetBlock(blockSel.Position).IsReplacableBy(this))
    {
      failureCode = "notreplaceable";
      return false;
    }
    if (Type == null)
      return false;

    string[] safeChoices = ComputeValidOrientations(
      world.BlockAccessor,
      blockSel.Position,
      Type,
      null
    );
    if (safeChoices.Length == 0)
    {
      // Shown to the player as Lang.Get("placefailure-" + code), so this must be
      // a plain code with a matching "game:placefailure-…" lang entry, not text.
      failureCode = "exlib-noorientation";
      return false;
    }

    // Prefer orientations that face the surface the player clicked.
    char targetFaceChar = blockSel.Face.Opposite.Code[0];
    string[] preferredChoices = safeChoices
      .Where(o => o.Contains(targetFaceChar))
      .ToArray();

    // Placing on a horizontal surface (clicking a block's top/bottom) gives no
    // horizontal hint from the clicked face, so without this the block always
    // snaps to its first valid orientation. Fall back to the player's look
    // direction — the connector points the way they're looking, matching how
    // wall placement points it into the clicked wall.
    if (preferredChoices.Length == 0)
    {
      char lookChar = SuggestedHVOrientation(byPlayer, blockSel)[
        0
      ].Opposite.Code[0];
      preferredChoices = safeChoices.Where(o => o.Contains(lookChar)).ToArray();
    }

    string[] finalChoices =
      preferredChoices.Length > 0 ? preferredChoices : safeChoices;

    _tempOrientationsStore[blockSel.Position] = finalChoices;

    AssetLocation newCode = CodeWithVariant("orientation", finalChoices[0]);
    Block? block = world.GetBlock(newCode);

    if (block != null)
    {
      block.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
      return true;
    }

    bool placed = base.TryPlaceBlock(
      world,
      byPlayer,
      itemstack,
      blockSel,
      ref failureCode
    );
    // A failed placement never reaches OnBlockPlaced (which consumes the entry), so
    // drop it here — otherwise the static store grows for every refused placement.
    if (!placed)
      _tempOrientationsStore.TryRemove(blockSel.Position, out _);
    return placed;
  }

  /// <summary>
  /// After the block is placed, stores the computed orientation choices on the block
  /// entity (for wrench cycling) or falls back to a full recalculation.
  /// </summary>
  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack? byItemStack = null
  )
  {
    base.OnBlockPlaced(world, blockPos, byItemStack);

    if (_tempOrientationsStore.TryRemove(blockPos, out string[]? finalChoices))
    {
      if (
        world.BlockAccessor.GetBlockEntity(blockPos)
        is BlockEntityNetworkNode beNet
      )
      {
        beNet.Orientation = Orientation;
        beNet.PossibleOrientations = finalChoices;
        beNet.MarkDirty(true);
      }
    }
    else
    {
      RecalculateAndSyncOrientations(world, blockPos);
    }

    // Neighbour orientation updates are handled by OnNeighbourBlockChange, which the
    // VS engine calls on every adjacent block after this method returns — no need to
    // duplicate that work here.
    //
    // Node registration is handled by BlockEntityNetworkNode.Initialize, which runs
    // inside DoPlaceBlock (before OnBlockPlaced).  Calling AddNode again here would
    // cause a redundant BroadcastUpdate → O(N) MarkDirty calls on every network node,
    // freezing the server for large networks.
  }

  /// <summary>
  /// Computes the valid orientations for a block of the given <paramref name="type"/>
  /// at <paramref name="pos"/>, taking into account which faces have compatible network
  /// neighbours (required faces) and which faces have non-network neighbours (forbidden).
  /// </summary>
  protected virtual string[] ComputeValidOrientations(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    string type,
    string? currentOrientation
  )
  {
    if (!AllowedOrientations.TryGetValue(type, out string[]? validOrientations))
      return [];

    List<char> requiredChars = [];
    List<char> forbiddenChars = [];

    foreach (var face in BlockFacing.ALLFACES)
    {
      BlockPos neighborPos = pos.AddCopy(face);
      Block neighborBlock = blockAccessor.GetBlock(neighborPos);

      if (
        BlockNetworkModSystem.IsCompatibleNetworkBlockAt(
          blockAccessor,
          neighborPos,
          neighborBlock,
          NetworkType
        ) && neighborBlock is INetworkConnector neighborNet
      )
      {
        if (
          neighborNet.HasConnectorAt(blockAccessor, neighborPos, face.Opposite)
        )
          requiredChars.Add(face.Code[0]);
        else
          forbiddenChars.Add(face.Code[0]);
      }
      else if (
        currentOrientation != null
        && neighborBlock.CanAttachBlockAt(
          blockAccessor,
          this,
          neighborPos,
          face.Opposite
        )
      )
      {
        if (currentOrientation.Contains(face.Code[0]))
          requiredChars.Add(face.Code[0]);
      }
    }

    // A linear shape connects only collinear faces — every one of its orientations
    // lies on a single axis (e.g. ns / we / ud) — so it can never join two
    // perpendicular neighbours at once. When such a segment has connectors on
    // perpendicular faces it cannot honour all of them; instead it stays placeable by
    // connecting to any one of them — the player picks the face at placement
    // (clicked/look direction) and the wrench switches between the choices. Shapes
    // that can bend (a single orientation spanning two axes) must still honour every
    // required face, so the relaxation must not apply to them.
    bool connectsAny = validOrientations.All(IsSingleAxisOrientation);

    bool Matches(string orient) =>
      !forbiddenChars.Any(c => orient.Contains(c))
      && (
        connectsAny
          ? requiredChars.Count == 0
            || requiredChars.Any(c => orient.Contains(c))
          : requiredChars.All(c => orient.Contains(c))
      );

    var choices = validOrientations.Where(Matches).ToArray();

    // Fallback: relax the requirement for non-network faces if no orientation matched.
    if (
      choices.Length == 0
      && requiredChars.Count > 0
      && currentOrientation != null
    )
    {
      requiredChars.RemoveAll(c =>
      {
        BlockFacing? facing = BlockFacing.FromCode(c.ToString());
        if (facing == null)
          return false;
        BlockPos nPos = pos.AddCopy(facing);
        return !BlockNetworkModSystem.IsCompatibleNetworkBlockAt(
          blockAccessor,
          nPos,
          blockAccessor.GetBlock(nPos),
          NetworkType
        );
      });

      choices = validOrientations.Where(Matches).ToArray();
    }

    return choices;
  }

  /// <summary>
  /// True when every connector face in <paramref name="orientation"/> lies on the same
  /// axis (e.g. "ns", "we", "ud", or a single face). Such shapes are linear: they can
  /// only join collinear neighbours, never two perpendicular ones at once.
  /// </summary>
  private static bool IsSingleAxisOrientation(string orientation) =>
    orientation
      .Select(c => BlockFacing.FromFirstLetter(c)?.Axis)
      .Distinct()
      .Count() == 1;

  /// <summary>
  /// Called by the VS engine when a neighbouring block changes.  Recalculates this
  /// block's valid orientations and breaks the block if it is no longer supported
  /// (no connected network neighbours and no solid surface to attach to).
  /// </summary>
  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  )
  {
    if (Orientation == null)
      return;

    RecalculateAndSyncOrientations(world, pos);

    // Break the block if it has no network neighbours and no solid surface to rest on.
    bool hasSolidSurface = false;
    foreach (var f in BlockFacing.ALLFACES)
    {
      BlockPos nPos = pos.AddCopy(f);
      if (
        world
          .BlockAccessor.GetBlock(nPos)
          .CanAttachBlockAt(world.BlockAccessor, this, nPos, f.Opposite)
      )
      {
        hasSolidSurface = true;
        break;
      }
    }

    if (
      !world
        .Api.ModLoader.GetModSystem<BlockNetworkModSystem>()
        .GetConnectedNeighbors(world.BlockAccessor, pos, NetworkType)
        .Any() && !hasSolidSurface
    )
      world.BlockAccessor.BreakBlock(pos, null);
  }

  /// <summary>
  /// Removes this block's position from the network graph before calling the base
  /// break logic (which drops items, removes the block entity, etc.).
  /// </summary>
  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1
  )
  {
    world
      .Api.ModLoader.GetModSystem<BlockNetworkModSystem>()
      .RemoveNode(world.BlockAccessor, pos);

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }
  #endregion

  #region IWrenchOrientable
  /// <summary>
  /// Cycles the block's orientation forward or backward through the possible choices
  /// stored on the block entity.  Re-registers the node in the network graph with
  /// the new orientation's connector set.
  /// </summary>
  public void Rotate(EntityAgent byEntity, BlockSelection blockSel, int dir)
  {
    if (Type == null || Orientation == null)
      return;

    IWorldAccessor world = byEntity.World;
    BlockPos pos = blockSel.Position;
    BlockEntity? be = world.BlockAccessor.GetBlockEntity(pos);

    if (!CanWrenchRotate(world, pos))
      return;

    string[] choices = GetWrenchOrientations(world, pos);
    if (choices.Length == 0)
      return;

    int currentIndex = Array.IndexOf(choices, Orientation);
    if (currentIndex == -1)
      return;

    int nextIndex = (currentIndex + dir) % choices.Length;
    if (nextIndex < 0)
      nextIndex += choices.Length;

    AssetLocation nextCode = CodeWithVariant("orientation", choices[nextIndex]);
    Block? nextBlock = world.GetBlock(nextCode);

    if (nextBlock != null && nextBlock.BlockId != BlockId)
    {
      var netManager =
        world.Api.ModLoader.GetModSystem<BlockNetworkModSystem>();

      // Full remove + add with broadcast so clients see the new connectivity immediately.
      netManager.RemoveNode(world.BlockAccessor, pos);

      world.BlockAccessor.ExchangeBlock(nextBlock.BlockId, pos);
      world.BlockAccessor.MarkBlockDirty(pos);
      be?.MarkDirty(true);

      // Recalculate adjacent blocks to keep their orientations consistent.
      foreach (var face in BlockFacing.ALLFACES)
        RecalculateAndSyncOrientations(world, pos.AddCopy(face));

      netManager.AddNode(world.BlockAccessor, pos, NetworkType);
    }
  }

  /// <summary>
  /// Returns the orientation cycle a wrench rotates through at <paramref name="pos"/>.
  /// <para>Full-cube blocks (passthrough, outlet, heated intake, …) fill the whole cell and
  /// are therefore supported in any orientation. For them the cycle is recomputed on the fly
  /// passing null for the current orientation (as placement does), so it is constrained only
  /// by real network topology — not by the solid walls/ground the block rests against, nor by
  /// the clicked-face narrowing applied at placement. Otherwise a passthrough placed on the
  /// ground (snapping to "ud") would store a single-orientation set in its BE and lock.</para>
  /// <para>Thin-profile pipes cannot float on the side when the only support is the ground
  /// below, so they keep the placement-time restriction: the narrowed cycle stored on the BE
  /// (falling back to a topology recompute for multiblock BEs that never store it).</para>
  /// </summary>
  protected string[] GetWrenchOrientations(IWorldAccessor world, BlockPos pos)
  {
    if (Type == null)
      return [];

    if (IsFullCube)
      return ComputeValidOrientations(world.BlockAccessor, pos, Type, null);

    return
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityNetworkNode beNet
      && beNet.PossibleOrientations is { Length: > 0 }
      ? beNet.PossibleOrientations
      : ComputeValidOrientations(world.BlockAccessor, pos, Type, null);
  }

  /// <summary>
  /// True when the block fills its whole cell (default full-cube collision box, i.e. no
  /// custom <c>collisionboxes</c> in JSON). Such blocks are supported in any orientation,
  /// so the wrench rotates them through the full topology-only cycle; thin-profile blocks
  /// instead keep the orientation restriction chosen at placement. Virtual so a subclass
  /// can opt in/out explicitly regardless of its collision geometry.
  /// </summary>
  protected virtual bool IsFullCube =>
    CollisionBoxes is { Length: 1 } boxes && IsFullCubeBox(boxes[0]);

  private static bool IsFullCubeBox(Cuboidf b) =>
    b.X1 <= 0 && b.Y1 <= 0 && b.Z1 <= 0 && b.X2 >= 1 && b.Y2 >= 1 && b.Z2 >= 1;

  /// <summary>
  /// Whether a wrench can rotate this block at <paramref name="pos"/> right now.
  /// False when only one orientation is valid (rotation would be a no-op).
  /// Override to add further restrictions (e.g. the blower locks while coupled
  /// to an axle).
  /// </summary>
  protected virtual bool CanWrenchRotate(IWorldAccessor world, BlockPos pos) =>
    GetWrenchOrientations(world, pos).Length > 1;
  #endregion

  #region Interaction help
  /// <summary>
  /// Appends the "Rotate" wrench hint to the placed-block help, but only when the
  /// block can actually be rotated here (more than one valid orientation, and any
  /// subclass restriction such as the blower's axle lock is satisfied).
  /// </summary>
  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    WorldInteraction[] baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    if (!CanWrenchRotate(world, selection.Position))
      return baseHelp;

    return baseHelp
      .Append(
        new WorldInteraction
        {
          ActionLangCode = "exlib:blockhelp-rotate",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = ExItems.WrenchStacks(world),
        }
      )
      .ToArray();
  }
  #endregion

  #region Collision and selection boxes
  protected readonly Dictionary<string, Cuboidf[]> collisionBoxesCache = [];
  protected readonly Dictionary<string, Cuboidf[]> selectionBoxesCache = [];

  /// <summary>
  /// Pre-computes rotated collision and selection box arrays for every
  /// (type, orientation) combination so runtime lookups are O(1) dictionary reads.
  /// </summary>
  protected virtual void PrecomputeRotatedBoxes()
  {
    if (CollisionBoxes == null || CollisionBoxes.Length == 0)
      return;
    Vec3d pivot = new(0.5, 0.5, 0.5);

    foreach (var kvp in AllowedOrientations)
    {
      foreach (string orient in kvp.Value)
      {
        GetRotations(orient, out float rotX, out float rotY, out float rotZ);
        string cacheKey = $"{kvp.Key}-{orient}";

        Cuboidf[] rotatedCollision = new Cuboidf[CollisionBoxes.Length];
        for (int i = 0; i < CollisionBoxes.Length; i++)
          rotatedCollision[i] = CollisionBoxes[i]
            .RotatedCopy(rotX, rotY, rotZ, pivot);
        collisionBoxesCache[cacheKey] = rotatedCollision;

        Cuboidf[] baseSelection = SelectionBoxes ?? CollisionBoxes;
        Cuboidf[] rotatedSelection = new Cuboidf[baseSelection.Length];
        for (int i = 0; i < baseSelection.Length; i++)
          rotatedSelection[i] = baseSelection[i]
            .RotatedCopy(rotX, rotY, rotZ, pivot);
        selectionBoxesCache[cacheKey] = rotatedSelection;
      }
    }
  }

  public override Cuboidf[] GetCollisionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    Type != null
    && Orientation != null
    && collisionBoxesCache.TryGetValue(
      $"{Type}-{Orientation}",
      out Cuboidf[]? boxes
    )
      ? boxes
      : base.GetCollisionBoxes(blockAccessor, pos);

  public override Cuboidf[] GetSelectionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    Type != null
    && Orientation != null
    && selectionBoxesCache.TryGetValue(
      $"{Type}-{Orientation}",
      out Cuboidf[]? boxes
    )
      ? boxes
      : base.GetSelectionBoxes(blockAccessor, pos);

  /// <summary>
  /// Returns the Euler rotation angles (in degrees) for the given <paramref name="orientation"/>
  /// string.  Virtual so network-specific subclasses can handle custom orientations
  /// (e.g. sloped canals).
  /// </summary>
  protected virtual void GetRotations(
    string orientation,
    out float rotX,
    out float rotY,
    out float rotZ
  )
  {
    rotX = 0;
    rotY = 0;
    rotZ = 0;

    switch (orientation)
    {
      // Base states (zero rotation)
      case "n":
      case "ns":
      case "nw":
      case "wne":
      case "nswe":
        break;

      // Y-axis 90°
      case "e":
      case "we":
      case "ws":
      case "swn":
        rotY = 90;
        break;

      // Y-axis 180°
      case "s":
      case "sn":
      case "se":
      case "esw":
        rotY = 180;
        break;

      // Y-axis 270°
      case "w":
      case "ew":
      case "en":
      case "nes":
        rotY = 270;
        break;

      // X-axis 90°
      case "u":
      case "ud":
      case "uwe":
        rotX = 90;
        break;

      // X-axis 270°
      case "d":
      case "du":
      case "dwe":
        rotX = 270;
        break;

      // Z-axis 90° variants
      case "dn":
      case "dnu":
      case "nsud":
        rotZ = 90;
        break;
      case "dw":
      case "dwu":
      case "weud":
        rotZ = 90;
        rotY = 90;
        break;
      case "ds":
      case "dsu":
        rotZ = 90;
        rotY = 180;
        break;
      case "de":
      case "deu":
        rotZ = 90;
        rotY = 270;
        break;

      // Z-axis 270° variants
      case "un":
        rotZ = 270;
        break;
      case "uw":
        rotZ = 270;
        rotY = 90;
        break;
      case "us":
        rotZ = 270;
        rotY = 180;
        break;
      case "ue":
        rotZ = 270;
        rotY = 270;
        break;

      // Complex multi-axis
      case "uns":
        rotX = 90;
        rotZ = 90;
        break;
      case "dns":
        rotZ = 90;
        rotX = 270;
        break;
    }
  }
  #endregion

  #region Drops
  public override BlockDropItemStack[] GetDropsForHandbook(
    ItemStack handbookStack,
    IPlayer forPlayer
  ) => [new BlockDropItemStack(handbookStack)];

  /// <summary>
  /// Always drops a canonical (fallback-orientation) item so the player receives
  /// one consistent item type regardless of in-world orientation variant.
  /// </summary>
  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    string fallback = GetFallbackOrientation(Type);
    AssetLocation loc = CodeWithVariant("orientation", fallback);
    return [new ItemStack(worldMap.GetBlock(loc) ?? this)];
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) =>
    GetDrops(world, pos, null)[0];

  /// <summary>
  /// Includes the node's material/rock/brick variant in its display name (e.g.
  /// "Iron Piping (Straight)", "Granite Molten Canal"). The placed-block name
  /// goes through <c>OnPickBlock</c> → <c>GetName</c>, so this covers both.
  /// </summary>
  public override string GetHeldItemName(ItemStack itemStack) =>
    ExBlockNames.Decorate(this, base.GetHeldItemName(itemStack));
  #endregion

  #region Abstracts and virtuals
  /// <summary>Identifies which block network type this block belongs to (e.g. "gas", "molten").</summary>
  public abstract string NetworkType { get; }

  /// <summary>
  /// Maps shape-type strings (e.g. "straight", "bend") to their valid orientation strings.
  /// Used for placement, wrench rotation, and collision-box pre-computation.
  /// </summary>
  public abstract Dictionary<string, string[]> AllowedOrientations { get; }

  /// <summary>Returns the default orientation for drops/handbook entries for the given type.</summary>
  protected abstract string GetFallbackOrientation(string? type);

  /// <summary>
  /// When <c>true</c>, this node acts as a fixed endpoint and is excluded from the
  /// standard neighbour-discovery traversal (e.g. an output machine port).
  /// </summary>
  public virtual bool IsNetworkEndPoint => false;

  /// <summary>
  /// Returns <c>true</c> when a connection to <paramref name="neighborBlock"/> on
  /// <paramref name="face"/> is valid even though the neighbour is not a network block
  /// (used to suppress false leak detection, e.g. a machine housing that seals the pipe).
  /// </summary>
  public virtual bool IsValidNonNetworkConnection(
    Block neighborBlock,
    BlockFacing face
  ) => false;

  /// <summary>
  /// Returns <c>true</c> if <see cref="Orientation"/> contains the single-char code
  /// for <paramref name="face"/>.
  /// </summary>
  public virtual bool HasConnectorAt(BlockFacing face) =>
    Orientation != null && Orientation.Contains(face.Code[0]);

  /// <summary>Returns all block faces that have a network connector, or <c>null</c> if unorientated.</summary>
  public virtual BlockFacing[]? GetConnectorFaces()
  {
    if (Orientation == null)
      return null;

    return Orientation
      .Select(c => BlockNetworkModSystem.SideToFace(c.ToString())!)
      .Where(f => f != null)
      .ToArray();
  }

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace);

  /// <summary>
  /// Recomputes the valid orientations for the block at <paramref name="pos"/>, updates
  /// the block entity's <c>PossibleOrientations</c>, and exchanges the in-world block if
  /// the current orientation is no longer valid.
  /// </summary>
  public virtual void RecalculateAndSyncOrientations(
    IWorldAccessor world,
    BlockPos pos
  )
  {
    if (
      world.BlockAccessor.GetBlock(pos) is not BlockNetworkNode netBlock
      || netBlock.Type == null
    )
      return;
    if (
      world.BlockAccessor.GetBlockEntity(pos)
      is not BlockEntityNetworkNode beNet
    )
      return;

    string[] finalChoices = netBlock.ComputeValidOrientations(
      world.BlockAccessor,
      pos,
      netBlock.Type,
      netBlock.Orientation
    );

    if (finalChoices.Length == 0)
    {
      world.BlockAccessor.BreakBlock(pos, null);
      return;
    }

    beNet.Orientation = Orientation;
    beNet.PossibleOrientations = finalChoices;
    beNet.MarkDirty(true);

    if (
      netBlock.Orientation != null
      && !finalChoices.Contains(netBlock.Orientation)
    )
    {
      AssetLocation newCode = netBlock.CodeWithVariant(
        "orientation",
        finalChoices[0]
      );
      Block? nextBlock = world.GetBlock(newCode);
      if (nextBlock != null && nextBlock.BlockId != netBlock.BlockId)
      {
        world.BlockAccessor.ExchangeBlock(nextBlock.BlockId, pos);
        world.BlockAccessor.MarkBlockDirty(pos);
      }
    }
  }
  #endregion
}
