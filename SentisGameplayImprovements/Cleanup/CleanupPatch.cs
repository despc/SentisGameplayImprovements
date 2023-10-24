using System;
using System.Linq;
using System.Reflection;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.SessionComponents;
using Torch.Managers.PatchManager;
using VRage.Game.Entity;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class CleanupPatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx)
        {
            var Method = typeof(MySessionComponentTrash).GetMethod("MyEntities_OnEntityAdd",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(Method).Prefixes.Add(
                typeof(CleanupPatch).GetMethod(nameof(OnEntityAddPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
           
        }

        private static bool OnEntityAddPatched(MyEntity entity)
        {
            try
            {
                if (!(entity is MyCubeGrid))
                    return false;

                var configIgnoreCleanupSubtypes = SentisGameplayImprovementsPlugin.Config.IgnoreCleanupSubtypes;
                if (string.IsNullOrEmpty(configIgnoreCleanupSubtypes))
                {
                    return true;
                }

                var containsIgnoreCleanupBlocks = ((MyCubeGrid)entity).GetFatBlocks().Where(block =>
                {
                    foreach (var s in configIgnoreCleanupSubtypes.Split(','))
                    {
                        if (block.BlockDefinition.Id.SubtypeName.Contains(s))
                        {
                            return true;
                        }
                    }

                    return false;
                }).Any();

                return !containsIgnoreCleanupBlocks;
            }
            catch (Exception e)
            {
                Log.Error("Exception in CleanupPatch", e);
            }
            return true;
        }
    }
}