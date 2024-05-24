using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NLog;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using SentisGameplayImprovements.DelayedLogic;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRageMath;

namespace SentisGameplayImprovements.Assholes;

public class Voxels
{
    public static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static ConcurrentDictionary<long, long> StuckGrids = new ConcurrentDictionary<long, long>();
    public static Random _random = new Random();

    public static void ProcessVoxelsContacts()
    {
        foreach (var gridVoxelContactInfo in new Dictionary<long, DamagePatch.GridVoxelContactInfo>(DamagePatch
                     .contactInfo))
        {
            var entityId = gridVoxelContactInfo.Key;
            var cubeGrid = gridVoxelContactInfo.Value.MyCubeGrid;
            var contactCount = gridVoxelContactInfo.Value.Count;

            if (contactCount > 50)
            {
                Log.Error("Entity  " + cubeGrid.DisplayName + " position " +
                          cubeGrid.PositionComp.GetPosition() + " contact count - " + contactCount);
            }

            if (contactCount < 500)
            {
                continue;
            }

            if (StuckGrids.ContainsKey(entityId))
            {
                if (StuckGrids[entityId] > 5)
                {
                    if (!Vector3.IsZero(
                            MyGravityProviderSystem.CalculateNaturalGravityInPoint(cubeGrid.WorldMatrix
                                .Translation)))
                    {
                        var identity = PlayerUtils.GetPlayerIdentity(PlayerUtils.GetOwner(cubeGrid));
                        var playerName = identity == null ? "----" : identity.DisplayName;
                        Log.Warn($"Convert stuck grid {cubeGrid.DisplayName} of player {playerName}");
                        NotificationUtils.NotifyAllPlayersAround(cubeGrid.PositionComp.GetPosition(), 200,
                            $"Грид {cubeGrid.DisplayName} игрока {playerName} конвертирован в статику из-за излишней любви к вокселям");
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            cubeGrid.Physics?.SetSpeeds(Vector3.Zero, Vector3.Zero);
                            cubeGrid.ConvertToStatic();
                            try
                            {
                                MyMultiplayer.RaiseEvent(cubeGrid,
                                    x => x.ConvertToStatic);
                                DelayedProcessor.Instance.AddDelayedAction(
                                    DateTime.Now.AddMilliseconds(MyRandom.Instance.Next(300, 1000)), () =>
                                    {
                                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                        {
                                            try
                                            {
                                                List<MyCubeGrid> groupNodes =
                                                    MyCubeGridGroups.Static.GetGroups(GridLinkTypeEnum.Logical)
                                                        .GetGroupNodes(cubeGrid);
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
                        });
                    }
                    else
                    {
                        try
                        {
                            var identity = PlayerUtils.GetPlayerIdentity(PlayerUtils.GetOwner(cubeGrid));
                            var playerName = identity == null ? "----" : identity.DisplayName;
                            Log.Warn($"Teleport stuck grid {cubeGrid.DisplayName} of player {playerName}");
                            NotificationUtils.NotifyAllPlayersAround(cubeGrid.PositionComp.GetPosition(), 200,
                                $"Грид {cubeGrid.DisplayName} игрока {playerName} телепортирован из-за излишней любви к вокселям");
                            MatrixD worldMatrix = cubeGrid.WorldMatrix;
                            var position = cubeGrid.PositionComp.GetPosition();

                            var garbageLocation = new Vector3D(position.X + _random.Next(-10000, 10000),
                                position.Y + _random.Next(-10000, 10000),
                                position.Z + _random.Next(-10000, 10000));
                            worldMatrix.Translation = garbageLocation;
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => { cubeGrid.Teleport(worldMatrix); });
                        }
                        catch (Exception e)
                        {
                            Log.Error("Exception in time try teleport entity to garbage", e);
                        }
                    }

                    StuckGrids.Remove(entityId);
                    continue;
                }

                StuckGrids[entityId] += 1;
                continue;
            }

            StuckGrids[entityId] = 1;
        }

        DamagePatch.contactInfo.Clear();
    }
}