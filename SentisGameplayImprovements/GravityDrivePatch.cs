using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NLog;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Entities.Blocks;
using Torch.Managers.PatchManager;
using Torch.Managers.PatchManager.MSIL;
using VRage.Game.Components;
using VRageMath;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// No gravity drives: a gravity generator does not push artificial mass on the ship it stands
    /// on.
    ///
    /// A generator pulls every artificial mass (and space ball) in its field and pushes the grid
    /// the mass is on - including its own grid, so a generator and a few masses on one ship move the
    /// ship by themselves, with no thrust. Here the push is dropped when the mass's grid and the
    /// generator's grid are one body: the same grid, or grids joined into one physical group -
    /// subgrids on rotors, pistons and hinges, ships docked with connectors or locked with landing
    /// gear. A generator still pushes anything that is not joined to it: gravity cannons, slings,
    /// masses on another ship.
    /// </summary>
    [PatchShim]
    public static class GravityDrivePatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>Pushes dropped because the mass was on the generator's own ship.</summary>
        public static long Dropped;

        private static readonly MethodInfo AddForce = typeof(MyPhysicsComponentBase).GetMethod(nameof(MyPhysicsComponentBase.AddForce),
            new[] { typeof(MyPhysicsForceType), typeof(Vector3?), typeof(Vector3D?), typeof(Vector3?), typeof(float?), typeof(bool), typeof(bool) });

        public static void Patch(PatchContext ctx) => global::SentisOptimisations.PatchGuard.Run("GravityDrivePatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            if (AddForce == null) throw new MissingMethodException("MyPhysicsComponentBase.AddForce");
            var update = typeof(MyGravityGeneratorBase).GetMethod(nameof(MyGravityGeneratorBase.UpdateBeforeSimulation),
                BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException("MyGravityGeneratorBase.UpdateBeforeSimulation");
            ctx.GetPattern(update).Transpilers.Add(
                typeof(GravityDrivePatch).GetMethod(nameof(Transpiler), BindingFlags.Static | BindingFlags.NonPublic));
        }

        /// <summary>
        /// The loop pushes twice: first artificial mass (the mass's grid), then loose objects. The
        /// first AddForce becomes <see cref="PushMass"/>, given the generator as well; the second,
        /// for loose objects, stays.
        /// </summary>
        internal static IEnumerable<MsilInstruction> Transpiler(IEnumerable<MsilInstruction> instructions)
        {
            var list = instructions.ToList();
            var calls = new List<int>();
            for (var i = 0; i < list.Count; i++)
            {
                var instruction = list[i];
                if ((instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Call) &&
                    instruction.Operand is MsilOperandInline<MethodBase> operand && operand.Value == AddForce)
                    calls.Add(i);
            }
            if (calls.Count != 2)
                throw new InvalidOperationException("GravityDrivePatch: expected two AddForce calls in MyGravityGeneratorBase.UpdateBeforeSimulation, found " + calls.Count);

            var at = calls[0];
            var loadThis = new MsilInstruction(OpCodes.Ldarg_0);
            foreach (var label in list[at].Labels) loadThis.Labels.Add(label);
            list[at] = new MsilInstruction(OpCodes.Call).InlineValue((MethodBase)typeof(GravityDrivePatch).GetMethod(nameof(PushMass)));
            list.Insert(at, loadThis);
            return list;
        }

        /// <summary>The generator's push of an artificial mass, unless the mass is on the generator's own ship.</summary>
        public static void PushMass(MyPhysicsComponentBase physics, MyPhysicsForceType type, Vector3? force, Vector3D? position, Vector3? torque,
            float? maxSpeed, bool applyImmediately, bool activeOnly, MyGravityGeneratorBase generator)
        {
            try
            {
                if (physics?.Entity is MyCubeGrid massGrid && generator?.CubeGrid != null && OneBody(massGrid, generator.CubeGrid))
                {
                    System.Threading.Interlocked.Increment(ref Dropped);
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Gravity drive check failed");
            }
            physics?.AddForce(type, force, position, torque, maxSpeed, applyImmediately, activeOnly);
        }

        /// <summary>The same grid, or grids joined into one physical group.</summary>
        public static bool OneBody(MyCubeGrid a, MyCubeGrid b)
        {
            if (a == b) return true;
            var group = MyCubeGridGroups.Static.Physical.GetGroup(a);
            return group != null && group == MyCubeGridGroups.Static.Physical.GetGroup(b);
        }
    }
}
