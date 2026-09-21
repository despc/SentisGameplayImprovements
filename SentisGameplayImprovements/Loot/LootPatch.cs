using System;
using System.Collections.Generic;
using System.Reflection;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Torch.Managers.PatchManager;
using VRage.Game;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class LootPatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static SortedSet<MyFloatingObject> _floatingOres;
        private static SortedSet<MyFloatingObject> _floatingItems;

        public static void Patch(PatchContext ctx) => global::SentisOptimisations.PatchGuard.Run("LootPatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            _floatingOres = (SortedSet<MyFloatingObject>)(typeof(MyFloatingObjects).GetField("m_floatingOres", any)
                ?? throw new MissingFieldException("MyFloatingObjects.m_floatingOres")).GetValue(null);
            _floatingItems = (SortedSet<MyFloatingObject>)(typeof(MyFloatingObjects).GetField("m_floatingItems", any)
                ?? throw new MissingFieldException("MyFloatingObjects.m_floatingItems")).GetValue(null);

            ctx.GetPattern(typeof(MyFloatingObjects).GetMethod(nameof(MyFloatingObjects.ReduceFloatingObjects), any))
                .Prefixes.Add(Method(nameof(ReduceFloatingObjectsPatched)));
            ctx.GetPattern(typeof(MyShipConnector).GetMethod("TryThrowOutItem", BindingFlags.Instance | BindingFlags.NonPublic))
                .Prefixes.Add(Method(nameof(MethodTryThrowOutItemPatched)));
        }

        private static MethodInfo Method(string name) =>
            typeof(LootPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        /// <summary>A connector set to throw things out destroys ore and ingots instead.</summary>
        private static bool MethodTryThrowOutItemPatched(MyShipConnector __instance)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ConnectorDestroyInsteadThrow) return true;
            try
            {
                var inventory = __instance.GetInventory();
                var items = inventory?.GetItems();
                if (items == null) return true;
                // backwards: removing an item shifts only what comes after it
                for (var i = items.Count - 1; i >= 0; i--)
                {
                    var type = items[i].Content.TypeId;
                    if (type == typeof(MyObjectBuilder_Ore) || type == typeof(MyObjectBuilder_Ingot))
                        inventory.RemoveItems(items[i].ItemId);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Connector clean-out failed");
            }
            return true;
        }

        /// <summary>
        /// The game's own reduction, taking the oldest object with SortedSet.Max - the set is kept
        /// newest first - instead of LINQ's Last(), which walks the whole set for every object it
        /// removes. With the plugin's own cleanup the game's reduction does not run at all.
        /// </summary>
        private static bool ReduceFloatingObjectsPatched()
        {
            if (SentisGameplayImprovementsPlugin.Config.CustomFloatingObjectsCleanup) return false;
            try
            {
                var max = (int)MySession.Static.MaxFloatingObjects;
                var count = _floatingOres.Count + _floatingItems.Count;
                var keepOres = Math.Max(max / 5, 4);
                for (; count > max; count--)
                {
                    var source = _floatingOres.Count > keepOres || _floatingItems.Count == 0 ? _floatingOres : _floatingItems;
                    if (source.Count == 0) continue;
                    var oldest = source.Max;
                    source.Remove(oldest);
                    if (Sync.IsServer) MyFloatingObjects.RemoveFloatingObject(oldest);
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Floating object reduction failed");
            }
            return false;
        }
    }
}
