﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders.Private;
using VRage.Utils;
using VRageMath;

namespace SentisGameplayImprovements.Loot;

public class LootProcessor
{
    public static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public static ConcurrentDictionary<MyCubeGrid, Dictionary<MyDefinitionId, int>> ComponentsSpawnBuffer =
        new ConcurrentDictionary<MyCubeGrid, Dictionary<MyDefinitionId, int>>();
    public static void CalculateLoot(object target, MyDamageInformation info)
        {
            var slimBlock = target as MySlimBlock;
            if (slimBlock == null)
            {
                return;
            }

            if (!SentisGameplayImprovementsPlugin.Config.LootSystemEnabled)
            {
                return;
            }
            
            if (info.Type == MyDamageType.Grind || info.Type == MyDamageType.Deformation)
            {
                return;
            }

            var amount = info.Amount;
            amount *= slimBlock.BlockGeneralDamageModifier;
            Dictionary<MyDefinitionId, int> componentsToSpawn = new Dictionary<MyDefinitionId, int>();
            for (var i = slimBlock.BlockDefinition.Components.Length - 1; i >= 0; i--)
            {
                var myComponentStackInfo = slimBlock.ComponentStack.GetComponentStackInfo(i);
                if (myComponentStackInfo.MountedCount == 0)
                {
                    continue;
                }

                var oneCompIntegrity = myComponentStackInfo.MaxIntegrity / myComponentStackInfo.TotalCount;
                if ((int) ((myComponentStackInfo.Integrity - amount) / oneCompIntegrity) + 1 >= myComponentStackInfo.MountedCount)
                {
                    break;
                }

                for (int j = 0; j < myComponentStackInfo.MountedCount; j++)
                {
                    if ((int) ((myComponentStackInfo.Integrity - amount) / oneCompIntegrity) + 1 >= myComponentStackInfo.MountedCount)
                    {
                        break;
                    }

                    amount -= oneCompIntegrity;
                    var compDef = myComponentStackInfo.DefinitionId;
                    if (componentsToSpawn.ContainsKey(compDef))
                    {
                        componentsToSpawn[compDef] = componentsToSpawn[compDef] + 1;
                    }
                    else
                    {
                        componentsToSpawn[compDef] = 1;
                    }
                }
            }

            if (componentsToSpawn.Count > 0)
            {
                Dictionary<MyDefinitionId, int> componentsToSpawnFromBuffer;
                if (ComponentsSpawnBuffer.TryGetValue(slimBlock.CubeGrid, out componentsToSpawnFromBuffer))
                {
                    foreach (var newComponents in componentsToSpawn)
                    {
                        int count;
                        if (componentsToSpawnFromBuffer.TryGetValue(newComponents.Key, out count))
                        {
                            componentsToSpawnFromBuffer[newComponents.Key] = count + newComponents.Value;
                        }
                        else
                        {
                            componentsToSpawnFromBuffer[newComponents.Key] = newComponents.Value;
                        }
                    }
                }
                else
                {
                    ComponentsSpawnBuffer[slimBlock.CubeGrid] = componentsToSpawn;
                }
            }
        }

        public static void CheckPlaceAndSpawnItems(Dictionary<MyDefinitionId, int> componentsToSpawn, Vector3D gridPos)
        {
            try
            {
                List<MyPhysicalInventoryItem> itemsToSpawn = new List<MyPhysicalInventoryItem>();
                foreach (var componentToSpawn in componentsToSpawn)
                {
                    var myDefinitionId = componentToSpawn.Key;
                    MyObjectBuilder_PhysicalObject newObject =
                        MyObjectBuilderSerializerKeen.CreateNewObject(myDefinitionId.TypeId,
                                myDefinitionId.SubtypeName) as MyObjectBuilder_PhysicalObject;

                   
                    var compDef = MyDefinitionManager.Static.GetComponentDefinition(myDefinitionId);
                    var amountWithChance = (int)(componentToSpawn.Value * compDef.DropProbability);
                    if (amountWithChance < 1)
                    {
                        continue;
                    }
                    var newItem = new MyPhysicalInventoryItem(amountWithChance, newObject);
                   
                    itemsToSpawn.Add(newItem);
                }
                
                
                MyPhysicsComponentBase motionInheritedFrom = null;
                
                for (var index = 0; index < itemsToSpawn.Count; index++)
                {
                    var item = itemsToSpawn[index];
                    Thread.Sleep(MyUtils.GetRandomInt(0, 32));
                    var boundingSphere = new BoundingSphere(gridPos, 75);
                    int i = 0;
                    Vector3D? pos = null;
                    while (i < 20 && !pos.HasValue)
                    {
                        i++;
                        var randomBorderPosition = MyUtils.GetRandomBorderPosition(ref boundingSphere);
                        var spawnPosSphere = new BoundingSphereD(randomBorderPosition, 0.3f);
                        List<MyEntity> result = new List<MyEntity>();
                        MyGamePruningStructure.GetAllEntitiesInSphere(ref spawnPosSphere, result);
                        var voxelMapsNotContainsPoint = result.Where(entity => entity is MyVoxelBase &&
                                                                               !((MyVoxelBase)entity).IsAnyOfPointInside([randomBorderPosition])).ToList();
                        foreach (var myEntity in voxelMapsNotContainsPoint)
                        {
                            result.Remove(myEntity);
                        }
                        if (result.Count == 0)
                        {
                            pos = randomBorderPosition;
                        }
                    }

                    if (!pos.HasValue)
                    {
                        continue;
                    }
                    
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        try
                        {
                            MyFloatingObjects.Spawn(item, pos.Value, Vector3D.Forward, Vector3D.Up);
                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "Spawn drop sync exception");
                        }
                    }, StartAt: MySession.Static.GameplayFrameCounter + 10 * index);
                        
                }
                
            }
            catch (Exception e)
            {
                Log.Error(e, "Spawn drop exception");
            }
        }
}