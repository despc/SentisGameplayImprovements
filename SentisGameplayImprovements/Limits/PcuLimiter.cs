using System;
using System.Collections.Generic;
using NLog;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.Entities.Blocks.SafeZone;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace SentisGameplayImprovements
{
    public class PcuLimiter
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        
        public void CheckGrid(MyCubeGrid grid)
        {
            if (grid.IsStatic || IsLimitNotReached(grid)) return;
            MyAPIGateway.Utilities.InvokeOnGameThread(() => { LimitReached(grid); });
        }

        public static void LimitReached(MyCubeGrid cube)
        {
            List<IMySlimBlock> blocks = GridUtils.GetBlocks<IMyFunctionalBlock>((IMyCubeGrid)cube);
            foreach (var mySlimBlock in blocks)
            {
                if (noDisableBlock(mySlimBlock))
                {
                    continue;
                }

                ((IMyFunctionalBlock)mySlimBlock.FatBlock).Enabled = false;
            }

            var subGrids = GridUtils.GetSubGrids((IMyCubeGrid)cube,SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids);
            foreach (var myCubeGrid in subGrids)
            {
                List<IMySlimBlock> subGridBlocks = GridUtils.GetBlocks<IMyFunctionalBlock>(myCubeGrid);
                foreach (var mySlimBlock in subGridBlocks)
                {
                    if (noDisableBlock(mySlimBlock))
                    {
                        continue;
                    }

                    ((IMyFunctionalBlock)mySlimBlock.FatBlock).Enabled = false;
                }
            }

            cube.RaiseGridChanged();
        }

        private static bool noDisableBlock(IMySlimBlock mySlimBlock)
        {
            return mySlimBlock.FatBlock is MyReactor ||
                   mySlimBlock.FatBlock is MySurvivalKit ||
                   mySlimBlock.FatBlock is MyBatteryBlock ||
                   mySlimBlock.FatBlock is MyJumpDrive ||
                   mySlimBlock.FatBlock is MyProjectorBase ||
                   mySlimBlock.FatBlock is MyShipConnector ||
                   mySlimBlock.FatBlock is MyMedicalRoom ||
                   mySlimBlock.FatBlock is MySafeZoneBlock;
        }

        public static void SendLimitMessage(long identityId, int pcu, int maxPcu, String gridName)
        {
            if (identityId == 0)
            {
                return;
            }

            ChatUtils.SendTo(identityId, "Для структуры " + gridName + " достигнут лимит PCU!");
            ChatUtils.SendTo(identityId, "Использовано " + pcu + " PCU из возможных " + maxPcu);
            MyVisualScriptLogicProvider.ShowNotification("Достигнут лимит PCU!", 10000, "Red",
                identityId);
            MyVisualScriptLogicProvider.ShowNotification("Использовано " + pcu + " PCU из возможных " + maxPcu, 10000,
                "Red",
                identityId);
        }

        private static bool IsLimitNotReached(MyCubeGrid cube)
        {
            var gridPcu = GridUtils.GetPCU((IMyCubeGrid)cube, true, SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids);
            var maxPcu = cube.IsStatic
                ? SentisGameplayImprovementsPlugin.Config.MaxStaticGridPCU
                : SentisGameplayImprovementsPlugin.Config.MaxDinamycGridPCU;
            var subGrids = GridUtils.GetSubGrids((IMyCubeGrid)cube, SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids);
            foreach (var myCubeGrid in subGrids)
            {
                if (myCubeGrid.IsStatic)
                {
                    maxPcu = SentisGameplayImprovementsPlugin.Config.MaxStaticGridPCU;
                }
            }

            bool enemyAround = false;
            var owner = PlayerUtils.GetOwner((IMyCubeGrid)cube);
            foreach (var player in PlayerUtils.GetAllPlayers())
            {
                if (player.GetRelationTo(owner) != MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    continue;
                }

                var distance = Vector3D.Distance(player.GetPosition(), cube.PositionComp.GetPosition());
                if (distance > 15000)
                {
                    continue;
                }

                enemyAround = true;
            }

            var isLimitNotReached = gridPcu <= maxPcu;
            if (!isLimitNotReached)
            {
                SendLimitMessage(owner, gridPcu, maxPcu, cube.DisplayName);
            }

            maxPcu = enemyAround ? maxPcu : maxPcu + 5000;
            isLimitNotReached = gridPcu <= maxPcu;
            return isLimitNotReached;
        }
        
    }
}