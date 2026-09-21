using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using Sandbox.ModAPI;
using SentisGameplayImprovements.Explosions;
using Torch.Managers.PatchManager;
using VRage.Game;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// Floating objects: their registration when the plugin cleans them up itself, and crates of
    /// ammunition or explosives that go off when they are knocked hard enough.
    /// </summary>
    [PatchShim]
    public static class ExplosionsPatch
    {
        public static Harmony harmony = new Harmony("ExplosionsPatch");
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static Action<MyFloatingObject> _addToSynchronization;

        // crates already told to go off, so the parallel update does not queue one twice
        private static readonly ConcurrentDictionary<long, byte> Detonating = new ConcurrentDictionary<long, byte>();

        public static void Patch(PatchContext ctx) => global::SentisOptimisations.PatchGuard.Run("ExplosionsPatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var addToSynchronization = typeof(MyFloatingObjects).GetMethod("AddToSynchronization", any)
                                       ?? throw new MissingMethodException("MyFloatingObjects.AddToSynchronization");
            _addToSynchronization = (Action<MyFloatingObject>)Delegate.CreateDelegate(typeof(Action<MyFloatingObject>), addToSynchronization);

            var updateParallel = typeof(MyFloatingObject).GetMethod(nameof(MyFloatingObject.UpdateAfterSimulationParallel),
                BindingFlags.Instance | BindingFlags.Public);
            ctx.GetPattern(updateParallel).Prefixes.Add(Method(nameof(UpdateAfterSimulationParallelPatched)));

            var register = typeof(MyFloatingObjects).GetMethod("RegisterFloatingObject", BindingFlags.Static | BindingFlags.NonPublic);
            ctx.GetPattern(register).Prefixes.Add(Method(nameof(RegisterFloatingObjectPatch)));

            // The game throws out of these two now and then (an object gone between the list and the
            // look at it); the exception is kept from the frame, but not hidden any more.
            var finalizer = new HarmonyMethod(typeof(ExplosionsPatch).GetMethod(nameof(SuppressExceptionFinalizer), any));
            harmony.Patch(typeof(MyFloatingObjects).GetMethod("UnregisterFloatingObject", BindingFlags.Static | BindingFlags.NonPublic),
                finalizer: finalizer);
            harmony.Patch(typeof(MyFloatingObjects).GetMethod("CheckObjectInVoxel", BindingFlags.Instance | BindingFlags.NonPublic),
                finalizer: finalizer);
        }

        private static MethodInfo Method(string name) =>
            typeof(ExplosionsPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        private static long _suppressed;
        private static long _suppressedLoggedAt;

        public static Exception SuppressExceptionFinalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            var count = Interlocked.Increment(ref _suppressed);
            var now = Stopwatch.GetTimestamp();
            var last = Interlocked.Read(ref _suppressedLoggedAt);
            if (last == 0 || now - last > Stopwatch.Frequency * 60)
            {
                Interlocked.Exchange(ref _suppressedLoggedAt, now);
                Log.Warn(__exception, "Kept an exception out of " + __originalMethod?.Name + " (" + count + " so far)");
            }
            return null;
        }

        /// <summary>
        /// With the plugin's own floating-object cleanup the game's age-ordered lists are not used,
        /// so a new object only goes into synchronisation. Otherwise the game registers it itself.
        ///
        /// The plugin used to register every object itself, three milliseconds later from its
        /// background loop: an object picked up or merged in that time was put back into the lists
        /// after the game had taken it out, and stayed there dead - which is what the exceptions
        /// kept out of UnregisterFloatingObject and CheckObjectInVoxel were about.
        /// </summary>
        private static bool RegisterFloatingObjectPatch(MyFloatingObject obj)
        {
            if (!SentisGameplayImprovementsPlugin.Config.CustomFloatingObjectsCleanup) return true;
            if (obj == null || obj.WasRemovedFromWorld) return false;
            obj.CreationTime = Stopwatch.GetTimestamp();
            if (Sync.IsServer) _addToSynchronization(obj);
            return false;
        }

        /// <summary>
        /// A crate of ammunition or explosives shaken hard enough goes off. This runs in the
        /// parallel update, off the game thread, and the explosion is anything but thread-safe:
        /// it is handed to the game thread, once.
        /// </summary>
        private static bool UpdateAfterSimulationParallelPatched(MyFloatingObject __instance)
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            if (!config.ExplosionTweaks) return true;
            try
            {
                var content = __instance.Item.Content;
                if (!(content is MyObjectBuilder_AmmoMagazine) && content?.SubtypeName != "Explosives") return true;
                var physics = __instance.Physics;
                if (physics == null || !ExplosionMath.ShouldDetonate(physics.LinearAcceleration, config.AccelerationToDamage))
                    return true;

                var crate = __instance;
                if (!Detonating.TryAdd(crate.EntityId, 0)) return true;
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    Detonating.TryRemove(crate.EntityId, out _);
                    try
                    {
                        if (!crate.MarkedForClose && !crate.Closed)
                            crate.DoDamage(999, MyDamageType.Explosion, true, 0, null);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Crate detonation failed");
                    }
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "Crate detonation check failed");
            }
            return true;
        }
    }
}
