using System.Reflection;
using Havok;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using Torch.Managers.PatchManager;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;

namespace SentisGameplayImprovements.Assholes;

[PatchShim]
internal static class LandingGearPatch
{
    public static void Patch(PatchContext ctx)
    {
        MethodInfo method = typeof(MyLandingGear).GetMethod("CanAttachTo",
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        ctx.GetPattern(method).Prefixes.Add(typeof(LandingGearPatch).GetMethod("CanAttachToPatched",
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
    }

    private static bool CanAttachToPatched(MyLandingGear __instance,
        HkBodyCollision obj, MyEntity entity, Vector3D worldPos, ref bool __result)
    {
        MyCubeGrid gridWithGear = __instance.CubeGrid;
        if (entity == gridWithGear || entity.MarkedForClose)
        {
            __result = false;
            return false;   
        }

        MyEntity topMostParent = entity.GetTopMostParent();
        if (topMostParent == gridWithGear || topMostParent.MarkedForClose ||
            topMostParent is MyCubeGrid myCubeGrid && myCubeGrid.IsPreview)
        {
            __result = false;
            return false;
        }

        if (topMostParent is MyCubeGrid)
        {
            if (SentisGameplayImprovementsPlugin.Config.DisableLockLandingGearOnNPCShips && ((MyCubeGrid)topMostParent).IsNpcGrid())
            {
                __result = false;
                return false;
            }
            
            if (SentisGameplayImprovementsPlugin.Config.DisableLockLandingGearOnEnemyShips && ((MyCubeGrid)topMostParent).IsNpcGrid())
            {
                var owner = PlayerUtils.GetOwner(gridWithGear);
                if (owner == 0)
                {
                    return true;
                }
                var player = PlayerUtils.GetPlayer(owner);
                if (player == null)
                {
                    return true;
                }

                var targetOwner = PlayerUtils.GetOwner((MyCubeGrid)topMostParent);
                if (targetOwner == 0)
                {
                    return true;
                }
                if (player.GetRelationTo(targetOwner) ==
                    MyRelationsBetweenPlayerAndBlock.Enemies)
                {
                    __result = false;
                    return false;
                }
            }
        }
        return true;
    }
}