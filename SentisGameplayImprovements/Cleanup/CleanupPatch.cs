using System;
using System.Reflection;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.SessionComponents;
using SentisOptimisations;
using Torch.Managers.PatchManager;
using VRage.Game.Entity;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class CleanupPatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx) => PatchGuard.Run("CleanupPatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var Method = typeof(MySessionComponentTrash).GetMethod("MyEntities_OnEntityAdd",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(Method).Prefixes.Add(
                typeof(CleanupPatch).GetMethod(nameof(OnEntityAddPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
           
        }

        // the setting split once, not once for every block of every grid that comes into the world
        private static string _ignoredSetting;
        private static string[] _ignored = new string[0];

        private static string[] IgnoredSubtypes(string setting)
        {
            if (setting != _ignoredSetting)
            {
                _ignored = setting.Split(',');
                _ignoredSetting = setting;
            }
            return _ignored;
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

                var subtypes = IgnoredSubtypes(configIgnoreCleanupSubtypes);
                foreach (var block in ((MyCubeGrid)entity).GetFatBlocks())
                {
                    var subtype = block.BlockDefinition.Id.SubtypeName;
                    foreach (var ignored in subtypes)
                        if (subtype.Contains(ignored))
                            return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Exception in CleanupPatch", e);
            }
            return true;
        }
    }
}