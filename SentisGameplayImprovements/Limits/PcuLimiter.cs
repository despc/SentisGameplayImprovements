using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using NLog;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using SentisGameplayImprovements.DelayedLogic;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.Entities.Blocks.SafeZone;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRageMath;

namespace SentisGameplayImprovements
{
    public class PcuLimiter
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public static ConcurrentDictionary<MyCubeGrid, int> overlimitGrids =
            new ConcurrentDictionary<MyCubeGrid, int>();
        
        public void CheckGrid(MyCubeGrid grid)
        {
            if (grid.IsStatic || IsLimitNotReached(grid)) return;
            LimitReached(grid);
        }

        public static void LimitReached(MyCubeGrid cube)
        {
            var subGrids = GridUtils.GetSubGrids(cube,SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids);
            MyCubeGrid largestGrid = cube;
            foreach (var myCubeGrid in subGrids)
            {
                if (largestGrid.BlocksPCU < ((MyCubeGrid)myCubeGrid).BlocksPCU)
                {
                    largestGrid = (MyCubeGrid)myCubeGrid;
                }
                List<IMySlimBlock> subGridBlocks = GridUtils.GetBlocks<IMyFunctionalBlock>(myCubeGrid);
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    try
                    {
                        foreach (var mySlimBlock in subGridBlocks)
                        {
                            if (noDisableBlock(mySlimBlock))
                            {
                                continue;
                            }

                            ((IMyFunctionalBlock)mySlimBlock.FatBlock).Enabled = false;
                        }
                    }
                    catch
                    {
                    }
                });
                
            }
            
            cube.RaiseGridChanged();
            
            var count = 0;
            if (overlimitGrids.TryGetValue(cube, out count))
            {
                count = count + 1;
            }

            if (count > 5)
            {
                
                overlimitGrids.Remove(largestGrid);
                var ownerId = PlayerUtils.GetOwner(largestGrid);
                var playerIdentity = PlayerUtils.GetPlayerIdentity(ownerId);
                var playerName = playerIdentity == null ? "---" : playerIdentity.DisplayName;
                Log.Error(
                    $"Перелимит на гриде {largestGrid.DisplayName} игрока {playerName} конвертим в статику");
                foreach (var player in PlayerUtils.GetAllPlayersInRadius(largestGrid.PositionComp.GetPosition(), 10000))
                {
                    ChatUtils.SendTo(player.IdentityId,
                        $"Структура {largestGrid.DisplayName} игрока {playerName} конвертирована в статичную по причине перелимита PCU");
                    MyVisualScriptLogicProvider.ShowNotification(
                        $"Структура {largestGrid.DisplayName} игрока {playerName} конвертирована в статичную по причине перелимита PCU",
                        5000,
                        "Red", player.IdentityId);
                }

                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    try
                    {
                        ConvertToStatic(largestGrid);
                    }
                    catch
                    {
                    }
                });
                Thread.Sleep(2000);
            }
            else
            {
                overlimitGrids[cube] = count;
            }
        }

        public static bool ConvertToStatic(MyCubeGrid grid)
        {
            try
            {
                grid.Physics?.SetSpeeds(Vector3.Zero, Vector3.Zero);
                grid.ConvertToStatic();
                try
                {
                    MyMultiplayer.RaiseEvent(grid, x => x.ConvertToStatic);
                    DelayedProcessor.Instance.AddDelayedAction(
                        DateTime.Now.AddMilliseconds(MyRandom.Instance.Next(300, 1000)), () =>
                        {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                            {
                                try
                                {
                                    List<MyCubeGrid> groupNodes =
                                        MyCubeGridGroups.Static.GetGroups(GridLinkTypeEnum.Logical).GetGroupNodes(grid);
                                    FixShipLogic.FixGroups(groupNodes);
                                }
                                catch
                                {
                                }
                            });
                        });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "()Exception in RaiseEvent.");
                }

                return true;
            }
            catch (Exception e)
            {
                return false;
            }
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