using System;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// The steam condenser's logic. Each tick it looks for steam on either horizontal
/// side; if found and the north (coolant) line has water, it pulls steam and cooling
/// water, blends them into hot water, and pushes the result out the remaining side.
/// Condensation only runs while coolant water is available.
/// </summary>
[EntityRegister]
public class BlockEntitySteamCondenser : BlockEntity
{
  private long _tickId;
  private BlockNetworkModSystem? _netSystem;

  // Client-display mirror, synced via the tree.
  private bool _condensing;

  private BlockSteamCondenser? CondenserBlock => Block as BlockSteamCondenser;

  private PipeNetwork? NetworkAt(BlockPos pos) =>
    _netSystem?.GetNetworkAt(pos) as PipeNetwork;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
    {
      _netSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
      _tickId = RegisterGameTickListener(OnTick, 1000);
    }
  }

  private void OnTick(float dt)
  {
    if (CondenserBlock == null)
      return;

    var ba = Api.World.BlockAccessor;

    PipeNetwork? netA = NetworkAt(CondenserBlock.SideAPos(Pos));
    PipeNetwork? netB = NetworkAt(CondenserBlock.SideBPos(Pos));
    PipeNetwork? coolant = NetworkAt(CondenserBlock.CoolantPos(Pos));

    // Pick whichever horizontal side carries steam; the other becomes the output.
    // Guard against the two sides being the same network (a pipe loop): we must not
    // feed hot water back into the network we are pulling steam from.
    PipeNetwork? steamNet = null;
    PipeNetwork? outputNet = null;
    if (HasSteam(netA))
    {
      steamNet = netA;
      outputNet = ReferenceEquals(netB, netA) ? null : netB;
    }
    else if (HasSteam(netB))
    {
      steamNet = netB;
      outputNet = ReferenceEquals(netA, netB) ? null : netA;
    }

    bool condensing = false;

    if (
      steamNet?.State != null
      && outputNet != null
      && coolant?.State != null
      && coolant.State.WaterVolume > 0f
    )
    {
      float steamTemp = steamNet.State.SourceTemperature;

      // Pull and condense steam (m³ → litres of hot water).
      float used = steamNet.TryConsumeGas(
        PpexValues.CondenserSteamPerSecond * dt,
        ba
      );
      if (used > 0f)
      {
        float condensed = used * PpexValues.BoilerWaterPerSteam;

        // Draw cooling water from the north line and blend the temperatures.
        float coolTemp = coolant.State.WaterTemperature;
        float cool = coolant.TryConsumeLiquid(
          condensed * PpexValues.CondenserCoolantRatio,
          ba
        );

        float total = condensed + cool;
        if (total > 0f)
        {
          float mixedTemp = (condensed * steamTemp + cool * coolTemp) / total;
          // It leaves as hot *water*, never steam.
          mixedTemp = Math.Clamp(mixedTemp, 20f, PpexValues.BoilingPoint - 1f);
          outputNet.TryProduceLiquid(total, mixedTemp, 0f, ba);
          condensing = true;
        }
      }
    }

    if (condensing != _condensing)
    {
      _condensing = condensing;
      MarkDirty(true);
    }
  }

  private static bool HasSteam(PipeNetwork? net) =>
    net?.State != null
    && net.State.GasType == "Steam"
    && net.State.CurrentVolume > 0f;

  public override void OnBlockRemoved()
  {
    if (_tickId != 0)
      UnregisterGameTickListener(_tickId);
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    if (_tickId != 0)
      UnregisterGameTickListener(_tickId);
    base.OnBlockUnloaded();
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("condensing", _condensing);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _condensing = tree.GetBool("condensing");
  }

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  )
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(
      Lang.Get(
        _condensing
          ? "ppex:condenser-info-condensing"
          : "ppex:condenser-info-idle"
      )
    );
  }
}
