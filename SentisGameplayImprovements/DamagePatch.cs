﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Havok;
using NLog;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisGameplayImprovements.DelayedLogic;
using SentisGameplayImprovements.Loot;
using SentisGameplayImprovements.PveZone;
using Torch.Managers.PatchManager;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders.Private;
using VRage.Utils;
using VRageMath;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class DamagePatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public static Dictionary<long, GridVoxelContactInfo> contactInfo = new Dictionary<long, GridVoxelContactInfo>();
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

        

        public static void Patch(PatchContext ctx)
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
                            if (contactInfo.ContainsKey(cubeGrid.EntityId))
                            {
                                contactInfo[cubeGrid.EntityId].Count++;
                            }
                            else
                            {
                                contactInfo[cubeGrid.EntityId] = new GridVoxelContactInfo(cubeGrid, 1);
                            }
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
                if (PvECore.EntitiesInZone.Contains(cubeGrid.EntityId))
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