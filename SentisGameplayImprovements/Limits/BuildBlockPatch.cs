using System;
using System.Collections.Generic;
using System.Reflection;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisGameplayImprovements.DelayedLogic;
using SentisOptimisations;
using Torch.Managers.PatchManager;
using VRage.Network;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// A player placing blocks (<c>MyCubeGrid.BuildBlocksRequest</c>):
    /// <list type="bullet">
    /// <item>on an NPC grid - refused unless an admin, with <c>DisableBuildBlockOnNPC</c>;</item>
    /// <item>a grid with no beacon - the players around are told, 2 seconds later, that the cleanup will remove it
    /// (<c>EnableCheckBeacon</c>);</item>
    /// <item>over the PCU limit of the group with the blocks placed - the player is told (<c>EnabledPcuLimiter</c>).
    /// The block is still placed; <see cref="PcuLimiter"/> enforces the limit.</item>
    /// </list>
    /// </summary>
    [PatchShim]
    public static class BuildBlockPatch
    {
        public static void Patch(PatchContext ctx) => PatchGuard.Run("BuildBlockPatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            MethodInfo method = typeof(MyCubeGrid).GetMethod("BuildBlocksRequest",
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            ctx.GetPattern(method).Prefixes.Add(typeof(BuildBlockPatch).GetMethod(nameof(BuildBlocksRequest),
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static bool BuildBlocksRequest(MyCubeGrid __instance, HashSet<MyCubeGrid.MyBlockLocation> locations)
        {
            if (__instance == null) return true;
            var identityId = SenderIdentity();
            var config = SentisGameplayImprovementsPlugin.Config;
            if (config.DisableBuildBlockOnNPC && identityId != 0 && __instance.IsNpcGrid() && !PlayerUtils.IsAdmin(identityId))
                return false;

            DelayedProcessor.Instance.AddDelayedAction(DateTime.Now.AddSeconds(2), () =>
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    try
                    {
                        if (!__instance.MarkedForClose) CheckBeacon(__instance);
                    }
                    catch (Exception e)
                    {
                        SentisGameplayImprovementsPlugin.Log.Warn(e, "Beacon check failed");
                    }
                }));

            if (!config.EnabledPcuLimiter || identityId == 0 || __instance.IsNpcGrid()) return true;
            try
            {
                var placed = 0;
                foreach (var location in locations)
                    placed += MyDefinitionManager.Static.GetCubeBlockDefinition(location.BlockDefinition)?.PCU ?? 0;
                // the game's count first: never below the exact one, and it costs nothing
                var upperBound = PcuLimiter.GroupPcu(__instance, false, out var hasStatic) + placed;
                var limit = hasStatic ? config.MaxStaticGridPCU : config.MaxDinamycGridPCU;
                if (upperBound <= limit) return true;
                var pcu = PcuLimiter.GroupPcu(__instance, true, out _) + placed;
                if (pcu > limit) PcuLimiter.SendLimitMessage(identityId, pcu, limit, __instance.DisplayName);
            }
            catch (Exception e)
            {
                SentisGameplayImprovementsPlugin.Log.Warn(e, "PCU check on building failed");
            }
            return true;
        }

        /// <summary>The identity of the player who sent the request; 0 when there is none (the server itself).</summary>
        private static long SenderIdentity()
        {
            var sender = MyEventContext.Current.Sender.Value;
            return sender == 0 ? 0 : MySession.Static.Players.TryGetIdentityId(sender);
        }

        private static void CheckBeacon(MyCubeGrid grid)
        {
            if (!SentisGameplayImprovementsPlugin.Config.EnableCheckBeacon) return;
            if (grid.GetFirstBlockOfType<MyBeacon>() != null) return;
            foreach (var subGrid in GridUtils.GetSubGrids(grid))
            {
                if (((MyCubeGrid)subGrid).GetFirstBlockOfType<MyBeacon>() != null) return;
            }
            NotificationUtils.NotifyAllPlayersAround(grid.PositionComp.GetPosition(), 50, "На постройке " + grid.DisplayName + " не установлен маяк");
            NotificationUtils.NotifyAllPlayersAround(grid.PositionComp.GetPosition(), 50, "она будет удалена при следующей очистке");
        }
    }
}
