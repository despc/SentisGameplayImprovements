using System;
using System.Reflection;
using NLog;
using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.EntityComponents.GameLogic;
using Torch.Managers.PatchManager;
using VRage;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class MedkitPatches
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx)
        {
            var MethodProvideSupport = typeof(MyLifeSupportingComponent).GetMethod
                (nameof(MyLifeSupportingComponent.ProvideSupport), BindingFlags.Instance | BindingFlags.Public);

            ctx.GetPattern(MethodProvideSupport).Prefixes.Add(
                typeof(MedkitPatches).GetMethod(nameof(ProvideSupportPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            
        }
        
        private static bool ProvideSupportPatched(MyLifeSupportingComponent __instance, MyCharacter user)
        {
            var myLifeSupportingBlock = __instance.Entity;
            if (!myLifeSupportingBlock.IsWorking)
                return false;
            var character = __instance.User;
            if (character == null)
            {
                character = user;
                if (myLifeSupportingBlock.RefuelAllowed)
                {
                    try
                    {
                        var resourceStateByType = ((MyCubeGrid)myLifeSupportingBlock.CubeGrid).GridSystems
                            .ResourceDistributor.ResourceStateByType(MyResourceDistributorComponent.HydrogenId, false);
                        if (resourceStateByType == MyResourceStateEnum.NoPower)
                        {
                            return true;
                        }
                        var characterInventory = character.GetInventoryBase();
                        foreach (var item in characterInventory.GetItems())
                        {
                            if (item.Content is MyObjectBuilder_GasContainerObject)
                            {
                                if (((MyObjectBuilder_GasContainerObject)item.Content).GasLevel > 1.0)
                                {
                                    continue;
                                }
                                ((MyObjectBuilder_GasContainerObject) item.Content).GasLevel = 1.02f;
                                characterInventory.OnContentsChanged();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Fill bottle exception");
                    }
                    
                }
            }
            return true;
        }
    }
}