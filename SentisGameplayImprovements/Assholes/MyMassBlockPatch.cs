using System.Reflection;
using Sandbox.Definitions;
using SpaceEngineers.Game.Entities.Blocks;
using Torch.Managers.PatchManager;

namespace SentisGameplayImprovements.Assholes
{
    [PatchShim]

    internal static class ArtificialMassBlockPatch
    {
        public static void Patch(PatchContext ctx)
        {
            MethodInfo method = typeof(MyVirtualMass).GetMethod("Init",
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            ctx.GetPattern(method).Prefixes.Add(typeof(ArtificialMassBlockPatch).GetMethod("MassInit",
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static void MassInit(MyVirtualMass __instance)
        {
            if (!SentisGameplayImprovementsPlugin.Config.DisableArtificialMass)
                return;

            ((MyVirtualMassDefinition)__instance.BlockDefinition).VirtualMass = 0f;
        }
    }
}
