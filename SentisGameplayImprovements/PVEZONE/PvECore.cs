using System.Globalization;
using NLog;
using Sandbox.Game.Entities;
using VRageMath;

namespace SentisGameplayImprovements.PveZone
{
    /// <summary>
    /// The PvE zone: a sphere (<c>PveZonePos</c>, <c>PveZoneRadius</c>) in which grids take no damage from
    /// other players.
    ///
    /// A grid is in the zone when its position is inside the sphere, and that is worked out at the moment of
    /// the damage: it costs a subtraction and a comparison, so there is no list of the grids inside to keep
    /// up to date (the list it replaced was rebuilt every 30 seconds on a worker thread while the game and
    /// the physics read it - a grid that flew in waited up to half a minute for its protection).
    /// </summary>
    public static class PvECore
    {
        public static readonly Logger Log = LogManager.GetLogger("PvE ZONE");

        /// <summary>Swapped whole when the settings change, so a reader never sees half a sphere.</summary>
        private sealed class Zone
        {
            public readonly BoundingSphereD Sphere;
            public Zone(BoundingSphereD sphere) => Sphere = sphere;
        }

        private static Zone _zone;

        // The config's setters call ReloadSettings while Torch is still reading the file, before the
        // plugin's config exists; reading it then throws, and an exception there makes Torch drop the
        // whole file for the defaults. So nothing is read before Init.
        private static bool _loaded;

        public static BoundingSphereD PveSphere => _zone?.Sphere ?? default;

        public static void Init()
        {
            _loaded = true;
            ReloadSettings();
            DamageHandler.Init();
            Log.Info("PvE zone " + SentisGameplayImprovementsPlugin.Config.PveZonePos + " radius " +
                     SentisGameplayImprovementsPlugin.Config.PveZoneRadius);
        }

        /// <summary>Reads the zone from the settings; a position that does not parse leaves the zone as it was.</summary>
        public static void ReloadSettings()
        {
            if (!_loaded) return;
            try
            {
                var config = SentisGameplayImprovementsPlugin.Config;
                if (!TryParsePosition(config.PveZonePos, out var centre))
                {
                    Log.Error("PvE zone position '" + config.PveZonePos + "' is not x:y:z");
                    return;
                }
                _zone = new Zone(new BoundingSphereD(centre, config.PveZoneRadius));
            }
            catch (System.Exception e)
            {
                Log.Error(e, "PvE zone settings");
            }
        }

        public static bool TryParsePosition(string text, out Vector3D position)
        {
            position = default;
            var parts = text?.Split(':');
            if (parts == null || parts.Length != 3) return false;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                return false;
            position = new Vector3D(x, y, z);
            return true;
        }

        /// <summary>Whether the zone is on and the point is inside it.</summary>
        public static bool IsInZone(Vector3D point)
        {
            var zone = _zone;
            return zone != null && SentisGameplayImprovementsPlugin.Config.PvEZoneEnabled &&
                   zone.Sphere.Contains(point) == ContainmentType.Contains;
        }

        /// <summary>Whether the grid is protected by the zone: it is in it and it is not a cargo drop container.</summary>
        public static bool IsProtected(MyCubeGrid grid) =>
            grid != null && IsInZone(grid.PositionComp.GetPosition()) && !IsExempt(grid.DisplayName);

        /// <summary>The cargo drop containers ("Container MK-...") are loot for anyone, in the zone too.</summary>
        public static bool IsExempt(string gridName) =>
            gridName != null && (gridName.Contains("Container MK-") || gridName.Contains("Container_MK-"));
    }
}
