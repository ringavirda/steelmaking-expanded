using System;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// The steam condenser's logic. Each tick it passes water through its west/east faces —
/// the fuller water side is the inlet, the other the outlet (the inlet's pressure is
/// preserved downstream) — running as a water conduit whether or not steam is present.
/// When steam is drawn from the north line it condenses back to its (much smaller) hot-water
/// volume and merges into that through-flow.
/// <para>
/// An unplumbed face leaks: with no outlet the backed-up water line (plus any condensate)
/// sprays out the open outlet face; with no water line at all, drawn steam simply vents out
/// as gas.
/// </para>
/// </summary>
[EntityRegister]
public class BlockEntitySteamCondenser : BlockEntity
{
  private long _tickId;
  private BlockNetworkModSystem? _netSystem;

  // Client-display mirror, synced via the tree.
  private bool _condensing;

  private BlockSteamCondenser? CondenserBlock => Block as BlockSteamCondenser;

  /// <summary>
  /// The pipe network connected across one of the condenser's connector faces, or <c>null</c>
  /// when the adjacent pipe has no connector facing back (so it is not actually plumbed in).
  /// </summary>
  private PipeNetwork? ConnectedNetwork(BlockFacing connectorFace) =>
    _netSystem?.GetConnectedNetworkAcross(
      Api.World.BlockAccessor,
      Pos,
      connectorFace
    ) as PipeNetwork;

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

    PipeNetwork? steamNet = ConnectedNetwork(CondenserBlock.SteamInletFace);
    BlockFacing faceA = CondenserBlock.SideAFace;
    BlockFacing faceB = CondenserBlock.SideBFace;
    PipeNetwork? netA = ConnectedNetwork(faceA);
    PipeNetwork? netB = ConnectedNetwork(faceB);

    bool condensing = Process(steamNet, netA, faceA, netB, faceB, dt, ba);

    if (condensing != _condensing)
    {
      _condensing = condensing;
      MarkDirty(true);
    }
  }

  /// <summary>
  /// Runs the water line through the two water faces (W↔E) — the fuller side is the inlet,
  /// the other the outlet — and condenses steam from the north line into that through-flow.
  /// With no outlet the line backs up and leaks out the open outlet face (carrying any
  /// condensate); with no water line plumbed at all, drawn steam just vents out as gas.
  /// The inlet's liquid pressure is preserved downstream so a pumped line keeps its head.
  /// Returns <c>true</c> if any steam was condensed this tick (for the HUD).
  /// </summary>
  private bool Process(
    PipeNetwork? steamNet,
    PipeNetwork? netA,
    BlockFacing faceA,
    PipeNetwork? netB,
    BlockFacing faceB,
    float dt,
    IBlockAccessor ba
  )
  {
    // Only water-capable sides count for the water line; a gas run is unplumbed for water.
    PipeNetwork? wa = CanTakeWater(netA) ? netA : null;
    PipeNetwork? wb = CanTakeWater(netB) ? netB : null;

    bool steam = HasSteam(steamNet);
    float steamTemp = steam
      ? steamNet!.State!.Temperature
      : PpexValues.BoilingPoint;

    // Both water faces on the same run (a loop): nothing to pass, just drop in condensate.
    if (wa != null && ReferenceEquals(wa, wb))
    {
      if (!steam)
        return false;
      float looped =
        steamNet!.TryConsumeGas(PpexValues.CondenserSteamPerSecond * dt, ba)
        / PpexValues.SteamExpansionFactor;
      if (looped <= 0f)
        return false;
      InjectWater(wa, looped, steamTemp, wa.State?.Pressure ?? 0f, ba);
      return true;
    }

    // Inlet = the fuller water side; outlet = the other (each paired with its face).
    PipeNetwork? inNet,
      outNet;
    BlockFacing outFace;
    if (LiquidVolumeOf(wa) >= LiquidVolumeOf(wb))
      (inNet, outNet, outFace) = (wa, wb, faceB);
    else
      (inNet, outNet, outFace) = (wb, wa, faceA);

    // No water line plumbed on either side — there is nowhere to condense steam into, so any
    // steam drawn just vents out of the condenser as gas (capped at the pipe-leak rate).
    if (inNet == null && outNet == null)
    {
      if (!steam)
        return false;
      float ventGas = steamNet!.TryConsumeGas(
        Math.Min(
          PpexValues.CondenserSteamPerSecond * dt,
          PpexValues.GasLeakRate
        ),
        ba
      );
      if (ventGas > 0f)
      {
        ExParticles.GasVent(
          Api.World,
          Pos,
          outFace,
          steamNet.State!.MediumType
        );
        ExSounds.PlayAt(
          Api.World,
          Pos,
          ExSounds.Swoosh,
          range: 24f,
          volume: 0.6f
        );
      }
      return false;
    }

    // Steam condenses into its (much smaller) hot-water volume, merged into the line below.
    float condensed = 0f;
    if (steam)
    {
      float used = steamNet!.TryConsumeGas(
        PpexValues.CondenserSteamPerSecond * dt,
        ba
      );
      condensed = used / PpexValues.SteamExpansionFactor;
    }

    // No outlet piped: the water line backs up and leaks out the open outlet face. Drain
    // what the open end can shed from the inlet, add the condensate, and spray it all out
    // (the water is lost). Capped at the pipe water-leak rate like any open-ended run.
    if (outNet == null)
    {
      float drained =
        inNet != null
          ? inNet.TryConsumeLiquid(PpexValues.LiquidLeakRate * dt, ba)
          : 0f;
      if (drained + condensed <= 0f)
        return false;
      ExParticles.WaterJet(Api.World, Pos, outFace);
      ExSounds.SplashSound(Api.World, Pos);
      return condensed > 0f;
    }

    // Reserve outlet space for the condensate first, then move as much through-flow as fits.
    float outFree = Math.Max(
      0f,
      outNet.Nodes.Count * PpexValues.LitresPerPipe - LiquidVolumeOf(outNet)
    );
    float condIn = Math.Min(condensed, outFree);
    float passSpace = outFree - condIn;

    float inTemp = inNet?.State?.Temperature ?? 20f;
    float inPress = inNet?.State?.Pressure ?? 0f;
    float move =
      inNet != null && passSpace > 0f
        ? inNet.TryConsumeLiquid(
          Math.Min(PpexValues.CondenserWaterThroughput * dt, passSpace),
          ba
        )
        : 0f;

    float total = condIn + move;
    if (total <= 0f)
      return false;

    float mixedTemp = (move * inTemp + condIn * steamTemp) / total;
    mixedTemp = Math.Clamp(mixedTemp, 20f, PpexValues.BoilingPoint - 1f);
    outNet.TryProduceLiquid(total, mixedTemp, inPress, ba);
    return condIn > 0f;
  }

  /// <summary>Adds water to a network as hot (sub-boiling) liquid at the given pressure.</summary>
  private static void InjectWater(
    PipeNetwork net,
    float amount,
    float temp,
    float pressure,
    IBlockAccessor ba
  )
  {
    net.TryProduceLiquid(
      amount,
      Math.Clamp(temp, 20f, PpexValues.BoilingPoint - 1f),
      pressure,
      ba
    );
  }

  /// <summary>Litres of water in <paramref name="net"/>, or 0 if it carries gas / is empty.</summary>
  private static float LiquidVolumeOf(PipeNetwork? net) =>
    net?.State is { IsLiquid: true } s ? s.Volume : 0f;

  /// <summary>Whether <paramref name="net"/> can receive water — a water run or one that
  /// hasn't claimed a medium yet (a gas run would reject it).</summary>
  private static bool CanTakeWater(PipeNetwork? net) =>
    net != null
    && (
      net.State == null
      || net.State.IsLiquid
      || net.State.MediumType.Length == 0
    );

  private static bool HasSteam(PipeNetwork? net) =>
    net?.State != null
    && net.State.MediumType == "Steam"
    && net.State.Volume > 0f;

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
