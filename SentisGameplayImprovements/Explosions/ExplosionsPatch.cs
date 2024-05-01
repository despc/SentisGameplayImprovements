using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SentisGameplayImprovements.DelayedLogic;
using Torch.Managers.PatchManager;
using VRage.Game;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class ExplosionsPatch
    {
        public static Harmony harmony = new Harmony("ExplosionsPatch");
        private static Type FloatingObjectType = typeof(MyFloatingObjects);
        private static BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                               | BindingFlags.Static;
        private static MethodInfo AddToSynchronizationMethod  = FloatingObjectType.GetMethod("AddToSynchronization", bindFlags);
        
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx)
        {
            var UpdateAfterSimulationParallel = typeof(MyFloatingObject).GetMethod
                (nameof(MyFloatingObject.UpdateAfterSimulationParallel), BindingFlags.Instance | BindingFlags.Public);

            ctx.GetPattern(UpdateAfterSimulationParallel).Prefixes.Add(
                typeof(ExplosionsPatch).GetMethod(nameof(UpdateAfterSimulationParallelPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));

            var CheckObjectInVoxel = typeof(MyFloatingObjects).GetMethod
                ("CheckObjectInVoxel", BindingFlags.Instance | BindingFlags.NonPublic);

            var RegisterFloatingObject = typeof(MyFloatingObjects).GetMethod
                ("RegisterFloatingObject", BindingFlags.Static | BindingFlags.NonPublic);

            var UnRegisterFloatingObject = typeof(MyFloatingObjects).GetMethod
                ("UnregisterFloatingObject", BindingFlags.Static | BindingFlags.NonPublic);

            var finalizer = typeof(ExplosionsPatch).GetMethod(nameof(SuppressExceptionFinalizer),
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            
            ctx.GetPattern(RegisterFloatingObject).Prefixes.Add(
                typeof(ExplosionsPatch).GetMethod(nameof(RegisterFloatingObjectPatch),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));

            harmony.Patch(UnRegisterFloatingObject, finalizer: new HarmonyMethod(finalizer));
            harmony.Patch(CheckObjectInVoxel, finalizer: new HarmonyMethod(finalizer));
        }

        public static Exception SuppressExceptionFinalizer(Exception __exception)
        {
            // if (__exception != null)
            // {
            // SentisOptimisationsPlugin.Log.Error("SuppressException ", __exception);
            // }
            return null;
        }

        private static bool RegisterFloatingObjectPatch(MyFloatingObject obj)
        {
            
            if (SentisGameplayImprovementsPlugin.Config.CustomFloatingObjectsCleanup)
            {
                obj.CreationTime = Stopwatch.GetTimestamp();
                AddToSynchronizationMethod.Invoke(null, new object[] { obj });
                return false;
            }

            if (obj == null || obj.WasRemovedFromWorld)
                return false;
            obj.CreationTime = Stopwatch.GetTimestamp();
            DelayedProcessor.Instance.AddDelayedAction(DateTime.Now.AddMilliseconds(3), () =>
            {
                SortedSet<MyFloatingObject> m_floatingOres =
                    (SortedSet<MyFloatingObject>)ReflectionUtils.GetPrivateStaticField(FloatingObjectType,
                        "m_floatingOres");
                SortedSet<MyFloatingObject> m_floatingItems =
                    (SortedSet<MyFloatingObject>)ReflectionUtils.GetPrivateStaticField(FloatingObjectType,
                        "m_floatingItems");
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    try
                    {
                        if (obj.VoxelMaterial != null)
                        {
                            m_floatingOres.Add(obj);
                        }
                        else
                        {
                            m_floatingItems.Add(obj);
                        }

                        AddToSynchronizationMethod.Invoke(null, new object[] { obj });
                    }
                    catch (Exception e)
                    {
                        Log.Error("RegisterFloatingObject exception " + e);
                    }
                });
            });
            return false;
        }
        private static bool UpdateAfterSimulationParallelPatched(MyFloatingObject __instance)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks)
            {
                return true;
            }

            try
            {
                var content = __instance.Item.Content;
                var canExploded = content is MyObjectBuilder_AmmoMagazine || "Explosives".Equals(content.SubtypeName);
                if (!canExploded)
                {
                    return true;
                }

                var acceleration = __instance.Physics.LinearAcceleration;
                if (acceleration.X > SentisGameplayImprovementsPlugin.Config.AccelerationToDamage
                    || acceleration.Y > SentisGameplayImprovementsPlugin.Config.AccelerationToDamage
                    || acceleration.Z > SentisGameplayImprovementsPlugin.Config.AccelerationToDamage)
                {
                    __instance.DoDamage(999, MyDamageType.Explosion, true, 0);
                    return false;
                }
            }
            catch (Exception e)
            {
                //
            }

            return true;
        }
    }
}