using System;
using System.Reflection;
using NLog;
using Sandbox.Game.World.Generator;
using Torch.Managers.PatchManager;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class ContractPricePatch
    {
        private static readonly double JUMP_DRIVE_DISTANCE = 2000000.0;
        private static readonly float AMOUNT_URANIUM_TO_RECHARGE = 3.75f;
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static Random _random = new Random();
        public static void Patch(PatchContext ctx) => global::SentisOptimisations.PatchGuard.Run("ContractPricePatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            // Each registration is independent: a target removed by a game update only
            // disables its own contract tweak instead of the whole shim.
            TryRegister(ctx, nameof(PatchGetMoneyRewardForAcquisitionContract), () =>
            {
            var MethodGetMoneyRewardForAcquisitionContract = typeof(MyContractTypeAcquisitionStrategy).GetMethod(
                "GetMoneyRewardForAcquisitionContract",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(MethodGetMoneyRewardForAcquisitionContract).Suffixes.Add(
                typeof(ContractPricePatch).GetMethod(nameof(PatchGetMoneyRewardForAcquisitionContract),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            }, "GetMoneyRewardForAcquisitionContract");

            TryRegister(ctx, nameof(PatchGetMoneyReward_Escort), () =>
            {
            var MethodGetMoneyReward_Escort = typeof(MyContractTypeEscortStrategy).GetMethod("GetMoneyReward_Escort",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(MethodGetMoneyReward_Escort).Suffixes.Add(
                typeof(ContractPricePatch).GetMethod(nameof(PatchGetMoneyReward_Escort),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            }, "GetMoneyReward_Escort");

            TryRegister(ctx, nameof(PatchGetMoneyRewardForHaulingContract), () =>
            {
            var MethodGetMoneyRewardForHaulingContract = typeof(MyContractTypeHaulingStrategy).GetMethod(
                "GetMoneyRewardForHaulingContract",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(MethodGetMoneyRewardForHaulingContract).Suffixes.Add(
                typeof(ContractPricePatch).GetMethod(nameof(PatchGetMoneyRewardForHaulingContract),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            }, "GetMoneyRewardForHaulingContract");

            TryRegister(ctx, nameof(PatchGetMoneyRewardForRepairContract), () =>
            {
            var MethodGetMoneyRewardForRepairContract = typeof(MyContractTypeRepairStrategy).GetMethod(
                "GetMoneyRewardForRepairContract",
                BindingFlags.Instance | BindingFlags.NonPublic);

            ctx.GetPattern(MethodGetMoneyRewardForRepairContract).Suffixes.Add(
                typeof(ContractPricePatch).GetMethod(nameof(PatchGetMoneyRewardForRepairContract),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            }, "GetMoneyRewardForRepairContract");
        }

        private static void TryRegister(PatchContext ctx, string suffixName, Action register, string targetName)
        {
            try
            {
                register();
            }
            catch (Exception e)
            {
                Log.Warn(e, $"Contract price target '{targetName}' not patchable; suffix '{suffixName}' skipped.");
            }
        }

        private static void PatchGetMoneyRewardForAcquisitionContract(ref long __result, long baseRew, int amount)
        {
            try
            {
                __result = (long) (baseRew * Math.Pow(2.0, Math.Log10(amount)) *
                                   SentisGameplayImprovementsPlugin.Config.ContractAcquisitionMultiplier);
            }
            catch (Exception e)
            {
                Log.Error("Exception in time PatchGetMoneyRewardForAcquisitionContract", e);
            }
        }

        private static void PatchGetMoneyReward_Escort(ref long __result, long baseRew, double distance)
        {
            try
            {
                __result = (long) (baseRew * Math.Pow(3.0, Math.Log10(distance)) *
                                   SentisGameplayImprovementsPlugin.Config.ContractEscortMultiplier);
            }
            catch (Exception e)
            {
                Log.Error("Exception in time PatchGetMoneyReward_Escort", e);
            }
        }

        private static void PatchGetMoneyRewardForHaulingContract(ref long __result, long baseRew, double distance,
            int uraniumPrice)
        {
            try
            {
                var configContractHaulingtMultiplier = SentisGameplayImprovementsPlugin.Config.ContractHaulingtMultiplier;
                if (Math.Abs(configContractHaulingtMultiplier - 1) < 0.1)
                {
                    return;
                }
                double num1 = distance / JUMP_DRIVE_DISTANCE;
                double num2 = num1 * (uraniumPrice * (double) AMOUNT_URANIUM_TO_RECHARGE);
                __result = (long) ((baseRew + baseRew * num1 + num2) *
                                   configContractHaulingtMultiplier);
            }
            catch (Exception e)
            {
                Log.Error("Exception in time PatchGetMoneyRewardForHaulingContract", e);
            }
        }

        private static void PatchGetMoneyRewardForRepairContract(ref long __result, long baseRew,
            double gridDistance,
            long gridPrice,
            float gridPriceToRewardcoef)
        {
            try
            {
                var configContractRepairMultiplier = SentisGameplayImprovementsPlugin.Config.ContractRepairMultiplier;
                if (Math.Abs(configContractRepairMultiplier - 1) < 0.1)
                {
                    return;
                }
                __result = (long) ((baseRew * Math.Pow(2.0, Math.Log10(gridDistance)) +
                                    (long) (gridPriceToRewardcoef * (double) gridPrice)) *
                                   configContractRepairMultiplier);
            }
            catch (Exception e)
            {
                Log.Error("Exception in time PatchGetMoneyRewardForHaulingContract", e);
            }
        }
    }
}