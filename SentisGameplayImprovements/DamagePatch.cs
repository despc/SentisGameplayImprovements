using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Havok;
using NLog;
using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using SentisGameplayImprovements.Loot;
using SentisGameplayImprovements.PveZone;
using SentisOptimisations;
using Torch.Managers.PatchManager;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class DamagePatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        // written from the physics callbacks, taken whole once a second by Voxels.ProcessVoxelsContacts
        public static ConcurrentDictionary<long, GridVoxelContactInfo> contactInfo =
            new ConcurrentDictionary<long, GridVoxelContactInfo>();
        public static HashSet<long> ProtectedChars = new HashSet<long>();
        private static bool _init;

        public static void Init()
        {
            if (_init)
                return;
            _init = true;
            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, ProcessDamage);
        }

        private static void ProcessDamage(object target, ref MyDamageInformation info)
        {
            try
            {
                DoProcessDamage(target, ref info);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        private static void DoProcessDamage(object target, ref MyDamageInformation damage)
        {
            IMyCharacter character = target as IMyCharacter;
            if (character != null)
            {
                if (ProtectedChars.Contains(character.EntityId))
                {
                    damage.Amount = 0;
                    return;
                }
            }
            
            if (MySandboxGame.Static.SimulationFrameCounter / 60 <
                (ulong)SentisGameplayImprovementsPlugin.Config.DisableAnyDamageAfterStartTime)
            {
                damage.Amount = 0;
                damage.IsDeformation = false;
                return;
            }

            LootProcessor.CalculateLoot(target, damage);
        }

        

        public static void Patch(PatchContext ctx) => PatchGuard.Run("DamagePatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var MethodPerformDeformation = typeof(MyGridPhysics).GetMethod
                ("PerformDeformation", BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(MethodPerformDeformation).Prefixes.Add(
                typeof(DamagePatch).GetMethod(nameof(PatchPerformDeformation),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
        }

        private static bool PatchPerformDeformation(
            MyGridPhysics __instance,
            ref HkBreakOffPointInfo pt,
            bool fromBreakParts,
            float separatingVelocity,
            MyEntity otherEntity)
        {
            var cubeGrid = (__instance.Entity as MyCubeGrid);
            if (cubeGrid == null)
            {
                return true;
            }

            if (otherEntity is MyVoxelBase)
            {
                if (cubeGrid.PlayerPresenceTier != MyUpdateTiersPlayerPresence.Normal ||
                    !SentisGameplayImprovementsPlugin.Config.NoDamageFromVoxelsIfNobodyNear)
                {
                    return false;
                }

                if (separatingVelocity < SentisGameplayImprovementsPlugin.Config.NoDamageFromVoxelsBeforeSpeed)
                {
                    if (separatingVelocity < 5)
                    {
                        try
                        {
                            var info = contactInfo.GetOrAdd(cubeGrid.EntityId, _ => new GridVoxelContactInfo(cubeGrid, 0));
                            Interlocked.Increment(ref info.Count);
                        }
                        catch (Exception e)
                        {
                            //do nothing
                        }
                    }

                    return false;
                }
            }


            if (SentisGameplayImprovementsPlugin.Config.PvEZoneEnabled)
            {
                // no ramming damage to a grid in the PvE zone, one's own included
                if (PvECore.IsProtected(cubeGrid))
                {
                    if (SentisGameplayImprovementsPlugin.Config.EnableDamageFromNPC
                        && otherEntity is MyCubeGrid && ((MyCubeGrid)otherEntity).IsNpcGrid())
                    {
                        return true;
                    }

                    return false;
                }
            }

            if (cubeGrid.IsStatic)
            {
                if (!SentisGameplayImprovementsPlugin.Config.StaticRamming)
                {
                    return false;
                }
            }

            if (otherEntity is MyCubeGrid)
            {
                if (((MyCubeGrid)otherEntity).IsStatic)
                {
                    return true;
                }

                if (((MyCubeGrid)otherEntity).Mass <
                    SentisGameplayImprovementsPlugin.Config.MinimumMassForKineticDamage)
                {
                    return false;
                }
            }

            return true;
        }

        public class GridVoxelContactInfo
        {
            public MyCubeGrid MyCubeGrid;
            public long Count;

            public GridVoxelContactInfo(MyCubeGrid myCubeGrid, long count)
            {
                MyCubeGrid = myCubeGrid;
                this.Count = count;
            }
        }
    }
}