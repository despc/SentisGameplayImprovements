using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Entities.Inventory;
using Sandbox.Game.World;
using Torch.Managers.PatchManager;
using VRage.Game;
using VRage.Game.Entity;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class LootPatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx)
        {
            var MethodReduceFloatingObjects = typeof(MyFloatingObjects).GetMethod(
                nameof(MyFloatingObjects.ReduceFloatingObjects),
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            ctx.GetPattern(MethodReduceFloatingObjects).Prefixes.Add(
                typeof(LootPatch).GetMethod(nameof(ReduceFloatingObjectsPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));

            var MethodTryThrowOutItem = typeof(MyShipConnector).GetMethod("TryThrowOutItem",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(MethodTryThrowOutItem).Prefixes.Add(
                typeof(LootPatch).GetMethod(nameof(MethodTryThrowOutItemPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
        }

        private static bool MethodTryThrowOutItemPatched(MyShipConnector __instance)
        {
            
            if (!SentisGameplayImprovementsPlugin.Config.ConnectorDestroyInsteadThrow)
            {
                return true;
            }
            
            try
            {
                var myInventory = __instance.GetInventory();
                foreach (var myPhysicalInventoryItem in new List<MyPhysicalInventoryItem>(myInventory.GetItems()))
                {
                    var myObjectBuilderType = myPhysicalInventoryItem.GetItemDefinition().Id.TypeId;
                    if (myObjectBuilderType == typeof(MyObjectBuilder_Ore) ||
                        myObjectBuilderType == typeof(MyObjectBuilder_Ingot))
                    {
                        myInventory.RemoveItems(myPhysicalInventoryItem.ItemId);
                    }
                }
            }
            catch (Exception e)
            {
                //
            }

            return true;
        }

        private static bool ReduceFloatingObjectsPatched()
        {
            if (SentisGameplayImprovementsPlugin.Config.CustomFloatingObjectsCleanup)
            {
                return false;
            }

            try
            {
                SortedSet<MyFloatingObject> m_floatingOres =
                    (SortedSet<MyFloatingObject>)ReflectionUtils.GetPrivateStaticField(typeof(MyFloatingObjects),
                        "m_floatingOres");
                SortedSet<MyFloatingObject> m_floatingItems =
                    (SortedSet<MyFloatingObject>)ReflectionUtils.GetPrivateStaticField(typeof(MyFloatingObjects),
                        "m_floatingItems");
                int num1 = m_floatingOres.Count + m_floatingItems.Count;
                int num2 = Math.Max((int)MySession.Static.MaxFloatingObjects / 5, 4);
                for (; num1 > (int)MySession.Static.MaxFloatingObjects; --num1)
                {
                    SortedSet<MyFloatingObject> source = m_floatingOres.Count > num2 || m_floatingItems.Count == 0
                        ? m_floatingOres
                        : m_floatingItems;
                    if (source.Count > 0)
                    {
                        MyFloatingObject myFloatingObject = source.Last<MyFloatingObject>();
                        source.Remove(myFloatingObject);
                        MyFloatingObjects.RemoveFloatingObject(myFloatingObject);
                    }
                }
            }
            catch (Exception e)
            {
                //
            }

            return false;
        }
    }
}