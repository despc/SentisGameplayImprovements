using System.Reflection;
using NAPI;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using SentisOptimisations;
using Torch.Managers.PatchManager;

namespace SentisGameplayImprovements.PveZone
{
    /// <summary>
    /// A drill does not cut into the blocks of another player's grid in the PvE zone. The damage a drill does
    /// to a block goes past the damage handler as a deformation, so the drilling itself is stopped - only for
    /// a target grid in the zone that the drill's owner may not damage (<see cref="DamageHandler.Blocks"/>);
    /// one's own grids and the grids outside the zone are drilled as always.
    /// </summary>
    [PatchShim]
    internal static class MyDrillDamageFix
    {
        private static FieldInfo drillEntity;

        public static void Patch(PatchContext ctx) => PatchGuard.Run("MyDrillDamageFix", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            drillEntity = typeof(MyDrillBase).easyField("m_drillEntity");
            ctx.Prefix(typeof(MyDrillBase), typeof(MyDrillDamageFix), nameof(TryDrillBlocks));
        }

        private static bool TryDrillBlocks(MyDrillBase __instance, MyCubeGrid grid, bool onlyCheck, ref bool __result)
        {
            if (onlyCheck || !SentisGameplayImprovementsPlugin.Config.PvEZoneEnabled || !PvECore.IsProtected(grid))
                return true;

            long owner;
            switch (drillEntity.GetValue(__instance))
            {
                case MyHandDrill handDrill:
                    owner = handDrill.OwnerIdentityId;
                    break;
                case MyShipDrill shipDrill:
                    owner = shipDrill.OwnerId;
                    break;
                default:
                    return true;
            }
            if (!DamageHandler.Blocks(owner, grid)) return true;
            __result = false;
            return false;
        }
    }
}
