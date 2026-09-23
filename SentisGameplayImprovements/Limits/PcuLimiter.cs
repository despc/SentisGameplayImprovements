using System;
using System.Collections.Generic;
using NLog;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.Entities.Blocks.SafeZone;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// The PCU limit of a group of grids (joined mechanically - rotors, pistons, hinges - and with
    /// <c>IncludeConnectedGrids</c> by connectors too).
    ///
    /// Every <see cref="PassSeconds"/> seconds each group that has a dynamic grid is counted, on the game thread
    /// and spread over frames (<see cref="CheckSlice"/>, at most <see cref="BlocksPerSlice"/> blocks a call). The
    /// limit is <c>MaxDinamycGridPCU</c>, or <c>MaxStaticGridPCU</c> when the group has a static grid; with no
    /// enemy player within <see cref="EnemyRadius"/> m it is enforced only <see cref="GraceWithoutEnemies"/>
    /// PCU above that. The owner is told whenever the group is over the limit. A group over the enforced limit
    /// has its functional blocks switched off (the ones that keep the ship alive stay on), and on the
    /// <see cref="StrikesBeforeStatic"/>-th check in a row over it its biggest grid is made static. NPC groups are
    /// not limited.
    /// </summary>
    public class PcuLimiter
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public const double PassSeconds = 30;
        public const int GraceWithoutEnemies = 5000;
        public const double EnemyRadius = 15000;
        public const int StrikesBeforeStatic = 5;
        public const double NotifyRadius = 10000;
        private const int BlocksPerSlice = 20000;

        /// <summary>What to do with a group, from <see cref="Decide"/>.</summary>
        public struct Verdict
        {
            /// <summary>The limit that applies to the group.</summary>
            public int Limit;
            /// <summary>Over the limit: the owner is told.</summary>
            public bool OverLimit;
            /// <summary>Over the enforced limit: the functional blocks are switched off.</summary>
            public bool Enforce;
            /// <summary>The checks in a row over the enforced limit, this one included; 0 after a conversion.</summary>
            public int Strikes;
            /// <summary>The group's biggest grid is made static.</summary>
            public bool ConvertToStatic;
        }

        public static Verdict Decide(int pcu, bool hasStatic, bool enemyNear, int strikes, int maxStaticPcu, int maxDynamicPcu)
        {
            var limit = hasStatic ? maxStaticPcu : maxDynamicPcu;
            var verdict = new Verdict { Limit = limit, OverLimit = pcu > limit };
            var enforced = enemyNear ? limit : limit + GraceWithoutEnemies;
            if (pcu <= enforced) return verdict;
            verdict.Enforce = true;
            verdict.Strikes = strikes + 1;
            if (verdict.Strikes >= StrikesBeforeStatic)
            {
                verdict.ConvertToStatic = true;
                verdict.Strikes = 0;
            }
            return verdict;
        }

        private readonly List<List<MyCubeGrid>> _queue = new List<List<MyCubeGrid>>();
        private int _cursor;
        private DateTime _lastPass = DateTime.MinValue;
        // checks in a row over the enforced limit, by the group's biggest grid
        private readonly Dictionary<long, int> _strikes = new Dictionary<long, int>();
        private readonly HashSet<long> _seen = new HashSet<long>();

        /// <summary>Checks the next groups, at most <see cref="BlocksPerSlice"/> blocks; the game thread.</summary>
        public void CheckSlice()
        {
            if (_cursor >= _queue.Count)
            {
                if ((DateTime.UtcNow - _lastPass).TotalSeconds < PassSeconds) return;
                Rebuild();
            }
            var blocks = 0;
            while (_cursor < _queue.Count && blocks < BlocksPerSlice)
            {
                var group = _queue[_cursor++];
                try
                {
                    blocks += CheckGroup(group);
                }
                catch (Exception e)
                {
                    Log.Error(e, "PCU limit check failed");
                }
            }
            if (_cursor >= _queue.Count) ForgetGone();
        }

        /// <summary>The groups with a dynamic grid, one list per group.</summary>
        private void Rebuild()
        {
            _lastPass = DateTime.UtcNow;
            _queue.Clear();
            _cursor = 0;
            _seen.Clear();
            if (SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids)
            {
                foreach (var group in MyCubeGridGroups.Static.Electrical.Groups)
                {
                    var grids = new List<MyCubeGrid>();
                    foreach (var node in group.Nodes) grids.Add(node.NodeData);
                    Add(grids);
                }
            }
            else
            {
                foreach (var group in MyCubeGridGroups.Static.Mechanical.Groups)
                {
                    var grids = new List<MyCubeGrid>();
                    foreach (var node in group.Nodes) grids.Add(node.NodeData);
                    Add(grids);
                }
            }
        }

        private void Add(List<MyCubeGrid> grids)
        {
            foreach (var grid in grids)
            {
                if (grid != null && !grid.IsStatic)
                {
                    _queue.Add(grids);
                    return;
                }
            }
        }

        /// <summary>What a group costs the slice's budget besides the blocks it counts.</summary>
        private const int GroupCost = 10;

        /// <summary>
        /// Checks one group; returns what it cost, in blocks. The game keeps a PCU count of every grid
        /// (<see cref="MyCubeGrid.BlocksPCU"/>) that is never below the limiter's - an unfinished block is 1 in it
        /// and 0 here, a projection counts there - so a group within the limit by it is within the limit, and
        /// only a group over it has its blocks counted.
        /// </summary>
        private int CheckGroup(List<MyCubeGrid> group)
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            if (!config.EnabledPcuLimiter) return 0;
            MyCubeGrid biggest = null;
            var upperBound = 0;
            var hasStatic = false;
            var hasDynamic = false;
            foreach (var grid in group)
            {
                if (!Counts(grid)) continue;
                upperBound += grid.BlocksPCU;
                if (grid.IsStatic) hasStatic = true;
                else hasDynamic = true;
                if (biggest == null || grid.BlocksPCU > biggest.BlocksPCU) biggest = grid;
            }
            if (biggest == null || !hasDynamic) return GroupCost;

            var owner = PlayerUtils.GetOwner(biggest);
            if (owner != 0 && MySession.Static.Players.IdentityIsNpc(owner)) return GroupCost;
            _seen.Add(biggest.EntityId);

            var limit = hasStatic ? config.MaxStaticGridPCU : config.MaxDinamycGridPCU;
            var pcu = upperBound;
            var blocks = 0;
            if (upperBound > limit)
            {
                pcu = 0;
                foreach (var grid in group)
                {
                    if (!Counts(grid)) continue;
                    pcu += GridPcu(grid);
                    blocks += grid.BlocksCount;
                }
            }

            _strikes.TryGetValue(biggest.EntityId, out var strikes);
            // whether an enemy is near matters only over the limit
            var enemyNear = pcu <= limit || EnemyNear(owner, biggest.PositionComp.GetPosition());
            var verdict = Decide(pcu, hasStatic, enemyNear, strikes, config.MaxStaticGridPCU, config.MaxDinamycGridPCU);
            if (verdict.Strikes == 0) _strikes.Remove(biggest.EntityId);
            else _strikes[biggest.EntityId] = verdict.Strikes;

            if (verdict.OverLimit) SendLimitMessage(owner, pcu, verdict.Limit, biggest.DisplayName);
            if (verdict.Enforce)
            {
                foreach (var grid in group) SwitchOff(grid);
            }
            if (verdict.ConvertToStatic) MakeStatic(biggest, owner);
            return blocks + GroupCost;
        }

        private static bool Counts(MyCubeGrid grid) =>
            grid != null && !grid.MarkedForClose && !grid.Closed && grid.Physics != null && !grid.IsPreview;

        /// <summary>
        /// The PCU of the grid's group (see the class), and whether the group has a static grid. With
        /// <paramref name="exact"/> false it is the game's count, never below the exact one and free to take; the
        /// exact one counts the blocks. The game thread.
        /// </summary>
        public static int GroupPcu(MyCubeGrid grid, bool exact, out bool hasStatic)
        {
            hasStatic = grid.IsStatic;
            var pcu = exact ? GridPcu(grid) : grid.BlocksPCU;
            foreach (var other in GridUtils.GetSubGrids(grid, SentisGameplayImprovementsPlugin.Config.IncludeConnectedGrids))
            {
                if (!(other is MyCubeGrid subGrid) || !Counts(subGrid)) continue;
                if (subGrid.IsStatic) hasStatic = true;
                pcu += exact ? GridPcu(subGrid) : subGrid.BlocksPCU;
            }
            return pcu;
        }

        private static int GridPcu(MyCubeGrid grid)
        {
            var pcu = 0;
            foreach (var block in grid.CubeBlocks) pcu += BlockUtils.GetPCU(block);
            return pcu;
        }

        private static bool EnemyNear(long owner, Vector3D position)
        {
            foreach (var player in PlayerUtils.GetAllPlayers())
            {
                if (player.GetRelationTo(owner) == MyRelationsBetweenPlayerAndBlock.Enemies &&
                    Vector3D.Distance(player.GetPosition(), position) <= EnemyRadius)
                    return true;
            }
            return false;
        }

        private static void SwitchOff(MyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose) return;
            foreach (var block in grid.GetFatBlocks())
            {
                if (block is IMyFunctionalBlock functional && !KeepsRunning(block) && functional.Enabled)
                    functional.Enabled = false;
            }
        }

        private void MakeStatic(MyCubeGrid grid, long owner)
        {
            if (grid.IsStatic) return;
            var ownerName = PlayerUtils.GetPlayerIdentity(owner)?.DisplayName ?? "---";
            var message = $"Структура {grid.DisplayName} игрока {ownerName} конвертирована в статичную по причине перелимита PCU";
            Log.Warn($"PCU over the limit on {grid.DisplayName} of {ownerName}: made static");
            ConvertToStatic(grid);
            foreach (var player in PlayerUtils.GetAllPlayersInRadius(grid.PositionComp.GetPosition(), (float)NotifyRadius))
            {
                ChatUtils.SendTo(player.IdentityId, message);
                MyVisualScriptLogicProvider.ShowNotification(message, 5000, "Red", player.IdentityId);
            }
        }

        /// <summary>Stops the grid and makes it static on the server and the clients. The game thread.</summary>
        public static bool ConvertToStatic(MyCubeGrid grid)
        {
            try
            {
                grid.Physics?.SetSpeeds(Vector3.Zero, Vector3.Zero);
                grid.ConvertToStatic();
                MyMultiplayer.RaiseEvent(grid, x => x.ConvertToStatic);
                return grid.IsStatic;
            }
            catch (Exception e)
            {
                Log.Error(e, "Converting " + grid.DisplayName + " to static failed");
                return false;
            }
        }

        /// <summary>Forgets the strikes of the groups that were not seen in the whole pass (gone, or back in the limit).</summary>
        private void ForgetGone()
        {
            if (_strikes.Count == 0) return;
            var gone = new List<long>();
            foreach (var id in _strikes.Keys)
                if (!_seen.Contains(id)) gone.Add(id);
            foreach (var id in gone) _strikes.Remove(id);
        }

        /// <summary>Blocks left on over the limit: power, survival, the jump drive, the projector, connectors, medical rooms, safe zones.</summary>
        public static bool KeepsRunning(object block) =>
            block is MyReactor || block is MySurvivalKit || block is MyBatteryBlock || block is MyJumpDrive ||
            block is MyProjectorBase || block is MyShipConnector || block is MyMedicalRoom || block is MySafeZoneBlock;

        public static void SendLimitMessage(long identityId, int pcu, int maxPcu, string gridName)
        {
            if (identityId == 0) return;
            ChatUtils.SendTo(identityId, "Для структуры " + gridName + " достигнут лимит PCU!");
            ChatUtils.SendTo(identityId, "Использовано " + pcu + " PCU из возможных " + maxPcu);
            MyVisualScriptLogicProvider.ShowNotification("Достигнут лимит PCU!", 10000, "Red", identityId);
            MyVisualScriptLogicProvider.ShowNotification("Использовано " + pcu + " PCU из возможных " + maxPcu, 10000, "Red", identityId);
        }
    }
}
