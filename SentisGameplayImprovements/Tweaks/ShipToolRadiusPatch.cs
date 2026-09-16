using System;
using System.Reflection;
using System.Threading;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using SentisGameplayImprovements.AllGridsActions;
using SpaceEngineers.Game.Entities.Blocks;
using Torch.Managers.PatchManager;
using VRage.Game;
using VRageMath;

namespace SentisGameplayImprovements.Tweaks
{
    /// <summary>
    /// Applies live ship-tool radius multipliers to existing blocks and initializes new blocks
    /// with the current values. All entity and mining-system mutations run on the game thread.
    /// </summary>
    [PatchShim]
    public static class ShipToolRadiusPatch
    {
        private const float MinMultiplier = 0.1f;
        private const float MaxMultiplier = 10f;

        private static readonly FieldInfo DetectorSphereField = typeof(MyShipToolBase).GetField(
            "m_detectorSphere", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ShipDrillIdField = typeof(MyShipDrill).GetField(
            "ShipDrillId", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DrillCutOutRadiusField = typeof(MyDrillCutOut).GetField(
            "Radius", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo DrillCutOutSphereField = typeof(MyDrillCutOut).GetField(
            "m_sphere", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int _refreshQueued;

        public static void Patch(PatchContext ctx) =>
            global::SentisOptimisations.PatchGuard.Run("ShipToolRadiusPatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var parameters = new[] { typeof(MyObjectBuilder_CubeBlock), typeof(MyCubeGrid) };
            var shipToolAddedToScene = typeof(MyShipToolBase).GetMethod("OnAddedToScene",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(object) }, null);
            var drillInit = typeof(MyShipDrill).GetMethod("Init",
                BindingFlags.Instance | BindingFlags.Public, null, parameters, null);

            ctx.GetPattern(shipToolAddedToScene).Suffixes.Add(typeof(ShipToolRadiusPatch).GetMethod(
                nameof(AfterShipToolAddedToScene), BindingFlags.Static | BindingFlags.NonPublic));
            ctx.GetPattern(drillInit).Suffixes.Add(typeof(ShipToolRadiusPatch).GetMethod(
                nameof(AfterDrillInit), BindingFlags.Static | BindingFlags.NonPublic));
        }

        public static float NormalizeMultiplier(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 1f;
            return Math.Max(MinMultiplier, Math.Min(MaxMultiplier, value));
        }

        /// <summary>Queues one coalesced refresh for every currently loaded ship tool.</summary>
        public static void ApplyAllAsync()
        {
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null || !SentisGameplayImprovementsPlugin.TryGetConfig(out _))
                return;
            if (Interlocked.Exchange(ref _refreshQueued, 1) != 0)
                return;

            utilities.InvokeOnGameThread(() =>
            {
                try
                {
                    ApplyAll();
                }
                catch (Exception e)
                {
                    SentisGameplayImprovementsPlugin.Log.Error(e,
                        "Failed to apply ship-tool radius multipliers");
                }
                finally
                {
                    Volatile.Write(ref _refreshQueued, 0);
                }
            });
        }

        private static void ApplyAll()
        {
            if (!SentisGameplayImprovementsPlugin.TryGetConfig(out _))
                return;

            foreach (var grid in EntitiesObserver.MyCubeGrids)
            {
                if (grid == null || grid.MarkedForClose)
                    continue;
                foreach (var block in grid.GetFatBlocks())
                    ApplyToBlock(block);
            }
        }

        /// <summary>Applies the current multipliers to one tool block. Runs on the game thread.</summary>
        public static void ApplyToBlock(MyCubeBlock block)
        {
            if (block == null || block.MarkedForClose ||
                !SentisGameplayImprovementsPlugin.TryGetConfig(out var config))
                return;

            try
            {
                if (block is MyShipWelder welder)
                    ApplyShipTool(welder, config.WelderRadiusMultiplier);
                else if (block is MyShipGrinder grinder)
                    ApplyShipTool(grinder, config.GrinderRadiusMultiplier);
                else if (block is MyShipDrill drill)
                    ApplyDrill(drill, config.DrillRadiusMultiplier);
            }
            catch (Exception e)
            {
                SentisGameplayImprovementsPlugin.Log.Error(e,
                    "Failed to update work radius for block {0}", block.EntityId);
            }
        }

        private static void ApplyShipTool(MyShipToolBase tool, float multiplier)
        {
            var definition = (MyShipToolDefinition)tool.BlockDefinition;
            var sphere = tool.DetectorSphere;
            sphere.Radius = definition.SensorRadius * multiplier;
            DetectorSphereField.SetValue(tool, sphere);
        }

        private static void ApplyDrill(MyShipDrill drill, float multiplier)
        {
            var drillBase = drill.DrillBase;
            var sensor = drillBase?.Sensor;
            if (sensor == null)
                return;

            var definition = (MyShipDrillDefinition)drill.BlockDefinition;
            var sensorRadius = sensor.GetType().GetField("m_radius",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (sensorRadius == null)
                throw new MissingFieldException(sensor.GetType().FullName, "m_radius");

            sensorRadius.SetValue(sensor, definition.SensorRadius * multiplier);
            var worldMatrix = drill.WorldMatrix;
            sensor.OnWorldPositionChanged(ref worldMatrix);

            var cutOut = drillBase.CutOut;
            if (cutOut == null || DrillCutOutRadiusField == null || DrillCutOutSphereField == null)
                throw new MissingFieldException(typeof(MyDrillCutOut).FullName, "Radius/m_sphere");
            var cutOutRadius = definition.CutOutRadius * multiplier;
            DrillCutOutRadiusField.SetValue(cutOut, cutOutRadius);
            cutOut.UpdatePosition(ref worldMatrix);
            var cutOutSphere = cutOut.Sphere;
            cutOutSphere.Radius = cutOutRadius;
            DrillCutOutSphereField.SetValue(cutOut, cutOutSphere);

            // MyShipMiningSystem caches GetDrillingSphere() when a drill is registered.
            // Re-registering rebuilds that cache with the current live multiplier.
            var miningSystem = drill.CubeGrid?.GridSystems?.MiningSystem;
            if (miningSystem != null && ShipDrillIdField != null &&
                (int)ShipDrillIdField.GetValue(drill) >= 0)
            {
                miningSystem.UnRegisterDrill(drill);
                miningSystem.RegisterDrill(drill);
            }
        }

        private static void AfterShipToolAddedToScene(MyShipToolBase __instance)
        {
            if (__instance is MyShipWelder || __instance is MyShipGrinder)
                ApplyToBlock(__instance);
        }

        private static void AfterDrillInit(MyShipDrill __instance) => ApplyToBlock(__instance);
    }
}
