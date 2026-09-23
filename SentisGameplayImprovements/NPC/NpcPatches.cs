using System;
using System.Collections.Generic;
using System.Reflection;
using NLog;
using Sandbox.Game.World;
using SentisOptimisations;
using Torch.Managers.PatchManager;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class NpcPatches
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx) => PatchGuard.Run("NpcPatches", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var MethodCheckLimitsAndNotify = typeof(MySession).GetMethod
                (nameof(MySession.CheckLimitsAndNotify), BindingFlags.Instance | BindingFlags.Public);

            ctx.GetPattern(MethodCheckLimitsAndNotify).Prefixes.Add(
                typeof(NpcPatches).GetMethod(nameof(CheckLimitsAndNotifyPatch),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            
        }


        private static bool CheckLimitsAndNotifyPatch(long ownerID,
            string blockName,
            int pcuToBuild,
            int blocksToBuild,
            int blocksCount,
            Dictionary<string, int> blocksPerType, ref bool __result)
        {
        try
        {
            if (MySession.Static.Players.IdentityIsNpc(ownerID))
            {
                __result = true;
                return false;
            }
            return true;
        


            }
                catch (Exception __guard_e)
                {
                    Log.Error("CheckLimitsAndNotifyPatch exception " + __guard_e);
                    return true;  // fall back to vanilla behavior
                }
        }
    }
}