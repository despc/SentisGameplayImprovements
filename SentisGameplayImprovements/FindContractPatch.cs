using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NLog;
using Sandbox.Game.Contracts;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SentisOptimisations;
using Torch.Managers.PatchManager;
using Torch.Managers.PatchManager.MSIL;
using VRage.Game.Entity;
using VRageMath;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// Search contracts posted by players work: find the grid, bring it back, get paid.
    ///
    /// A player posts a search contract on their contract block for a grid of their own; the game runs it with the NPC
    /// search contract (<c>MyContractFind</c>), which takes for granted that the grid is one the game spawned and
    /// that the contract came from a faction station:
    /// <list type="bullet">
    /// <item>each update it fails the contract when the grid is not NPC-spawned (for an NPC contract this means a
    /// player took the grid over) - a player's grid never is, so the contract failed the frame it was taken;</item>
    /// <item>the place to bring the grid back to is the station's safe zone - a player's contract has no station,
    /// so finding the grid would fail it as well;</item>
    /// <item>the integrity check on delivery and the reward also want an NPC-spawned grid, and would give 0.</item>
    /// </list>
    /// Here, for a player's contract only, the grid counts as the contract's grid wherever the game asks whether it
    /// is NPC-spawned, and the grid is brought back to the contract block it was posted on, within the radius of a
    /// station's safe zone. The rest is the game's: the trigger round the grid, the power check, the integrity the
    /// grid must keep, the reward scaled by it. NPC contracts are untouched.
    /// </summary>
    [PatchShim]
    public static class FindContractPatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo IsNpcSpawned = typeof(MyCubeGrid).GetProperty(nameof(MyCubeGrid.IsNpcSpawnedGrid), Any)?.GetGetMethod(true);
        private static readonly MethodInfo DeliverPositionOf = typeof(MyContractFind).GetMethod("GetDeliverPosition", Any);

        // the game's own delivery place: an open delegate, so NPC contracts go to it without reflection
        private static Func<MyContractFind, BoundingSphereD?> _stationDelivery;

        /// <summary>The patched methods: the method, and whether it asks about the grid and the delivery place.</summary>
        public static readonly (string Method, bool Npc, bool Delivery)[] Targets =
        {
            ("Update", true, true),
            ("EntityEnteredTrigger", false, true),
            ("IsIntegritySufficient", true, false),
            ("GetMoneyRewardForPlayer", true, false),
        };

        public static void Patch(PatchContext ctx) => PatchGuard.Run("FindContractPatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            if (IsNpcSpawned == null) throw new MissingMethodException("MyCubeGrid.IsNpcSpawnedGrid");
            if (DeliverPositionOf == null) throw new MissingMethodException("MyContractFind.GetDeliverPosition");
            _stationDelivery = (Func<MyContractFind, BoundingSphereD?>)Delegate.CreateDelegate(typeof(Func<MyContractFind, BoundingSphereD?>), DeliverPositionOf);
            foreach (var (method, npc, delivery) in Targets)
            {
                var target = typeof(MyContractFind).GetMethod(method, Any) ?? throw new MissingMethodException("MyContractFind", method);
                ctx.GetPattern(target).Transpilers.Add(typeof(FindContractPatch).GetMethod(
                    npc && delivery ? nameof(TranspileBoth) : npc ? nameof(TranspileNpc) : nameof(TranspileDelivery), BindingFlags.Static | BindingFlags.NonPublic));
            }
        }

        internal static IEnumerable<MsilInstruction> TranspileBoth(IEnumerable<MsilInstruction> instructions) => Transpile(instructions, true, true);
        internal static IEnumerable<MsilInstruction> TranspileNpc(IEnumerable<MsilInstruction> instructions) => Transpile(instructions, true, false);
        internal static IEnumerable<MsilInstruction> TranspileDelivery(IEnumerable<MsilInstruction> instructions) => Transpile(instructions, false, true);

        /// <summary>
        /// <c>grid.IsNpcSpawnedGrid</c> becomes <see cref="CountsAsNpc"/>(grid, this), and <c>this.GetDeliverPosition()</c>
        /// becomes <see cref="DeliverPosition"/>(this): both take what was on the stack and leave the same type.
        /// </summary>
        internal static IEnumerable<MsilInstruction> Transpile(IEnumerable<MsilInstruction> instructions, bool npc, bool delivery)
        {
            var list = instructions.ToList();
            int npcCalls = 0, deliveryCalls = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var instruction = list[i];
                if ((instruction.OpCode != OpCodes.Callvirt && instruction.OpCode != OpCodes.Call) ||
                    !(instruction.Operand is MsilOperandInline<MethodBase> operand)) continue;
                if (npc && operand.Value == IsNpcSpawned)
                {
                    var loadThis = new MsilInstruction(OpCodes.Ldarg_0);
                    foreach (var label in instruction.Labels) loadThis.Labels.Add(label);
                    list[i] = new MsilInstruction(OpCodes.Call).InlineValue((MethodBase)typeof(FindContractPatch).GetMethod(nameof(CountsAsNpc)));
                    list.Insert(i, loadThis);
                    i++;
                    npcCalls++;
                }
                else if (delivery && operand.Value == DeliverPositionOf)
                {
                    var call = new MsilInstruction(OpCodes.Call).InlineValue((MethodBase)typeof(FindContractPatch).GetMethod(nameof(DeliverPosition)));
                    foreach (var label in instruction.Labels) call.Labels.Add(label);
                    list[i] = call;
                    deliveryCalls++;
                }
            }
            if (npc && npcCalls == 0 || delivery && deliveryCalls == 0)
                throw new InvalidOperationException("FindContractPatch: the game's search contract changed, found " + npcCalls +
                                                    " IsNpcSpawnedGrid and " + deliveryCalls + " GetDeliverPosition calls");
            return list;
        }

        /// <summary>The game's NPC-spawned flag; a player's contract takes its grid as it is.</summary>
        public static bool CountsAsNpc(MyCubeGrid grid, MyContractFind contract) => grid.IsNpcSpawnedGrid || contract.IsPlayerMade;

        /// <summary>
        /// Where the found grid goes: for an NPC contract the game's place (the station's safe zone), for a player's
        /// the contract block it was posted on, within the same radius; none if the block is gone.
        /// </summary>
        public static BoundingSphereD? DeliverPosition(MyContractFind contract)
        {
            if (!contract.IsPlayerMade) return _stationDelivery(contract);
            if (contract.StartBlock == 0 || !(MyEntities.GetEntityById(contract.StartBlock) is MyEntity block)) return null;
            return new BoundingSphereD(block.PositionComp.GetPosition(), MyFactionStation.SAFEZONE_SIZE);
        }
    }
}
