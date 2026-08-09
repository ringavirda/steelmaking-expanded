using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Datastructures;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>Exposes the protected persistence hooks on <see cref="BlockEntityPipe"/> for a
/// round-trip test (the save/reload path that lost pipe contents in past regressions).</summary>
internal sealed class TestableBlockEntityPipe : BlockEntityPipe {
  public void Write(ITreeAttribute tree, object? state) =>
    SerializeNetworkState(tree, state);

  public object? Read(ITreeAttribute tree) => DeserializeNetworkState(tree);

  /// <summary>The look-at text this pipe would print, for the display tests.</summary>
  public string Info() {
    var sb = new System.Text.StringBuilder();
    GetBlockInfo(null!, sb);
    return sb.ToString();
  }
}

public class PipeSerializationTests {
  [Fact]
  public void Network_state_survives_a_save_reload_round_trip() {
    var be = new TestableBlockEntityPipe();
    var tree = new TreeAttribute();
    var state = new PipeNetworkState {
      Volume = 123f,
      MaxVolume = 300f,
      Temperature = 88f,
      MediumType = "Steam",
      OpeningsCount = 2,
      FlowRate = 4.5f,
      Pressure = 1.7f,
      FeedPressure = 2.3f,
    };

    be.Write(tree, state);
    var restored = be.Read(tree) as PipeNetworkState;

    Assert.NotNull(restored);
    Assert.Equal(123f, restored!.Volume, 3);
    Assert.Equal(300f, restored.MaxVolume, 3);
    Assert.Equal(88f, restored.Temperature, 3);
    Assert.Equal("Steam", restored.MediumType);
    Assert.Equal(2, restored.OpeningsCount);
    Assert.Equal(4.5f, restored.FlowRate, 3);
    Assert.Equal(1.7f, restored.Pressure, 3);
    Assert.Equal(2.3f, restored.FeedPressure, 3);
  }

  [Fact]
  public void Empty_network_state_round_trips_to_null() {
    var be = new TestableBlockEntityPipe();
    var tree = new TreeAttribute();
    be.Write(tree, new PipeNetworkState { Volume = 0f });

    Assert.Null(be.Read(tree)); // nothing worth persisting for an empty run
  }

  /// <summary>
  /// Builds a pipe whose client display fields carry <paramref name="volume"/> standing and
  /// <paramref name="flow"/> passing through, the way a synced tree would.
  /// </summary>
  private static TestableBlockEntityPipe Displaying(
    float volume,
    float flow,
    string medium
  ) {
    // The display fields directly: FromTreeAttributes needs a live world to reach them.
    var be = new TestableBlockEntityPipe();
    ReflectionHelpers.SetField(be, "_clientVolume", volume);
    ReflectionHelpers.SetField(be, "_clientMaxVolume", 30f);
    ReflectionHelpers.SetField(be, "_clientFlowRate", flow);
    ReflectionHelpers.SetProperty(be, nameof(be.Medium), medium);
    ReflectionHelpers.SetProperty(be, nameof(be.Temperature), 20f);
    return be;
  }

  /// <summary>
  /// Regression (player-reported: "the pipes going into the blast furnace read empty even though the
  /// furnace is running"). A run drained as fast as it is fed holds nothing and loses its medium
  /// label with the last litre, and the throughput line was gated on that label - so a tuyere
  /// carrying a furnace's whole blast reported Empty, which is exactly when the figure matters.
  /// </summary>
  // Lang.Get returns the key itself headless, so these assert on WHICH line the pipe emits.
  private const string FlowLine = "ppex:pipe-info-flow";
  private const string EmptyLine = "ppex:pipe-info-empty";

  [Fact]
  public void A_pipe_carrying_gas_reports_its_throughput_even_with_nothing_standing_in_it() {
    string flowing = Displaying(volume: 0f, flow: 24f, medium: "").Info();

    Assert.Contains(FlowLine, flowing);
    Assert.DoesNotContain(EmptyLine, flowing);
  }

  [Fact]
  public void A_pipe_with_nothing_in_it_and_nothing_passing_still_reads_empty() {
    string idle = Displaying(volume: 0f, flow: 0f, medium: "").Info();

    Assert.Contains(EmptyLine, idle);
    Assert.DoesNotContain(FlowLine, idle);
  }

  [Fact]
  public void A_charged_pipe_still_reports_its_flow() {
    string charged = Displaying(volume: 12f, flow: 24f, medium: "Air").Info();

    Assert.Contains(FlowLine, charged);
    Assert.DoesNotContain(EmptyLine, charged);
  }
}
