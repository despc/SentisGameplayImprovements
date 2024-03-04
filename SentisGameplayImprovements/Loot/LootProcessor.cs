using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisGameplayImprovements.DelayedLogic;
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
            
            if (info.Type == MyDamageType.Grind)
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
                DelayedProcessor.Instance.AddDelayedAction(DateTime.Now.AddSeconds(1),
                    () => { CheckPlaceAndSpawnItems(componentsToSpawn, slimBlock, slimBlock.WorldPosition); });
            }
        }

        private static void CheckPlaceAndSpawnItems(Dictionary<MyDefinitionId, int> componentsToSpawn, MySlimBlock slimBlock, Vector3D blockPos)
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
                var cubeGrid = slimBlock.CubeGrid;
                if (cubeGrid != null && !cubeGrid.Closed && !cubeGrid.MarkedForClose)
                {
                    motionInheritedFrom = cubeGrid.Physics;
                }
                
                for (var index = 0; index < itemsToSpawn.Count; index++)
                {
                    var item = itemsToSpawn[index];
                    Thread.Sleep(MyUtils.GetRandomInt(0, 32));
                    var boundingSphere = new BoundingSphere(blockPos, 25);
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
                            MyFloatingObjects.Spawn(item, pos.Value, Vector3D.Forward, Vector3D.Up, motionInheritedFrom,
                                entity =>
                                {
                                    // entity.Physics.RigidBody.SetCollisionFilterInfo(HkGroupFilter.CalcFilterInfo(31, 0, 0, 0));
                                    // MyPhysics.RefreshCollisionFilter((MyPhysicsBody)entity.Physics);
                                    //
                                    // ((MyPhysicsBody)entity.Physics).HavokWorld.RemoveRigidBody(entity.Physics.RigidBody);
                                    // entity.Physics.RigidBody.Dispose();
                                    // entity.RaisePhysicsChanged();
                                });
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