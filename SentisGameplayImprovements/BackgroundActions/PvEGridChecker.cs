using System;
using NLog;
using Sandbox.Game.Entities;
using SentisGameplayImprovements.PveZone;
using VRageMath;

namespace SentisGameplayImprovements.BackgroundActions
{
    public class PvEGridChecker
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public void CheckGridIsPvE(MyCubeGrid grid)
        {
            if (!SentisGameplayImprovementsPlugin.Config.PvEZoneEnabled)
            {
                return;
            }
            try
            {
                if (grid.DisplayName.Contains("Container MK-") || grid.DisplayName.Contains("Container_MK-"))
                    return;
                
                if (grid.MarkedForClose || grid.Closed)
                {
                    if (PvECore.EntitiesInZone.Contains(grid.EntityId))
                        PvECore.EntitiesInZone.Remove(grid.EntityId);
                }

                var insidePvEZone = PvECore.PveSphere.Contains(grid.PositionComp.GetPosition()) == ContainmentType.Contains;

                if (!insidePvEZone)
                {
                    if (PvECore.EntitiesInZone.Contains(grid.EntityId))
                    {
                        PvECore.EntitiesInZone.Remove(grid.EntityId);
                    }
                }

                if (insidePvEZone)
                {
                    PvECore.EntitiesInZone.Add(grid.EntityId);
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }
    }
}