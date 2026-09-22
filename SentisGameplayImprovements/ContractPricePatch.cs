using System;
using System.Reflection;
using NLog;
using Sandbox.Game.World.Generator;
using Torch.Managers.PatchManager;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// Multiplies the money reward of the game's contracts, one multiplier per kind:
    /// <list type="bullet">
    /// <item>acquisition (bring items to a station) - <c>ContractAcquisitionMultiplier</c>;</item>
    /// <item>escort - <c>ContractEscortMultiplier</c>;</item>
    /// <item>hauling - a package and a grid alike, the game prices both with one method -
    /// <c>ContractHaulingtMultiplier</c>;</item>
    /// <item>repair - <c>ContractRepairMultiplier</c>.</item>
    /// </list>
    /// Each is a suffix on the game's own reward method that scales what it returned, so the game's formula
    /// stays the game's; the game then rounds the reward down to thousands as before.
    /// </summary>
    [PatchShim]
    public static class ContractPricePatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>The patched methods: the type, the method, the suffix.</summary>
        public static readonly (Type Type, string Method, string Suffix)[] Targets =
        {
            (typeof(MyContractTypeAcquisitionStrategy), "GetMoneyRewardForAcquisitionContract", nameof(AcquisitionSuffix)),
            (typeof(MyContractTypeEscortStrategy), "GetMoneyReward_Escort", nameof(EscortSuffix)),
            // used by the hauling of a package and of a grid
            (typeof(MyContractTypeBaseStrategy), "GetHaulingMoneyReward", nameof(HaulingSuffix)),
            (typeof(MyContractTypeRepairStrategy), "GetMoneyRewardForRepairContract", nameof(RepairSuffix)),
        };

        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Patch(PatchContext ctx) => global::SentisOptimisations.PatchGuard.Run("ContractPricePatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            // Each target on its own: one removed by a game update only disables its own multiplier.
            foreach (var (type, method, suffix) in Targets)
            {
                try
                {
                    var target = type.GetMethod(method, Any) ?? throw new MissingMethodException(type.FullName, method);
                    ctx.GetPattern(target).Suffixes.Add(typeof(ContractPricePatch).GetMethod(suffix, BindingFlags.Static | BindingFlags.NonPublic));
                }
                catch (Exception e)
                {
                    Log.Warn(e, "Contract reward '" + type.Name + "." + method + "' not patchable; its multiplier is off.");
                }
            }
        }

        /// <summary>
        /// The reward times the multiplier, kept between 0 and <see cref="long.MaxValue"/>: a reward that would
        /// not fit a long no longer wraps into a negative one, and a negative or broken multiplier gives nothing.
        /// </summary>
        public static long Scale(long reward, double multiplier)
        {
            var scaled = reward * multiplier;
            if (double.IsNaN(scaled) || scaled <= 0) return 0;
            return scaled >= long.MaxValue ? long.MaxValue : (long)scaled;
        }

        private static void AcquisitionSuffix(ref long __result) =>
            __result = Scale(__result, SentisGameplayImprovementsPlugin.Config.ContractAcquisitionMultiplier);

        private static void EscortSuffix(ref long __result) =>
            __result = Scale(__result, SentisGameplayImprovementsPlugin.Config.ContractEscortMultiplier);

        private static void HaulingSuffix(ref long __result) =>
            __result = Scale(__result, SentisGameplayImprovementsPlugin.Config.ContractHaulingtMultiplier);

        private static void RepairSuffix(ref long __result) =>
            __result = Scale(__result, SentisGameplayImprovementsPlugin.Config.ContractRepairMultiplier);
    }
}
