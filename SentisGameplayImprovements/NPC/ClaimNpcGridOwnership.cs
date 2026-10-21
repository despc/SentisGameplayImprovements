using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using SentisOptimisations;
using Torch.Managers;
using Torch.Managers.PatchManager;
using VRage.Game;
using VRage.Groups;
using VRage.Network;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// "Claim NPC grid" (the terminal's button, <c>MyBlockLimits.ClaimNPCGrid</c>) also makes the player the owner of the
    /// grid's blocks, with <c>ClaimNpcGridTakesOwnership</c> (off by default).
    ///
    /// The game's claim moves only the authorship: the grid's PCU and block limits go to the player and the grid no longer
    /// despawns, the blocks stay the NPC's ("Blocks will still need to be hacked to gain ownership", its dialog says).
    /// Here, after a claim that went through (the grid is no longer an NPC's spawn), the blocks of the claimed grids (the
    /// grid and the subgrids the claim took with it) owned by an NPC identity go to the player, not shared. Blocks of
    /// players, and of nobody, stay as they are. The change goes to every client as the game's own ownership transfer
    /// sends it (<c>MyCubeGrid.OnChangeOwnersSuccess</c>).
    /// </summary>
    [PatchShim]
    public static class ClaimNpcGridOwnership
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static MethodInfo _ownersChanged;
        // the grids of the claimed one's group that were NPC spawns before the claim (game thread)
        private static readonly List<MyCubeGrid> Before = new List<MyCubeGrid>();

        public static void Patch(PatchContext ctx) => PatchGuard.Run("ClaimNpcGridOwnership", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var claim = typeof(MyBlockLimits).GetMethod("ClaimNPCGrid", Any) ?? throw new MissingMethodException("MyBlockLimits.ClaimNPCGrid");
            _ownersChanged = typeof(MyCubeGrid).GetMethod("OnChangeOwnersSuccess", Any) ?? throw new MissingMethodException("MyCubeGrid.OnChangeOwnersSuccess");
            var pattern = ctx.GetPattern(claim);
            pattern.Prefixes.Add(typeof(ClaimNpcGridOwnership).GetMethod(nameof(ClaimPrefix), Any));
            pattern.Suffixes.Add(typeof(ClaimNpcGridOwnership).GetMethod(nameof(ClaimSuffix), Any));
        }

        private static void ClaimPrefix(long gridEntityId)
        {
            Before.Clear();
            try
            {
                if (SentisGameplayImprovementsPlugin.Config?.ClaimNpcGridTakesOwnership != true) return;
                if (!(MyEntities.GetEntityById(gridEntityId) is MyCubeGrid grid)) return;
                foreach (var node in MyCubeGridGroups.Static.Mechanical.GetGroup(grid)?.Nodes ?? Enumerable.Empty<MyGroups<MyCubeGrid, MyGridMechanicalGroupData>.Node>())
                    if (node.NodeData.IsNpcSpawnedGrid)
                        Before.Add(node.NodeData);
            }
            catch (Exception e)
            {
                Log.Error(e, "Claim NPC grid: the grids before the claim were not noted; the claim gives no ownership");
                Before.Clear();
            }
        }

        private static void ClaimSuffix(long gridEntityId)
        {
            try
            {
                if (Before.Count == 0) return;
                var sender = MyEventContext.Current.Sender.Value;
                var player = sender == 0 ? 0 : MySession.Static.Players.TryGetIdentityId(sender);
                if (player == 0) return;
                var claimed = Before.Where(g => !g.MarkedForClose && !g.IsNpcSpawnedGrid).ToList();
                var blocks = 0;
                foreach (var grid in claimed)
                {
                    var requests = NpcOwned(grid).Select(b => new MyCubeGrid.MySingleOwnershipRequest
                    {
                        BlockId = b.EntityId,
                        Owner = player,
                        ShareMode = MyOwnershipShareModeEnum.None,
                    }).ToList();
                    if (requests.Count == 0) continue;
                    // on the server as the game's transfer applies it, then to every client
                    _ownersChanged.Invoke(null, new object[] { requests });
                    NetworkManager.RaiseStaticEvent(_ownersChanged, requests);
                    blocks += requests.Count;
                }
                if (blocks > 0)
                    Log.Info($"Claim NPC grid: {blocks} blocks of {string.Join(", ", claimed.Select(g => g.DisplayName))} now owned by " +
                             $"{MySession.Static.Players.TryGetIdentity(player)?.DisplayName} ({sender})");
            }
            catch (Exception e)
            {
                Log.Error(e, "Claim NPC grid: the ownership of the blocks did not change");
            }
            finally
            {
                Before.Clear();
            }
        }

        /// <summary>The blocks of a grid owned by an NPC identity.</summary>
        public static IEnumerable<MyCubeBlock> NpcOwned(MyCubeGrid grid) =>
            grid.GetFatBlocks().Where(b => b.OwnerId != 0 && MySession.Static.Players.IdentityIsNpc(b.OwnerId));
    }
}
