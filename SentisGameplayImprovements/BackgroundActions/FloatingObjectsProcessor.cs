using System;
using NLog;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisGameplayImprovements.AllGridsActions;
using SentisGameplayImprovements.Loot;
using VRage.Utils;

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
                        }, StartAt: MySession.Static.GameplayFrameCounter + MyUtils.GetRandomInt(10, 300));
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