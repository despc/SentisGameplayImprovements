using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Torch.Managers.PatchManager;
using VRage.Game.ModAPI;
using VRage.Network;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class BuildBlockPatch
    {
        public static void Patch(PatchContext ctx)
        {
            MethodInfo method = typeof(MyCubeGrid).GetMethod("BuildBlocksRequest",
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            ctx.GetPattern(method).Prefixes.Add(typeof(BuildBlockPatch).GetMethod("BuildBlocksRequest",
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static bool BuildBlocksRequest(
            MyCubeGrid __instance,
            HashSet<MyCubeGrid.MyBlockLocation> locations)
        {
            Task.Run(() =>
            {
                if (__instance != null)
                {
                    CheckBeacon(__instance);
                }
                
            });
            if (!SentisGameplayImprovementsPlugin.Config.EnabledPcuLimiter)
                return true;
            if (__instance == null)
            {
                SentisGameplayImprovementsPlugin.Log.Warn("BuildBlocksRequest: Grid is NULL.");
                return true;
            }
            if (MyDefinitionManager.Static.GetCubeBlockDefinition(
                locations.FirstOrDefault().BlockDefinition) == null)
            {
                SentisGameplayImprovementsPlugin.Log.Warn("BuildBlocksRequest: Definition is NULL.");
                return true;
            }
            long identityId = PlayerUtils.GetIdentityByNameOrId(MyEventContext.Current.Sender.Value.ToString())
                .IdentityId;
            var instanceIsStatic = __instance.IsStatic;
            var maxPcu = instanceIsStatic
                ? SentisGameplayImprovementsPlugin.Config.MaxStaticGridPCU
                : SentisGameplayImprovementsPlugin.Config.MaxDinamycGridPCU;
            var subGrids = GridUtils.GetSubGrids(__instance,
                SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids);
            foreach (var myCubeGrid in subGrids)
            {
                if (myCubeGrid.IsStatic)
                {
                    maxPcu = SentisGameplayImprovementsPlugin.Config.MaxStaticGridPCU;
                }
            }
            Task.Run(() =>
            {
                var pcu = GridUtils.GetPCU((IMyCubeGrid)__instance, true,
                    SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids);
                MyCubeBlockDefinition cubeBlockDefinition = MyDefinitionManager.Static.GetCubeBlockDefinition(locations.First().BlockDefinition);
                pcu = pcu + cubeBlockDefinition.PCU;
            
                if (pcu > maxPcu)
                {
                    PcuLimiter.SendLimitMessage(identityId, pcu, maxPcu, __instance.DisplayName);
                }
            });
            
            return true;
        }

        private static void CheckBeacon(MyCubeGrid grid)
        {

            if (!SentisGameplayImprovementsPlugin.Config.EnableCheckBeacon)
            {
                return;
            }
            var myCubeGrids = GridUtils.GetSubGrids(grid);
            foreach (var myCubeGrid in myCubeGrids)
            {
                var beacon = ((MyCubeGrid)myCubeGrid).GetFirstBlockOfType<MyBeacon>();
                if (beacon != null)
                {
                    return;
                }
            }
            var beacon2 = grid.GetFirstBlockOfType<MyBeacon>();
            if (beacon2 != null)
            {
                return;
            }

            NotificationUtils.NotifyAllPlayersAround(grid.PositionComp.GetPosition(), 50, "На постройке " + grid.DisplayName + " не установлен маяк");
            NotificationUtils.NotifyAllPlayersAround(grid.PositionComp.GetPosition(), 50, "она будет удалена при следующей очистке");
        }
    }
}