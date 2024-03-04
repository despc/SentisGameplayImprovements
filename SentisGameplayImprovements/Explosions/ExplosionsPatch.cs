using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Torch.Managers.PatchManager;
using VRage.Game;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class ExplosionsPatch
    {
        public static Harmony harmony = new Harmony("ExplosionsPatch");
        
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
            
            harmony.Patch(UnRegisterFloatingObject, finalizer: new HarmonyMethod(finalizer));
            harmony.Patch(RegisterFloatingObject, finalizer: new HarmonyMethod(finalizer));
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
        
        private static bool UpdateAfterSimulationParallelPatched(MyFloatingObject __instance)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks)
            {
                return true;
            }
           
            try
            {
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