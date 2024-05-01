using System;
using System.Collections.Generic;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisGameplayImprovements.AllGridsActions;
using SentisGameplayImprovements.Loot;
using VRage.Library.Utils;

namespace SentisGameplayImprovements.BackgroundActions
{
    public class FloatingObjectsProcessor
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void SpawnAccumulatedLoot()
        {
            if (!SentisGameplayImprovementsPlugin.Config.LootSystemEnabled)
            {
                return;
            }

            foreach (var entry in LootProcessor.ComponentsSpawnBuffer)
            {
                var myCubeGrid = entry.Key;
                var componentsToSpawn = entry.Value;
                LootProcessor.CheckPlaceAndSpawnItems(componentsToSpawn, myCubeGrid.PositionComp.GetPosition());
            }
            
            LootProcessor.ComponentsSpawnBuffer.Clear();
        }

        public static void CheckFloatingObjects()
        {
            if (!SentisGameplayImprovementsPlugin.Config.CustomFloatingObjectsCleanup)
            {
                return;
            }

            try
            {
                var currentFloatingObjectsCount = EntitiesObserver.MyFloatingObjects.Count;
                var maxFloatingObjects = SentisGameplayImprovementsPlugin.Config.MaxFloatingObjects;
                if (currentFloatingObjectsCount > maxFloatingObjects)
                {
                    var delta = (currentFloatingObjectsCount - maxFloatingObjects) * 2;
                    var listObj = new List<MyFloatingObject>(EntitiesObserver.MyFloatingObjects.Keys);
                    while (delta > 0)
                    {
                        delta--;
                        if (listObj.Count == 0)
                        {
                            break;
                        }
                        var myFloatingObject = listObj[MyRandom.Instance.Next(0, listObj.Count)];
                        if (myFloatingObject == null)
                        {
                            break;
                        }
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                if (myFloatingObject.Closed || myFloatingObject.MarkedForClose)
                                {
                                    return;
                                }
                                myFloatingObject.Close();
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }, StartAt: MySession.Static.GameplayFrameCounter + MyRandom.Instance.Next(1, 30));
                    }
                }
                foreach (var entry in EntitiesObserver.MyFloatingObjects)
                {
                    var spawnTime = entry.Value;
                    if (spawnTime.AddMinutes(SentisGameplayImprovementsPlugin.Config.FloatingObjectsLifetime) <
                        DateTime.Now)
                    {
                        var myFloatingObject = entry.Key;
                        if (myFloatingObject == null || myFloatingObject.Closed || myFloatingObject.MarkedForClose)
                        {
                            continue;
                        }

                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                myFloatingObject.Close();
                                myFloatingObject.WasRemovedFromWorld = true;
                            }
                            catch (Exception e)
                            {
                                Log.Error(e);
                            }
                        }, StartAt: MySession.Static.GameplayFrameCounter + MyRandom.Instance.Next(10, 300));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
}