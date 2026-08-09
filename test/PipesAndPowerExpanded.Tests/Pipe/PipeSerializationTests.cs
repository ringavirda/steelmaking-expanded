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
}
