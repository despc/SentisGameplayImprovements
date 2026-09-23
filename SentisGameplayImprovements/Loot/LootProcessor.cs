using System;
using System.Collections.Generic;
using NLog;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders.Private;
using VRage.Utils;
using VRageMath;

namespace SentisGameplayImprovements.Loot;

/// <summary>
/// Components knocked out of blocks by damage, dropped as floating objects where the blocks were.
///
/// The damage handler counts what each hit knocks out and adds it to the grid's pile, at the
/// place of the blocks it came from; the background loop takes the piles every so often and has
/// them dropped there, each stack in a free spot a few metres from it. Everything that touches
/// the world - finding a free spot, spawning - happens on the game thread, a few frames apart.
/// </summary>
public static class LootProcessor
{
    public static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // spheres around the place of the lost blocks, nearest first, and the spots tried on each
    private static readonly double[] DropRadii = { 3, 6, 10, 15, 25 };
    private const int TriesPerRadius = 5;
    private const int FramesBetweenDrops = 10;

    private sealed class Pile
    {
        // where the blocks were, weighted by what each lost
        public Vector3D Sum;
        public double Weight;
        public Vector3D Position => Weight > 0 ? Sum / Weight : Sum;
        public readonly Dictionary<MyDefinitionId, int> Components = new Dictionary<MyDefinitionId, int>();
    }

    /// <summary>
    /// Set by an explosion while it asks the damage handlers about a block itself: the game then
    /// asks them again from the block's own DoDamage, and loot counted both times came out double.
    /// The explosion counts the loot of a block it removes without DoDamage by itself. Game thread.
    /// </summary>
    internal static bool Suppressed;

    // grid id -> what it has lost since the last drop. Filled on the game thread by the damage
    // handler, taken whole by the background loop: one lock, and the loop swaps the dictionary out.
    private static readonly object PilesLock = new object();
    private static Dictionary<long, Pile> _piles = new Dictionary<long, Pile>();

    public static void CalculateLoot(object target, MyDamageInformation info)
    {
        if (!SentisGameplayImprovementsPlugin.Config.LootSystemEnabled || Suppressed) return;
        if (!(target is MySlimBlock block)) return;
        if (info.Type == MyDamageType.Grind || info.Type == MyDamageType.Deformation) return;
        if (info.Amount <= 0) return;

        try
        {
            var definition = block.BlockDefinition;
            var components = definition?.Components;
            if (components == null || components.Length == 0) return;

            // the damage as the block will really take it (MySlimBlock.DoDamage)
            var damage = info.Amount * block.BlockGeneralDamageModifier * definition.GeneralDamageMultiplier * block.DamageRatio;
            var stacks = new LootMath.Stack[components.Length];
            for (var i = 0; i < components.Length; i++)
            {
                var stack = block.ComponentStack.GetComponentStackInfo(i);
                stacks[i] = new LootMath.Stack(stack.Integrity, stack.MountedCount, stack.TotalCount, stack.MaxIntegrity);
            }

            var lost = LootMath.Lost(stacks, damage);
            var lostTotal = 0;
            foreach (var count in lost) lostTotal += count;
            if (lostTotal == 0) return;
            Pile pile = null;
            for (var i = 0; i < lost.Length; i++)
            {
                if (lost[i] <= 0) continue;
                if (pile == null)
                {
                    var grid = block.CubeGrid;
                    lock (PilesLock)
                    {
                        if (!_piles.TryGetValue(grid.EntityId, out pile))
                        {
                            pile = new Pile();
                            _piles[grid.EntityId] = pile;
                        }
                        block.ComputeWorldCenter(out var at);
                        pile.Sum += at * lostTotal;
                        pile.Weight += lostTotal;
                    }
                }

                var id = block.ComponentStack.GetComponentStackInfo(i).DefinitionId;
                lock (PilesLock)
                {
                    pile.Components.TryGetValue(id, out var count);
                    pile.Components[id] = count + lost[i];
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Loot count failed");
        }
    }

    /// <summary>Everything piled up since the last call. Background loop.</summary>
    public static void DropPiles()
    {
        Dictionary<long, Pile> piles;
        lock (PilesLock)
        {
            if (_piles.Count == 0) return;
            piles = _piles;
            _piles = new Dictionary<long, Pile>();
        }

        var session = MySession.Static;
        if (session == null) return;
        var frame = session.GameplayFrameCounter;
        var index = 0;
        foreach (var pile in piles.Values)
        {
            foreach (var pair in pile.Components)
            {
                var item = Item(pair.Key, pair.Value);
                if (item == null) continue;
                var at = pile.Position;
                MyAPIGateway.Utilities.InvokeOnGameThread(() => Drop(item.Value, at),
                    StartAt: frame + FramesBetweenDrops * ++index);
            }
        }
    }

    private static MyPhysicalInventoryItem? Item(MyDefinitionId id, int lost)
    {
        var definition = MyDefinitionManager.Static.GetComponentDefinition(id);
        if (definition == null) return null;
        var amount = LootMath.Dropped(lost, definition.DropProbability);
        if (amount < 1) return null;
        if (!(MyObjectBuilderSerializerKeen.CreateNewObject(id.TypeId, id.SubtypeName) is MyObjectBuilder_PhysicalObject content))
            return null;
        return new MyPhysicalInventoryItem(amount, content);
    }

    /// <summary>
    /// A free spot near where the blocks were - the nearest sphere around it that has one - and
    /// the item dropped there. The old spot was anywhere on a sphere of 75 m around the middle of
    /// the grid, which looked like nothing had fallen out at all. Game thread.
    /// </summary>
    private static void Drop(MyPhysicalInventoryItem item, Vector3D around)
    {
        try
        {
            var found = new List<MyEntity>();
            foreach (var radius in DropRadii)
            {
                var sphere = new BoundingSphereD(around, radius);
                for (var i = 0; i < TriesPerRadius; i++)
                {
                    var at = MyUtils.GetRandomBorderPosition(ref sphere);
                    var spot = new BoundingSphereD(at, 0.3);
                    found.Clear();
                    MyGamePruningStructure.GetAllEntitiesInSphere(ref spot, found);
                    if (!Free(found, at)) continue;
                    MyFloatingObjects.Spawn(item, at, Vector3D.Forward, Vector3D.Up);
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Loot drop failed");
        }
    }

    /// <summary>
    /// Nothing solid at the point: no block of a grid in that cell (a grid's box is mostly air
    /// around a wreck), no rock, no other floating object or character.
    /// </summary>
    private static bool Free(List<MyEntity> found, Vector3D at)
    {
        foreach (var entity in found)
        {
            switch (entity)
            {
                case MyCubeGrid grid:
                    if (grid.GetCubeBlock(grid.WorldToGridInteger(at)) != null) return false;
                    break;
                case MyVoxelBase voxel:
                    if (voxel.IsAnyOfPointInside(new[] { at })) return false;
                    break;
                case MyFloatingObject _:
                case MyCharacter _:
                    return false;
            }
        }
        return true;
    }
}
