using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NLog;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.World;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// An economy station's safe zone made again when it is gone while the station stands.
    ///
    /// The game makes a station's safe zone only together with the station (<c>MyStationCellGenerator.GenerateObjects</c>)
    /// and takes both away when the players leave its area (<c>CloseObjectSeed</c>, the zone first). A zone that went alone
    /// (an admin, a plugin, a mod, not loaded) is never made again: the station stands open to grinding and fire for ever,
    /// and as <c>CloseObjectSeed</c> then finds no zone it does not take the station away either (SCBT Watchpost Arcadia,
    /// 16.11.2026). Here, the frame after a station's zone goes, and once a minute for those already without one: the zone
    /// made again the game's way (<c>MyFactionStation.CreateSafeZone</c>, the accesses as the generator sets them). A zone
    /// that goes with its station (the area unloaded) is not made again: the station is closing too. SentisWatcher logs who
    /// closed it.
    /// </summary>
    public static class StationSafeZones
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private const int SweepMs = 60000;

        private static readonly MethodInfo Create =
            typeof(MyFactionStation).GetMethod("CreateSafeZone", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static Timer _timer;
        private static bool _subscribed;

        public static void OnLoaded()
        {
            if (Create == null)
            {
                Log.Error("SentisGameplayImprovements: MyFactionStation.CreateSafeZone not found, the stations' safe zones are not made again");
                return;
            }
            if (!_subscribed)
            {
                MySessionComponentSafeZones.OnRemoveSafeZone += OnRemoved;
                _subscribed = true;
            }
            _timer?.Dispose();
            _timer = new Timer(_ => MySandboxGame.Static?.Invoke(Sweep, "SGI station safe zones"), null, 10000, SweepMs);
        }

        public static void OnUnloading()
        {
            _timer?.Dispose();
            _timer = null;
            if (_subscribed) MySessionComponentSafeZones.OnRemoveSafeZone -= OnRemoved;
            _subscribed = false;
        }

        private static void OnRemoved(object sender, EventArgs args)
        {
            if (!(sender is MySafeZone zone) || zone.SafeZoneBlockId != 0 || MySession.Static == null || !MySession.Static.Ready) return;
            var id = zone.EntityId;
            // the next frame: the station's own closing (its area unloaded) comes right after its zone's
            MySandboxGame.Static?.Invoke(() => Restore(Stations().Where(s => s.Station.SafeZoneEntityId == id)), "SGI station safe zone");
        }

        /// <summary>Every station standing without its safe zone.</summary>
        public static void Sweep()
        {
            try
            {
                Restore(Stations());
            }
            catch (Exception e)
            {
                Log.Error(e, "SentisGameplayImprovements: the stations' safe zones check failed");
            }
        }

        private static IEnumerable<(MyFaction Faction, MyFactionStation Station)> Stations()
        {
            var factions = MySession.Static?.Factions;
            if (factions == null) yield break;
            foreach (var faction in factions.Factions.Values.ToList())
            foreach (var station in faction.Stations.OfType<MyFactionStation>().ToList())
                yield return ((MyFaction)faction, station);
        }

        private static void Restore(IEnumerable<(MyFaction Faction, MyFactionStation Station)> stations)
        {
            if (MySession.Static == null || !MySession.Static.Ready) return;
            foreach (var (faction, station) in stations.ToList())
            {
                try
                {
                    if (station.StationEntityId == 0 || !MyEntities.TryGetEntityById(station.StationEntityId, out MyCubeGrid grid) || grid.MarkedForClose) continue;
                    if (station.SafeZoneEntityId != 0 && MyEntities.TryGetEntityById(station.SafeZoneEntityId, out MySafeZone old) && !old.MarkedForClose) continue;
                    var gone = station.SafeZoneEntityId;
                    var zone = (MySafeZone)Create.Invoke(station, new object[] { faction });
                    zone.AccessTypeGrids = MySafeZoneAccess.Blacklist;
                    zone.AccessTypeFloatingObjects = MySafeZoneAccess.Blacklist;
                    zone.AccessTypePlayers = MySafeZoneAccess.Blacklist;
                    zone.AccessTypeFactions = MySafeZoneAccess.Blacklist;
                    var at = grid.PositionComp.GetPosition();
                    Log.Warn($"SentisGameplayImprovements: the station \"{grid.DisplayName}\" {grid.EntityId} ({faction.Tag}) stood without its safe zone " +
                             (gone != 0 ? gone.ToString() : "(none noted)") + $"; made again: {zone.EntityId}, radius {zone.Radius:0} m, at {at.X:0} {at.Y:0} {at.Z:0}");
                }
                catch (Exception e)
                {
                    Log.Error(e, "SentisGameplayImprovements: the safe zone of station " + station.StationEntityId + " was not made again");
                }
            }
        }
    }
}
