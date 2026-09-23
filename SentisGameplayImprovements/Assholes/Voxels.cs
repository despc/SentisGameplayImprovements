using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using VRageMath;

namespace SentisGameplayImprovements.Assholes;

/// <summary>
/// A grid stuck in voxels - grinding against them, hundreds of contacts a second - is taken out: made
/// static where there is gravity, moved <see cref="TeleportDistance"/> away to a free place where there
/// is none (made static as well when no free place is found, or when it is a subgrid of something
/// static: moving it would move that too). Any grid: a player's, an NPC's, one in a safe zone.
///
/// The contacts are counted by DamagePatch (grid-voxel deformation at under 5 m/s) and taken once a
/// second; a grid counts as stuck after <see cref="StuckGridTracker.StuckSeconds"/> seconds in a row with
/// at least <see cref="StuckGridTracker.ContactsPerSecond"/> contacts. What is done to it is done on the
/// game thread.
/// </summary>
public class Voxels
{
    public static readonly Logger Log = LogManager.GetCurrentClassLogger();

    /// <summary>Metres from the place it was stuck to where a grid in space is moved.</summary>
    public const double TeleportDistance = 1000;

    /// <summary>Metres between the moved grid and anything else.</summary>
    public const double Clearance = 20;

    /// <summary>Random directions tried for a free place.</summary>
    public const int PlacementTries = 30;

    private static readonly StuckGridTracker Tracker = new StuckGridTracker();
    private static readonly Random Random = new Random();

    /// <summary>Takes the contacts of the last second (background thread) and deals with the stuck grids.</summary>
    public static void ProcessVoxelsContacts()
    {
        // the counts of the last second, taken whole: contacts counted meanwhile go into the new one
        var contacts = Interlocked.Exchange(ref DamagePatch.contactInfo,
            new ConcurrentDictionary<long, DamagePatch.GridVoxelContactInfo>());
        var counts = contacts.ToDictionary(c => c.Key, c => Interlocked.Read(ref c.Value.Count));
        foreach (var id in Tracker.Update(counts))
        {
            var grid = contacts[id].MyCubeGrid;
            var count = counts[id];
            MyAPIGateway.Utilities.InvokeOnGameThread(() => Unstick(grid, count));
        }
    }

    /// <summary>Makes the stuck grid static, or moves it away from the voxels (game thread).</summary>
    public static void Unstick(MyCubeGrid grid, long contacts)
    {
        try
        {
            if (grid == null || grid.MarkedForClose || grid.Closed || grid.Physics == null || grid.IsStatic) return;
            var position = grid.PositionComp.GetPosition();
            var identity = PlayerUtils.GetPlayerIdentity(PlayerUtils.GetOwner(grid));
            var playerName = identity == null ? "----" : identity.DisplayName;

            // In space the stuck grid's whole group would be moved - a base too, when the stuck grid is
            // a subgrid of something static; such a subgrid is made static instead.
            if (Vector3.IsZero(MyGravityProviderSystem.CalculateNaturalGravityInPoint(position)) && !OnStaticGroup(grid))
            {
                if (TryTeleport(grid, out var destination))
                {
                    Log.Warn($"Teleported stuck grid {grid.DisplayName} of player {playerName} ({contacts} voxel contacts a second) " +
                             $"from {position} to {destination}");
                    NotificationUtils.NotifyAllPlayersAround(position, 200,
                        $"Грид {grid.DisplayName} игрока {playerName} телепортирован на {TeleportDistance:0} м из-за излишней любви к вокселям");
                    if (identity != null)
                        ChatUtils.SendTo(identity.IdentityId,
                            $"Ваш грид {grid.DisplayName} застрял в вокселях и перенесён: GPS:{grid.DisplayName?.Replace(':', ' ')}:{destination.X:0}:{destination.Y:0}:{destination.Z:0}:");
                    return;
                }
                Log.Warn($"No free place {TeleportDistance:0} m from stuck grid {grid.DisplayName}; making it static");
            }

            Log.Warn($"Converted stuck grid {grid.DisplayName} of player {playerName} to static ({contacts} voxel contacts a second) at {position}");
            NotificationUtils.NotifyAllPlayersAround(position, 200,
                $"Грид {grid.DisplayName} игрока {playerName} конвертирован в статику из-за излишней любви к вокселям");
            // made static in place: the grid is not closed and made anew
            PcuLimiter.ConvertToStatic(grid);
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not unstick grid " + grid?.DisplayName);
        }
    }

    /// <summary>Whether a grid physically joined to this one (by rotor, piston, connector...) is static.</summary>
    private static bool OnStaticGroup(MyCubeGrid grid) =>
        MyCubeGridGroups.Static.Physical.GetGroup(grid)?.Nodes.Any(n => n.NodeData != grid && n.NodeData.IsStatic) == true;

    /// <summary>
    /// Moves the grid's whole physical group so that its centre is <see cref="TeleportDistance"/> from
    /// where it is, in a random direction where the group touches nothing; the group is stopped.
    /// </summary>
    private static bool TryTeleport(MyCubeGrid grid, out Vector3D destination)
    {
        var group = MyCubeGridGroups.Static.Physical.GetGroup(grid)?.Nodes.Select(n => n.NodeData).ToList()
                    ?? new List<MyCubeGrid> { grid };
        var box = BoundingBoxD.CreateInvalid();
        foreach (var member in group) box.Include(member.PositionComp.WorldAABB);
        var centre = box.Center;
        var radius = box.HalfExtents.Length() + Clearance;
        var ignore = new HashSet<long>(group.Select(g => g.EntityId));

        if (!TryPickDestination(centre, TeleportDistance, radius, Random,
                sphere => AsteroidFieldSpawner.IsFree(sphere, ignore), out destination))
            return false;

        var matrix = grid.WorldMatrix;
        matrix.Translation += destination - centre;
        grid.Teleport(matrix);
        foreach (var member in group)
        {
            if (member.Physics == null) continue;
            member.Physics.LinearVelocity = Vector3.Zero;
            member.Physics.AngularVelocity = Vector3.Zero;
        }
        return true;
    }

    /// <summary>
    /// A point <paramref name="distance"/> from <paramref name="centre"/>, in a random direction, where a
    /// sphere of <paramref name="radius"/> is free; false after <see cref="PlacementTries"/> directions.
    /// </summary>
    public static bool TryPickDestination(Vector3D centre, double distance, double radius, Random random,
        Func<BoundingSphereD, bool> isFree, out Vector3D destination)
    {
        for (var i = 0; i < PlacementTries; i++)
        {
            // uniform on the sphere
            var z = random.NextDouble() * 2 - 1;
            var angle = random.NextDouble() * Math.PI * 2;
            var r = Math.Sqrt(1 - z * z);
            destination = centre + new Vector3D(r * Math.Cos(angle), r * Math.Sin(angle), z) * distance;
            if (isFree(new BoundingSphereD(destination, radius))) return true;
        }
        destination = default;
        return false;
    }
}

/// <summary>
/// Which grids are stuck: those with at least <see cref="ContactsPerSecond"/> voxel contacts in each of
/// <see cref="StuckSeconds"/> seconds in a row. A second with fewer contacts starts the count over.
/// </summary>
public class StuckGridTracker
{
    public const long ContactsPerSecond = 500;
    public const int StuckSeconds = 5;

    private readonly Dictionary<long, int> _streaks = new Dictionary<long, int>();

    /// <summary>Seconds in a row the grid has been grinding, 0 when it is not.</summary>
    public int Streak(long gridId) => _streaks.TryGetValue(gridId, out var s) ? s : 0;

    /// <summary>
    /// Takes the contact counts of one second; returns the grids found stuck now, whose count starts
    /// over. Grids not in the counts, or under the threshold, start over too.
    /// </summary>
    public List<long> Update(IReadOnlyDictionary<long, long> contactsThisSecond)
    {
        var stuck = new List<long>();
        foreach (var id in _streaks.Keys.Where(id => !Heavy(contactsThisSecond, id)).ToList())
            _streaks.Remove(id);
        foreach (var pair in contactsThisSecond)
        {
            if (pair.Value < ContactsPerSecond) continue;
            var streak = Streak(pair.Key) + 1;
            if (streak >= StuckSeconds)
            {
                stuck.Add(pair.Key);
                _streaks.Remove(pair.Key);
            }
            else
            {
                _streaks[pair.Key] = streak;
            }
        }
        return stuck;
    }

    private static bool Heavy(IReadOnlyDictionary<long, long> counts, long id) =>
        counts.TryGetValue(id, out var count) && count >= ContactsPerSecond;
}
