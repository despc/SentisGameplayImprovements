using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using SentisGameplayImprovements.AllGridsActions;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace SentisGameplayImprovements.BackgroundActions
{
    /// <summary>
    /// Brings back grids that went through the ground of a planet.
    ///
    /// About twice a second, on the game thread and spread over frames (<see cref="GroupsPerSlice"/>
    /// groups per call, a call every 100 ms), every dynamic grid group near a planet is checked
    /// against the planet's voxel storage (edits included, so tunnels and dug pits are air): the
    /// centre of the group's biggest grid more than <see cref="DeepM"/> inside solid rock is a
    /// fall-through - a dynamic grid can only get there by passing the surface. A moving group is
    /// restored at once (a fall inside the rock tears wheels off within a couple of seconds); a
    /// still one on the second check in a row - a vehicle that went through often hangs under the
    /// ground on its suspension with the wheels caught on the surface, not moving at all.
    ///
    /// While a group is out of the rock its pose is remembered every check. A fallen group is put
    /// back to that pose - the whole physical group (chassis, wheels, rotor parts) moved and turned
    /// as one rigid body, so a tumble ends the way the vehicle stood a second before - lifted so
    /// its bounding box clears the real ground under it by <see cref="ClearanceM"/>, and stopped. A
    /// group that falls again <see cref="MaxRestores"/> times within <see cref="RestoreWindowSec"/>
    /// seconds is given up on and logged.
    /// </summary>
    public class FallInVoxelDetector
    {
        public const double DeepM = 1.5;
        public const double ClearanceM = 1.0;
        public const int MaxRestores = 3;
        public const double RestoreWindowSec = 60;
        // Ground search along the local vertical, from this far over the generated surface down.
        private const double GroundSearchM = 100.0;
        private const double GroundStepM = 0.5;
        // A remembered pose older than this is not used; the group is lifted where it is instead.
        private const double PoseMaxAgeSec = 30;
        public const int GroupsPerSlice = 25;
        private const double RebuildSec = 0.5;
        private const double MovingMps = 2.0;
        // A group this far over the generated surface is not near any rock; no voxel read needed.
        private const double SurelyAboveM = 30;

        private sealed class SafePose
        {
            public MatrixD Matrix;
            public DateTime Time;
        }

        private readonly Dictionary<long, SafePose> _poses = new Dictionary<long, SafePose>();
        private readonly Dictionary<long, List<DateTime>> _restores = new Dictionary<long, List<DateTime>>();
        private readonly HashSet<long> _givenUp = new HashSet<long>();
        // Groups found under the ground on the last check; restored when found there again.
        private readonly HashSet<long> _underLastCheck = new HashSet<long>();
        private readonly MyStorageData _probe = new MyStorageData(MyStorageDataTypeFlags.Content);
        private DateTime _lastCleanup = DateTime.UtcNow;
        private readonly List<MyCubeGrid> _queue = new List<MyCubeGrid>();
        private int _cursor;
        private DateTime _lastRebuild = DateTime.MinValue;

        public int RestoredCount { get; private set; }

        // Cost of the checks, logged once a minute with the cleanup.
        private double _checkMsMax, _checkMsSum;
        private int _checks, _groupsChecked;
        private double _rebuildMsMax;
        private int _queueSize;

        /// <summary>
        /// Checks the next <see cref="GroupsPerSlice"/> grid groups; once all are done and a second
        /// has passed, collects the groups again. Game thread, called every 100 ms.
        /// </summary>
        public void CheckSlice()
        {
            var started = Stopwatch.GetTimestamp();
            if (_cursor >= _queue.Count)
            {
                if ((DateTime.UtcNow - _lastRebuild).TotalSeconds < RebuildSec) return;
                var rebuildStarted = Stopwatch.GetTimestamp();
                Rebuild();
                _rebuildMsMax = Math.Max(_rebuildMsMax, (Stopwatch.GetTimestamp() - rebuildStarted) * 1000.0 / Stopwatch.Frequency);
                _queueSize = _queue.Count;
            }
            var end = Math.Min(_queue.Count, _cursor + GroupsPerSlice);
            for (; _cursor < end; _cursor++) CheckGroup(_queue[_cursor]);
            var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _checkMsMax = Math.Max(_checkMsMax, ms);
            _checkMsSum += ms;
            _checks++;
            if ((DateTime.UtcNow - _lastCleanup).TotalSeconds > 60) Cleanup();
        }

        /// <summary>
        /// The roots of all dynamic grid groups, one per group, straight from the game's physical
        /// groups: no pass over every grid of the world and no allocation per grid.
        /// </summary>
        private void Rebuild()
        {
            _lastRebuild = DateTime.UtcNow;
            _queue.Clear();
            _cursor = 0;
            foreach (var group in MyCubeGridGroups.Static.Physical.Groups)
            {
                MyCubeGrid root = null;
                var dynamic = true;
                foreach (var node in group.Nodes)
                {
                    var grid = node.NodeData;
                    if (grid == null || grid.IsStatic || grid.Physics == null || grid.MarkedForClose || grid.IsPreview)
                    {
                        dynamic = false;
                        break;
                    }
                    if (root == null || grid.BlocksCount > root.BlocksCount ||
                        grid.BlocksCount == root.BlocksCount && grid.EntityId < root.EntityId)
                        root = grid;
                }
                if (dynamic && root != null) _queue.Add(root);
            }
        }

        private void CheckGroup(MyCubeGrid root)
        {
            if (root.MarkedForClose || root.Closed || root.Physics == null || root.IsStatic) return;
            var planet = MyGamePruningStructure.GetClosestPlanet(root.PositionComp.GetPosition());
            if (planet?.Storage == null) return;
            _groupsChecked++;

            if (!IsUnderGround(root, planet))
            {
                _underLastCheck.Remove(root.EntityId);
                _poses[root.EntityId] = new SafePose { Matrix = root.WorldMatrix, Time = DateTime.UtcNow };
                return;
            }
            // Falling: at once, before the fall tears the wheels off (at ~60 m/s they go in about a
            // second and a half). Still: on the second check in a row.
            var first = _underLastCheck.Add(root.EntityId);
            if (_givenUp.Contains(root.EntityId) || first && root.Physics.LinearVelocity.Length() < MovingMps) return;
            if (TooManyRestores(root))
            {
                _givenUp.Add(root.EntityId);
                SentisGameplayImprovementsPlugin.Log.Warn("Fall-through: giving up on grid " + root.DisplayName + " (" + root.EntityId +
                                                         "), it fell through " + MaxRestores + " times in " + RestoreWindowSec + "s");
                return;
            }
            _underLastCheck.Remove(root.EntityId);
            Restore(root, planet, "fell through the ground");
        }

        /// <summary>
        /// Restores a grid on request (the player command): only if its group is under the ground.
        /// Returns what happened. Game thread.
        /// </summary>
        public string RestoreOnRequest(MyCubeGrid grid)
        {
            var group = GroupOf(grid);
            if (group.Any(g => g.IsStatic)) return "Grid is static";
            var root = Root(group);
            var planet = MyGamePruningStructure.GetClosestPlanet(root.PositionComp.GetPosition());
            if (planet?.Storage == null) return "No planet near the grid";
            if (!IsUnderGround(root, planet)) return "Grid is not under the ground";
            Restore(root, planet, "restore requested");
            return "Grid restored";
        }

        private bool IsUnderGround(MyCubeGrid root, MyPlanet planet)
        {
            var centre = root.PositionComp.WorldAABB.Center;
            var planetCentre = planet.PositionComp.GetPosition();
            var generated = planet.GetClosestSurfacePointGlobal(ref centre);
            if ((centre - planetCentre).Length() - (generated - planetCentre).Length() > SurelyAboveM) return false;
            var up = Vector3D.Normalize(centre - planetCentre);
            return IsRock(planet, centre + up * DeepM);
        }

        private void Restore(MyCubeGrid root, MyPlanet planet, string reason)
        {
            var group = GroupOf(root);
            var planetCentre = planet.PositionComp.GetPosition();
            var from = root.PositionComp.GetPosition();
            var depth = HeightUnderGround(planet, root.PositionComp.WorldAABB.Center);

            // Target pose of the root: the last one out of the rock, or, without a fresh one, the
            // current pose (then only the lift below moves it).
            var target = root.WorldMatrix;
            if (_poses.TryGetValue(root.EntityId, out var pose) && (DateTime.UtcNow - pose.Time).TotalSeconds <= PoseMaxAgeSec)
                target = pose.Matrix;
            else
                pose = null;
            var move = MatrixD.Invert(root.WorldMatrix) * target;

            // Lift: the moved group's box must clear the real ground under the target by ClearanceM.
            var up = Vector3D.Normalize(target.Translation - planetCentre);
            var ground = Ground(planet, target.Translation, up);
            // Exact: the corners of each grid's own box in its moved pose (a world box of a long
            // chassis rolled over is tens of metres tall and would lift it that high).
            var lowest = double.MaxValue;
            foreach (var g in group)
            {
                var moved = g.WorldMatrix * move;
                foreach (var corner in g.PositionComp.LocalAABB.GetCorners())
                    lowest = Math.Min(lowest, Vector3D.Dot(Vector3D.Transform(corner, moved) - ground, up));
            }
            var lift = Math.Max(0, ClearanceM - lowest);
            if (lift > 0) move *= MatrixD.CreateTranslation(up * lift);

            MoveGroup(root, move);

            RestoredCount++;
            if (!_restores.TryGetValue(root.EntityId, out var times)) _restores[root.EntityId] = times = new List<DateTime>();
            times.Add(DateTime.UtcNow);
            SentisGameplayImprovementsPlugin.Log.Warn("Fall-through: restored grid " + root.DisplayName + " (" + root.EntityId + ", " +
                                                     group.Count + " grids, " + reason + "), centre " + depth.ToString("F1") +
                                                     " m under the ground, lifted " + lift.ToString("F1") + " m over the pose, moved " + Vector3D.Distance(from, root.PositionComp.GetPosition()).ToString("F0") +
                                                     " m, pose " + (pose == null ? "none (lifted in place)" : (DateTime.UtcNow - pose.Time).TotalSeconds.ToString("F0") + "s old"));
        }

        /// <summary>
        /// MyCubeGrid.Teleport for a full rigid transform: Teleport keeps the rotation of the grids
        /// and only moves them. Same steps: blocks told (OnTeleport), bodies out of the world
        /// (welded ones stay with their parent), all world matrices set - the group and everything
        /// joined to it by constraints from outside - and the bodies put back in one batch, so the
        /// wheel and rotor constraints never see one side of them alone. Velocities end at zero.
        /// </summary>
        private static void MoveGroup(MyCubeGrid root, MatrixD move)
        {
            var nodes = MyCubeGridGroups.Static.Physical.GetGroup(root)?.Nodes.Select(n => n.NodeData).ToList()
                        ?? new List<MyCubeGrid> { root };
            var entities = new Dictionary<MyCubeGrid, HashSet<IMyEntity>>();
            var linked = new HashSet<IMyEntity>();
            foreach (var grid in nodes)
            {
                var children = new HashSet<IMyEntity>();
                grid.Hierarchy.GetChildrenRecursive(children);
                foreach (var child in children)
                    (child as MyCubeBlock)?.OnTeleport();
            }
            foreach (var grid in nodes)
            {
                var set = new HashSet<IMyEntity> { grid };
                grid.Hierarchy.GetChildrenRecursive(set);
                entities[grid] = set;
            }
            var all = new HashSet<IMyEntity>(entities.Values.SelectMany(x => x));
            foreach (var entity in all)
            {
                if (!(entity.Physics is MyPhysicsBody body)) continue;
                foreach (var constraint in body.Constraints)
                {
                    var other = constraint.RigidBodyA.GetEntity(0u) == entity ? constraint.RigidBodyB.GetEntity(0u) : constraint.RigidBodyA.GetEntity(0u);
                    if (other != null && !all.Contains(other)) linked.Add(other);
                }
            }

            var wasDisabled = new HashSet<IMyEntity>();
            foreach (var entity in all.Concat(linked))
            {
                if (!(entity.Physics is MyPhysicsBody body) || body.IsWelded) continue;
                if (body.Enabled) body.Enabled = false;
                else wasDisabled.Add(entity);
            }
            foreach (var grid in nodes)
            {
                var m = grid.PositionComp.WorldMatrixRef * move;
                grid.PositionComp.SetWorldMatrix(ref m, null, false, true, true, true);
            }
            foreach (var entity in linked)
            {
                var m = entity.PositionComp.WorldMatrixRef * move;
                entity.PositionComp.SetWorldMatrix(ref m, null, false, true, true, true);
            }
            var reinsert = new List<MyPhysicsBody>();
            foreach (var entity in all.Concat(linked))
            {
                if (!(entity.Physics is MyPhysicsBody body) || body.IsWelded || wasDisabled.Contains(entity)) continue;
                body.LinearVelocity = Vector3.Zero;
                body.AngularVelocity = Vector3.Zero;
                reinsert.Add(body);
            }
            MyPhysics.ReinsertBodiesBatched(reinsert);
        }

        private double HeightUnderGround(MyPlanet planet, Vector3D point)
        {
            var planetCentre = planet.PositionComp.GetPosition();
            var up = Vector3D.Normalize(point - planetCentre);
            return Vector3D.Dot(Ground(planet, point, up) - point, up);
        }

        private bool TooManyRestores(MyCubeGrid root)
        {
            if (!_restores.TryGetValue(root.EntityId, out var times)) return false;
            times.RemoveAll(t => (DateTime.UtcNow - t).TotalSeconds > RestoreWindowSec);
            return times.Count >= MaxRestores;
        }

        private void Cleanup()
        {
            _lastCleanup = DateTime.UtcNow;
            if (_checks > 0)
                SentisGameplayImprovementsPlugin.Log.Info("Fall-through checks: " + _checks + " slices in the last minute, " +
                                                         (_groupsChecked / _checks) + " grid groups per slice, " + (_checkMsSum / _checks).ToString("F2") +
                                                         " ms average, " + _checkMsMax.ToString("F2") + " ms max (collecting " + _queueSize + " groups: " + _rebuildMsMax.ToString("F2") + " ms max), " + RestoredCount + " restored since start");
            _checkMsMax = _checkMsSum = _rebuildMsMax = 0;
            _checks = _groupsChecked = 0;
            var live = new HashSet<long>(EntitiesObserver.MyCubeGrids.Where(g => g != null && !g.Closed).Select(g => g.EntityId));
            foreach (var id in _poses.Keys.Where(id => !live.Contains(id)).ToList()) _poses.Remove(id);
            foreach (var id in _restores.Keys.Where(id => !live.Contains(id)).ToList()) _restores.Remove(id);
            _givenUp.RemoveWhere(id => !live.Contains(id));
            _underLastCheck.RemoveWhere(id => !live.Contains(id));
        }

        private static List<MyCubeGrid> GroupOf(MyCubeGrid grid)
        {
            var group = MyCubeGridGroups.Static.Physical.GetGroup(grid);
            return group == null ? new List<MyCubeGrid> { grid } : group.Nodes.Select(n => n.NodeData).ToList();
        }

        // The group's biggest grid; ties by entity id, so the same grid is the root every check.
        private static MyCubeGrid Root(List<MyCubeGrid> group) =>
            group.OrderByDescending(g => g.BlocksCount).ThenBy(g => g.EntityId).First();

        private bool IsRock(MyPlanet planet, Vector3D point)
        {
            var voxel = Vector3I.Floor(point - planet.PositionLeftBottomCorner) + planet.StorageMin;
            var size = planet.Storage.Size;
            if (voxel.X < 0 || voxel.Y < 0 || voxel.Z < 0 || voxel.X >= size.X || voxel.Y >= size.Y || voxel.Z >= size.Z) return false;
            _probe.Resize(Vector3I.One);
            planet.Storage.ReadRange(_probe, MyStorageDataTypeFlags.Content, 0, voxel, voxel);
            return _probe.Content(0) >= 128;
        }

        /// <summary>The first rock walking down the local vertical from high over the generated surface.</summary>
        private Vector3D Ground(MyPlanet planet, Vector3D point, Vector3D up)
        {
            var generated = planet.GetClosestSurfacePointGlobal(ref point);
            for (var h = GroundSearchM; h > -GroundSearchM; h -= GroundStepM)
                if (IsRock(planet, generated + up * h))
                    return generated + up * (h + GroundStepM);
            return generated;
        }
    }
}
