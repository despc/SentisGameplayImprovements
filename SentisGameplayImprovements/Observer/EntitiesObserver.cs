using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NLog;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using SentisGameplayImprovements.Utils;

namespace SentisGameplayImprovements.AllGridsActions
{
    public class EntitiesObserver
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static ConcurrentHashSet<MySafeZone> Safezones = new ConcurrentHashSet<MySafeZone>();
        public static ConcurrentHashSet<MyCubeGrid> MyCubeGrids = new ConcurrentHashSet<MyCubeGrid>();
        public static ConcurrentDictionary<MyFloatingObject, DateTime> MyFloatingObjects = new ConcurrentDictionary<MyFloatingObject, DateTime>();
        public static ConcurrentHashSet<IMyVoxelMap> VoxelMaps = new ConcurrentHashSet<IMyVoxelMap>();
        public static ConcurrentHashSet<MyPlanet> Planets = new ConcurrentHashSet<MyPlanet>();

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