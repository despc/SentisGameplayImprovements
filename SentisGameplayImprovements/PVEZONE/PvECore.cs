using System;
using System.Collections.Generic;
using System.Globalization;
using NLog;
using VRageMath;

namespace SentisGameplayImprovements.PveZone
{
    internal static class PvECore
    {
        public static readonly Logger Log = LogManager.GetLogger("PvE ZONE");

        public static readonly HashSet<long> EntitiesInZone = new HashSet<long>();

        public static BoundingSphereD PveSphere;

        private static bool _init = false;

        public static void Init()
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            try
            {
                _init = true; 
                var configPveZonePos = config.PveZonePos.Split(':');
                var pos = new Vector3D(Convert.ToDouble(configPveZonePos[0], CultureInfo.InvariantCulture),
                    Convert.ToDouble(configPveZonePos[1], CultureInfo.InvariantCulture),
                    Convert.ToDouble(configPveZonePos[2], CultureInfo.InvariantCulture));
                Log.Info("PvE Zone pos " + config.PveZonePos);
                InitSphere(pos, config.PveZoneRadius);
                DamageHandler.Init();
                Log.Info("Initing Sentis PVE ZONE... Complete!");
            }
            catch (Exception e)
            {
                Log.Error(e, "Initing Sentis PVE ZONE... CRASH!");
            }

        }

        public static void ReloadSettings()
        {
            if (!_init)
            {
                return;
            }
            var configPveZonePos = SentisGameplayImprovementsPlugin.Config.PveZonePos.Split(':');
            var pos = new Vector3D(Convert.ToDouble(configPveZonePos[0], CultureInfo.InvariantCulture),
                Convert.ToDouble(configPveZonePos[1], CultureInfo.InvariantCulture),
                Convert.ToDouble(configPveZonePos[2], CultureInfo.InvariantCulture));
            InitSphere(pos, SentisGameplayImprovementsPlugin.Config.PveZoneRadius);
        }

        private static void InitSphere(Vector3D pos, int radius)
        {
            PveSphere = new BoundingSphereD(pos, radius);
        }
    }
}
