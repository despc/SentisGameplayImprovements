using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NLog;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace SentisGameplayImprovements.AllGridsActions
{
    public class EntitiesObserver
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static HashSet<MySafeZone> Safezones = new HashSet<MySafeZone>();
        public static HashSet<MyCubeGrid> MyCubeGrids = new HashSet<MyCubeGrid>();
        public static ConcurrentDictionary<MyFloatingObject, DateTime> MyFloatingObjects = new ConcurrentDictionary<MyFloatingObject, DateTime>();
        public static HashSet<IMyVoxelMap> VoxelMaps = new HashSet<IMyVoxelMap>();
        public static HashSet<MyPlanet> Planets = new HashSet<MyPlanet>();

        public static void MyEntitiesOnOnEntityRemove(MyEntity entity)
        {
            if (entity is MyFloatingObject)
            {
                MyFloatingObjects.Remove((MyFloatingObject)entity);
                return;
            }
           
            if (entity is MyCubeGrid)
            {
                MyCubeGrids.Remove((MyCubeGrid) entity);
                return;
            }

            if (entity is MyPlanet)
            {
                Planets.Remove((MyPlanet) entity);
                return;
            }

            if (entity is IMyVoxelMap)
            {
                VoxelMaps.Remove((IMyVoxelMap) entity);
                return;
            }

            if (entity is MySafeZone)
            {
                Safezones.Remove((MySafeZone) entity);
            }
        }

        public static void MyEntitiesOnOnEntityAdd(MyEntity entity)
        {
            if (entity is MyFloatingObject)
            {
                MyFloatingObjects.TryAdd((MyFloatingObject)entity, DateTime.Now);
                return;
            }

            if (entity is MyPlanet)
            {
                Planets.Add((MyPlanet) entity);
                return;
            }

            if (entity is MyCubeGrid)
            {
                MyCubeGrids.Add((MyCubeGrid) entity);
                return;
            }

            if (entity is IMyVoxelMap)
            {
                VoxelMaps.Add((IMyVoxelMap) entity);
                return;
            }

            if (entity is MySafeZone)
            {
                Safezones.Add((MySafeZone) entity);
            }
        }
    }
}