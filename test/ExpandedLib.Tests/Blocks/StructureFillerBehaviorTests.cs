using System.Collections.Generic;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Behaviour-capable fillers: a <c>fillerOffsets</c> cell can declare behaviours the invisible filler
/// hosts on the principal's behalf, such as a mechanical-power intake at the cell where an axle couples.
/// Covers parsing the declarations, rotating their connector faces into the placed orientation, the save
/// tree, the runtime hosting path, the <see cref="IMechanicalPowerBlock"/> glue that turns a hosting cell
/// into an MP connector, and <see cref="BEBehaviorMPFillerPort"/> itself.
/// </summary>
public class StructureFillerBehaviorTests {
  #region Parsing

  [Fact]
  public void A_cell_without_behaviors_parses_as_null() {
    var off = Assert.Single(ReadOffsets("[{ \"x\": 0, \"y\": 0, \"z\": 0 }]"));
    Assert.Null(off.Behaviors);
  }

  [Fact]
  public void A_behavior_parses_its_code_and_face() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": ["
          + "{ \"code\": \"exlib.BEBehaviorMPFillerPort\", \"face\": \"west\" } ] }]"
      )
    );

    var b = Assert.Single(off.Behaviors!);
    Assert.Equal("exlib.BEBehaviorMPFillerPort", b.Code);
    Assert.Equal(BlockFacing.WEST, b.ConnectorFace);
    Assert.Null(b.Properties);
  }

  [Fact]
  public void A_behavior_without_a_face_has_a_null_connector() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"code\": \"test.X\" } ] }]"
      )
    );
    Assert.Null(Assert.Single(off.Behaviors!).ConnectorFace);
  }

  [Fact]
  public void A_behavior_keeps_its_declared_properties() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"code\": \"test.X\", "
          + "\"properties\": { \"resistance\": 0.75 } } ] }]"
      )
    );
    var b = Assert.Single(off.Behaviors!);
    Assert.Equal(0.75f, b.Properties!["resistance"].AsFloat(), 3);
  }

  [Fact]
  public void Multiple_behaviors_on_one_cell_all_parse() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": ["
          + "{ \"code\": \"a\", \"face\": \"west\" }, { \"code\": \"b\", \"face\": \"east\" } ] }]"
      )
    );
    Assert.Equal(2, off.Behaviors!.Length);
    Assert.Equal("a", off.Behaviors[0].Code);
    Assert.Equal(BlockFacing.EAST, off.Behaviors[1].ConnectorFace);
  }

  [Fact]
  public void A_behavior_missing_a_code_is_skipped() {
    var off = Assert.Single(
      ReadOffsets(
        "[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"face\": \"west\" } ] }]"
      )
    );
    Assert.Null(off.Behaviors);
  }

  [Fact]
  public void An_empty_behaviors_array_is_null() {
    var off = Assert.Single(
      ReadOffsets("[{ \"x\": 0, \"y\": 0, \"z\": 0, \"behaviors\": [] }]")
    );
    Assert.Null(off.Behaviors);
  }

  #endregion

  #region Face rotation

  [Theory]
  [InlineData(0, "west")]
  [InlineData(90, "south")]
  [InlineData(180, "east")]
  [InlineData(270, "north")]
  public void The_connector_face_rotates_with_the_structure_angle(
    int angle,
    string expected
  ) {
    var host = new Host(
      Offsets(
        "[{ \"x\": 1, \"y\": 0, \"z\": 0, \"behaviors\": ["
          + "{ \"code\": \"exlib.BEBehaviorMPFillerPort\", \"face\": \"west\" } ] }]"
      )
    );

    var cell = Assert.Single(
      StructureFillers.FootprintCells(host, new BlockPos(0, 0, 0), angle)
    );
    Assert.Equal(
      BlockFacing.FromCode(expected),
      Assert.Single(cell.Behaviors!).ConnectorFace
    );
  }

  [Fact]
  public void A_faceless_behavior_passes_through_rotation_unchanged() {
    var host = new Host(
      Offsets(
        "[{ \"x\": 1, \"y\": 0, \"z\": 0, \"behaviors\": [ { \"code\": \"test.X\" } ] }]"
      )
    );
    var cell = Assert.Single(
      StructureFillers.FootprintCells(host, new BlockPos(0, 0, 0), 90)
    );
    var b = Assert.Single(cell.Behaviors!);
    Assert.Equal("test.X", b.Code);
    Assert.Null(b.ConnectorFace);
  }

  #endregion

  #region Serialization

  [Fact]
  public void Hosted_behaviors_round_trip_through_the_save_tree() {
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(3, 4, 5),
      HostedBehaviors =
      [
        new FillerBehavior(
          "exlib.BEBehaviorMPFillerPort",
          BlockFacing.EAST,
          Props("{ \"resistance\": 0.75 }")
        ),
      ],
    };

    var restored = RoundTrip(be);

    var b = Assert.Single(restored.HostedBehaviors!);
    Assert.Equal("exlib.BEBehaviorMPFillerPort", b.Code);
    Assert.Equal(BlockFacing.EAST, b.ConnectorFace);
    Assert.Equal(0.75f, b.Properties!["resistance"].AsFloat(), 3);
  }

  [Fact]
  public void A_faceless_propertyless_behavior_round_trips_as_such() {
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(1, 1, 1),
      HostedBehaviors = [new FillerBehavior("test.X", null, null)],
    };

    var b = Assert.Single(RoundTrip(be).HostedBehaviors!);
    Assert.Equal("test.X", b.Code);
    Assert.Null(b.ConnectorFace);
    Assert.Null(b.Properties);
  }

  [Fact]
  public void A_filler_with_no_hosted_behaviors_stays_null_across_the_tree() {
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(1, 2, 3),
    };
    Assert.Null(RoundTrip(be).HostedBehaviors);
  }

  #endregion

  #region Runtime hosting (scenario)

  [Fact]
  public void A_placed_filler_creates_configures_and_initialises_its_behaviors() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(2, 3, 4);
    var principal = new BlockPos(2, 3, 1);
    var be = new BlockEntityStructureFiller {
      Principal = principal,
      HostedBehaviors =
      [
        new FillerBehavior(
          "test.Tracking",
          BlockFacing.EAST,
          Props("{ \"k\": 7 }")
        ),
      ],
    };
    world.Place(pos, filler, be);
    world
      .Api.ClassRegistry.CreateBlockEntityBehavior(
        Arg.Any<BlockEntity>(),
        "test.Tracking"
      )
      .Returns(ci => new TrackingHostedBehavior(ci.Arg<BlockEntity>()));

    world.Initialize(be); // the placement/load path that recreates hosted behaviours

    var hosted = be.GetBehavior<TrackingHostedBehavior>();
    Assert.NotNull(hosted);
    Assert.True(hosted!.Initialized); // its own Initialize ran
    Assert.Equal(principal, hosted.Principal); // principal link
    Assert.Equal(BlockFacing.EAST, hosted.Face); // rotated connector face
    Assert.Equal(7, hosted.Props!["k"].AsInt()); // declared properties
  }

  [Fact]
  public void An_unknown_behavior_class_is_skipped_without_throwing() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(0, 0, -2),
      HostedBehaviors = [new FillerBehavior("test.DoesNotExist", null, null)],
    };
    world.Place(pos, filler, be);

    world.Initialize(be); // class registry returns null for the unknown code

    Assert.Null(be.GetBehavior<TrackingHostedBehavior>());
  }

  [Fact]
  public void Hosted_behaviors_arriving_via_a_later_sync_update_are_created() {
    // The filler BE is created and Initialized with no hosted behaviours, then the principal assigns
    // them and they arrive as a sync update. Initialize does not run again, so FromTreeAttributes has
    // to create them.
    var (world, filler) = NewWorld();
    var pos = new BlockPos(5, 6, 7);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(5, 6, 4),
    };
    world.Place(pos, filler, be);
    world.Initialize(be);
    Assert.Null(be.GetBehavior<TrackingHostedBehavior>()); // none yet

    world
      .Api.ClassRegistry.CreateBlockEntityBehavior(
        Arg.Any<BlockEntity>(),
        "test.Tracking"
      )
      .Returns(ci => new TrackingHostedBehavior(ci.Arg<BlockEntity>()));

    // Build the sync tree the principal's assignment would produce.
    var update = new TreeAttribute();
    new BlockEntityStructureFiller {
      Pos = pos,
      Block = filler,
      Principal = new BlockPos(5, 6, 4),
      HostedBehaviors =
      [
        new FillerBehavior("test.Tracking", BlockFacing.WEST, null),
      ],
    }.ToTreeAttributes(update);

    be.FromTreeAttributes(update, world.World);

    var hosted = be.GetBehavior<TrackingHostedBehavior>();
    Assert.NotNull(hosted);
    Assert.True(hosted!.Initialized);
    Assert.Equal(BlockFacing.WEST, hosted.Face);
  }

  #endregion

  #region Mechanical-power connector glue

  [Fact]
  public void A_hosting_cell_accepts_an_axle_on_either_end_of_its_port_axis() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(0, 0, -2),
    };
    world.Place(pos, filler, be);
    world.Initialize(be);
    // Stands in for the real hosted port: west-facing metadata plus an MP behaviour on the BE.
    be.HostedBehaviors =
    [
      new FillerBehavior(
        "exlib.BEBehaviorMPFillerPort",
        BlockFacing.WEST,
        null
      ),
    ];
    be.Behaviors.Add(new BEBehaviorMPFillerPort(be));

    // An axle couples along the axis, so both ends of the declared face connect,
    Assert.True(HasMechConnector(filler, world, pos, BlockFacing.WEST));
    Assert.True(HasMechConnector(filler, world, pos, BlockFacing.EAST));
    // but a perpendicular face does not.
    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.NORTH));
    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.SOUTH));
  }

  [Fact]
  public void A_cell_with_no_mp_behavior_is_not_a_connector() {
    var (world, filler) = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityStructureFiller {
      Principal = new BlockPos(0, 0, -2),
      // Face metadata present, but no MP behaviour hosted, so not an MP connector.
      HostedBehaviors =
      [
        new FillerBehavior(
          "exlib.BEBehaviorMPFillerPort",
          BlockFacing.WEST,
          null
        ),
      ],
    };
    world.Place(pos, filler, be);
    world.Initialize(be);

    Assert.False(HasMechConnector(filler, world, pos, BlockFacing.WEST));
    Assert.Null(filler.GetNetwork(world.World, pos));
  }

  #endregion

  #region MP filler-port behaviour

  [Fact]
  public void The_port_takes_its_face_and_resistance_from_configuration() {
    var (world, filler) = NewWorld();
    var be = new BlockEntityStructureFiller();
    world.Place(new BlockPos(0, 0, 0), filler, be);

    var port = new BEBehaviorMPFillerPort(be);
    port.ConfigureFromFiller(
      null,
      BlockFacing.EAST,
      Props("{ \"resistance\": 0.9 }")
    );

    Assert.Equal(BlockFacing.EAST, port.PortFacing);
    Assert.Equal(0.9f, port.GetResistance(), 3);
  }

  [Fact]
  public void The_port_defaults_to_north_and_the_default_resistance() {
    var (world, filler) = NewWorld();
    var be = new BlockEntityStructureFiller();
    world.Place(new BlockPos(0, 0, 0), filler, be);

    var port = new BEBehaviorMPFillerPort(be);

    Assert.Equal(BlockFacing.NORTH, port.PortFacing);
    Assert.Equal(
      BEBehaviorMPFillerPort.DefaultResistance,
      port.GetResistance(),
      3
    );
  }

  [Theory]
  [InlineData("west", -1, 0, 0)]
  [InlineData("east", -1, 0, 0)]
  [InlineData("north", 0, 0, -1)]
  [InlineData("south", 0, 0, -1)]
  public void The_port_signs_its_axis_not_its_facing(
    string face,
    int sx,
    int sy,
    int sz
  ) {
    // Opposite facings share one axle line, so they must carry the same sign or a row of ports
    // would counter-rotate against itself.
    var (world, filler) = NewWorld();
    var be = new BlockEntityStructureFiller();
    world.Place(new BlockPos(0, 0, 0), filler, be);

    var port = new BEBehaviorMPFillerPort(be);
    port.ConfigureFromFiller(null, BlockFacing.FromCode(face), null);
    port.SetOrientations();

    Assert.Equal(BlockFacing.FromCode(face), port.OutFacingForNetworkDiscovery);
    Assert.Equal(new[] { sx, sy, sz }, port.AxisSign);
  }

  [Fact]
  public void The_port_contributes_no_mesh() {
    // The filler is invisible and the principal renders the rotor, so the port must not tesselate.
    var (world, filler) = NewWorld();
    var be = new BlockEntityStructureFiller();
    world.Place(new BlockPos(0, 0, 0), filler, be);

    var port = new BEBehaviorMPFillerPort(be);

    Assert.False(
      port.OnTesselation(
        Substitute.For<ITerrainMeshPool>(),
        Substitute.For<ITesselatorAPI>()
      )
    );
  }

  #endregion

  #region Helpers

  private static (TestWorld world, BlockStructureFiller filler) NewWorld() {
    var world = new TestWorld();
    var filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      70
    );
    world.Register(filler);
    return (world, filler);
  }

  private static bool HasMechConnector(
    BlockStructureFiller filler,
    TestWorld world,
    BlockPos pos,
    BlockFacing face
  ) =>
#if GAME_GE_1_22
    filler.HasMechPowerConnectorAt(world.World, pos, face, null!);
#else
    filler.HasMechPowerConnectorAt(world.World, pos, face);
#endif

  private static JsonObject Offsets(string json) => new(JArray.Parse(json));

  private static JsonObject Props(string json) => new(JToken.Parse(json));

  private static List<FillerOffset> ReadOffsets(string json) =>
    StructureFillers.ReadOffsets(Offsets(json));

  private static BlockEntityStructureFiller RoundTrip(
    BlockEntityStructureFiller be
  ) {
    var world = new TestWorld();
    var block = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      70
    );
    var pos = new BlockPos(1, 2, 3);
    be.Pos = pos;
    be.Block = block;

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var restored = new BlockEntityStructureFiller { Pos = pos, Block = block };
    restored.FromTreeAttributes(tree, world.World);
    return restored;
  }

  private sealed class Host(JsonObject? offsets) : IFillerHost {
    public JsonObject? FillerOffsets { get; } = offsets;
  }

  /// <summary>A non-MP hosted behaviour that records how the filler configured and initialised it.</summary>
  private sealed class TrackingHostedBehavior(BlockEntity be)
    : BlockEntityBehavior(be),
      IFillerHostedBehavior {
    public BlockPos? Principal;
    public BlockFacing? Face;
    public JsonObject? Props;
    public bool Initialized;

    public void ConfigureFromFiller(
      BlockPos? principal,
      BlockFacing? connectorFace,
      JsonObject? properties
    ) {
      Principal = principal;
      Face = connectorFace;
      Props = properties;
    }

    public override void Initialize(ICoreAPI api, JsonObject properties) {
      base.Initialize(api, properties);
      Initialized = true;
    }
  }

  #endregion
}
