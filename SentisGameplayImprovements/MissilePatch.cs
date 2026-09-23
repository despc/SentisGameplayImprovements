using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using NAPI;
using NLog;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisGameplayImprovements.Explosions;
using SentisGameplayImprovements.Loot;
using SentisOptimisations;
using Torch.Managers.PatchManager;
using Torch.Managers.PatchManager.MSIL;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Components;
using VRage.Groups;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// Explosions as the server wants them (ExplosionTweaks): warheads, and every explosion the game
    /// makes through MyExplosion - missiles, crates of ammunition and explosives going off.
    ///
    /// What an explosion does to a grid is worked out as the game does it - blocks in the sphere,
    /// a ray from each back to the centre, the damage used up by what stands in the way - with the
    /// server's own numbers: its damage multipliers, the piles of explosives, the delay before a
    /// shot warhead goes off. The work happens on the game thread, all but the search for the
    /// blocks inside the sphere, which runs in the background.
    ///
    /// Explosions wait their turn in a queue a frame takes a few milliseconds of (<see cref="Pump"/>):
    /// a thousand warheads set off at once each still make their own explosion, over a second or so,
    /// instead of one frame of many seconds. An armed warhead a blast reaches goes off itself a
    /// couple of frames later (<see cref="SetOff"/>) - a chain reaction, not one big explosion.
    /// </summary>
    [PatchShim]
    public static class MissilePatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static AccessTools.FieldRef<MyCubeGrid, ConcurrentDictionary<Vector3I, MyCube>> _cubes;
        private static AccessTools.FieldRef<MyWarhead, bool> _isExploded;
        private static AccessTools.FieldRef<MyWarhead, bool> _marked;
        private static AccessTools.FieldRef<MyWarhead, MyWarheadDefinition> _warheadDefinition;
        private static AccessTools.FieldRef<MyWarhead, BoundingSphereD> _explosionFullSphere;
        private static Action<MyWarhead> _markForExplosion;
        private static Action<MyWarhead, int> _explodeDelayed;
        private static Action<float, Vector3D, MyVoxelBase, bool, bool> _cutOutVoxelMap;

        // warheads a hit has already set off, so a second hit does not queue them again
        private static readonly HashSet<long> ShotWarheads = new HashSet<long>();

        public static void Patch(PatchContext ctx) => PatchGuard.Run("MissilePatch", ctx, PatchImpl);

        internal static void PatchImpl(PatchContext ctx)
        {
            var any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _cubes = AccessTools.FieldRefAccess<MyCubeGrid, ConcurrentDictionary<Vector3I, MyCube>>("m_cubes");
            _isExploded = AccessTools.FieldRefAccess<MyWarhead, bool>("m_isExploded");
            _marked = AccessTools.FieldRefAccess<MyWarhead, bool>("m_marked");
            _warheadDefinition = AccessTools.FieldRefAccess<MyWarhead, MyWarheadDefinition>("m_warheadDefinition");
            _explosionFullSphere = AccessTools.FieldRefAccess<MyWarhead, BoundingSphereD>("m_explosionFullSphere");
            _markForExplosion = AccessTools.MethodDelegate<Action<MyWarhead>>(
                typeof(MyWarhead).GetMethod("MarkForExplosion", any) ?? throw new MissingMethodException("MyWarhead.MarkForExplosion"));
            _explodeDelayed = AccessTools.MethodDelegate<Action<MyWarhead, int>>(
                typeof(MyWarhead).GetMethod("ExplodeDelayed", any) ?? throw new MissingMethodException("MyWarhead.ExplodeDelayed"));

            var explosionType = typeof(MyVoxelBase).Assembly.GetType("Sandbox.Game.MyExplosion")
                                ?? throw new TypeLoadException("Sandbox.Game.MyExplosion");
            _cutOutVoxelMap = AccessTools.MethodDelegate<Action<float, Vector3D, MyVoxelBase, bool, bool>>(
                explosionType.GetMethod("CutOutVoxelMap", any) ?? throw new MissingMethodException("MyExplosion.CutOutVoxelMap"));

            ctx.GetPattern(typeof(MyWarhead).GetMethod(nameof(MyWarhead.Explode), BindingFlags.Instance | BindingFlags.Public))
                .Prefixes.Add(Method(nameof(MyWarheadExplodePatched)));
            ctx.GetPattern(typeof(MyWarhead).GetMethods(any).First(m => m.Name.Contains("DoDamage")))
                .Prefixes.Add(Method(nameof(MyWarheadDoDamagePatched)));
            ctx.GetPattern(typeof(MyWarhead).GetMethod(nameof(MyWarhead.OnDestroy), any))
                .Prefixes.Add(Method(nameof(MyWarheadOnDestroyPatched)));

            // There are two overloads; the one taking the list of entities and of safe zones.
            var applyVolumetric = explosionType.GetMethods(any).First(m => m.Name == "ApplyVolumetricExplosion"
                && m.GetParameters().Length == 3
                && m.GetParameters()[1].ParameterType == typeof(List<MyEntity>)
                && m.GetParameters()[2].ParameterType.IsGenericType
                && m.GetParameters()[2].ParameterType.GetGenericArguments()[0].Name == "MySafeZone");
            ctx.GetPattern(applyVolumetric).Prefixes.Add(Method(nameof(ApplyVolumetricExplosionPatched)));

            ctx.GetPattern(typeof(MyCockpit).GetMethod(nameof(MyCockpit.OnUnregisteredFromGridSystems), BindingFlags.Instance | BindingFlags.Public))
                .Transpilers.Add(Method(nameof(CockpitPilotTranspiler)));

            // borrowed from DePatch
            ctx.Prefix(typeof(MyExplosionInfo), "get_AffectVoxels", typeof(MissilePatch), nameof(AffectVoxelsPatch));
        }

        private static MethodInfo Method(string name) =>
            typeof(MissilePatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);

        private static bool AffectVoxelsPatch() => SentisGameplayImprovementsPlugin.Config.DamageVoxelsFromExplosions;

        // ------------------------------------------------------------------ warheads

        /// <summary>A destroyed warhead does not go off by itself: the hit that destroyed it set it off already.</summary>
        private static bool MyWarheadOnDestroyPatched() => !SentisGameplayImprovementsPlugin.Config.ExplosionTweaks;

        /// <summary>
        /// A hit on an armed warhead sets it off a moment later instead of doing damage. The
        /// moment is the server's (WarheadExplosionDelay), and a warhead already set off is not
        /// queued again by the next hit.
        /// </summary>
        private static bool MyWarheadDoDamagePatched(MyWarhead __instance, ref bool __result)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks) return true;
            try
            {
                if (!__instance.IsArmed) return true;
                __result = true;
                if (_marked(__instance) || !ShotWarheads.Add(__instance.EntityId)) return false;

                var maxDelay = SentisGameplayImprovementsPlugin.Config.WarheadExplosionDelay;
                var delay = maxDelay > 10 ? MyUtils.GetRandomInt(maxDelay / 2, maxDelay) : 10;
                var warhead = __instance;
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    ShotWarheads.Remove(warhead.EntityId);
                    try
                    {
                        if (warhead.MarkedForClose || warhead.Closed || _marked(warhead)) return;
                        _markForExplosion(warhead);
                        _explodeDelayed(warhead, 500);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "Setting off a warhead failed");
                    }
                }, StartAt: MySession.Static.GameplayFrameCounter + delay);
                return false;
            }
            catch (Exception e)
            {
                Log.Error(e, "Warhead hit failed");
                return true;
            }
        }

        /// <summary>
        /// A warhead goes off through the explosion queue: set off in the same frame as a thousand
        /// others, each still makes its own explosion, a few per frame (<see cref="Pump"/>).
        /// </summary>
        private static bool MyWarheadExplodePatched(MyWarhead __instance)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks) return true;
            SetOff(__instance, 0);
            return false;
        }

        /// <summary>
        /// Queues a warhead to go off, once however many times it is set off, after
        /// <paramref name="afterFrames"/> frames. Game thread.
        /// </summary>
        private static void SetOff(MyWarhead warhead, int afterFrames)
        {
            if (warhead == null || _isExploded(warhead) || !PendingWarheads.Add(warhead.EntityId)) return;
            if (afterFrames <= 0)
            {
                Enqueue(() => ExplodeNow(warhead));
                return;
            }
            MyAPIGateway.Utilities.InvokeOnGameThread(() => Enqueue(() => ExplodeNow(warhead)),
                StartAt: MySession.Static.GameplayFrameCounter + afterFrames);
        }

        private static void ExplodeNow(MyWarhead warhead)
        {
            PendingWarheads.Remove(warhead.EntityId);
            try
            {
                if (warhead.MarkedForClose || warhead.Closed || _isExploded(warhead) || !MySession.Static.WeaponsEnabled ||
                    warhead.CubeGrid.Physics == null)
                    return;
                // as the game: a warhead in a zone that forbids damage does not go off
                if (!MySessionComponentSafeZones.IsActionAllowed(warhead.WorldMatrix.Translation, MySafeZoneAction.Damage, 0L, 0uL))
                    return;

                _isExploded(warhead) = true;
                if (!_marked(warhead)) _markForExplosion(warhead);
                var sphere = _explosionFullSphere(warhead);
                var definition = _warheadDefinition(warhead);

                // The effect for the players. It comes back to ApplyVolumetricExplosion with no damage,
                // which leaves it alone (see there): the blast is done below.
                var explosionInfo = new MyExplosionInfo
                {
                    PlayerDamage = 0.0f,
                    Damage = 0,
                    ExplosionType = sphere.Radius > 6.0
                        ? sphere.Radius > 20.0
                            ? sphere.Radius > 40.0 ? MyExplosionTypeEnum.WARHEAD_EXPLOSION_50 : MyExplosionTypeEnum.WARHEAD_EXPLOSION_30
                            : MyExplosionTypeEnum.WARHEAD_EXPLOSION_15
                        : MyExplosionTypeEnum.WARHEAD_EXPLOSION_02,
                    ExplosionSphere = sphere,
                    LifespanMiliseconds = 700,
                    HitEntity = warhead,
                    ParticleScale = 1f,
                    OwnerEntity = warhead.CubeGrid,
                    Direction = (Vector3)warhead.WorldMatrix.Forward,
                    VoxelExplosionCenter = sphere.Center,
                    ExplosionFlags = MyExplosionFlags.CREATE_DEBRIS | MyExplosionFlags.AFFECT_VOXELS |
                                     MyExplosionFlags.CREATE_DECALS | MyExplosionFlags.CREATE_PARTICLE_EFFECT |
                                     MyExplosionFlags.CREATE_SHRAPNELS | MyExplosionFlags.APPLY_DEFORMATION,
                    VoxelCutoutScale = 1f,
                    PlaySound = true,
                    ApplyForceAndDamage = true,
                    ObjectsRemoveDelayInMiliseconds = 40,
                    Velocity = warhead.CubeGrid.Physics.LinearVelocity,
                };
                var owner = warhead.OwnerId;
                MyExplosions.AddExplosion(ref explosionInfo);

                var entities = MyEntities.GetTopMostEntitiesInSphere(ref sphere);
                var inSphere = new List<MyEntity>(entities);
                entities.Clear();
                Explode(new Blast
                {
                    Sphere = sphere,
                    Damage = definition.WarheadExplosionDamage * SentisGameplayImprovementsPlugin.Config.WarheadDamageMultiplier,
                    Origin = warhead.EntityId,
                    Attacker = owner,
                    IsWarhead = true,
                }, inSphere);

                warhead.CubeGrid.RemoveDestroyedBlock(warhead.SlimBlock, owner);
                MyDamageSystem.Static.RaiseDestroyed(warhead.SlimBlock,
                    new MyDamageInformation(false, 999999, MyDamageType.Explosion, owner));
            }
            catch (Exception e)
            {
                Log.Error(e, "Warhead explosion failed");
            }
        }

        // ------------------------------------------------------------------ pacing

        /// <summary>
        /// What one frame may spend on explosions. A thousand warheads going off at once used to be
        /// done in one frame - fourteen seconds of it. Now each explosion, and each explosion's
        /// damage, waits its turn: a frame does what fits in the budget, always at least one, and
        /// leaves the rest to the next.
        /// </summary>
        private const double FrameBudgetMs = 4;

        // how long after a blast reaches an armed warhead that one goes off
        private const int ChainMinFrames = 2, ChainMaxFrames = 4;

        private static readonly ConcurrentQueue<Action> Work = new ConcurrentQueue<Action>();
        private static int _pumpScheduled;

        // warheads queued to go off, so that a hundred blasts reaching one set it off once
        private static readonly HashSet<long> PendingWarheads = new HashSet<long>();

        /// <summary>Queues explosion work for the game thread. Any thread.</summary>
        private static void Enqueue(Action work)
        {
            Work.Enqueue(work);
            SchedulePump();
        }

        private static void SchedulePump()
        {
            if (Interlocked.CompareExchange(ref _pumpScheduled, 1, 0) != 0) return;
            var session = MySession.Static;
            MyAPIGateway.Utilities.InvokeOnGameThread(Pump, StartAt: session == null ? 0 : session.GameplayFrameCounter + 1);
        }

        private static void Pump()
        {
            Interlocked.Exchange(ref _pumpScheduled, 0);
            var started = Stopwatch.GetTimestamp();
            var budget = (long)(Stopwatch.Frequency * FrameBudgetMs / 1000);
            var done = 0;
            while ((done == 0 || Stopwatch.GetTimestamp() - started < budget) && Work.TryDequeue(out var work))
            {
                done++;
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    Log.Error(e, "Explosion work failed");
                }
            }
            if (!Work.IsEmpty) SchedulePump();
        }

        // ------------------------------------------------------------------ the game's explosions

        /// <summary>
        /// Every explosion the game makes itself. Characters sitting in a cockpit are left to the
        /// cockpit; a pile of explosives or a crate of ammunition blows with the server's numbers.
        /// The game has already taken out whatever sits in a safe zone.
        /// </summary>
        private static bool ApplyVolumetricExplosionPatched(ref MyExplosionInfo m_explosionInfo, List<MyEntity> entities)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks) return true;
            // the effect of a warhead gone off through ExplodeNow, which does the blast itself
            if (m_explosionInfo.HitEntity is MyWarhead own && m_explosionInfo.Damage <= 0f && _isExploded(own)) return false;
            try
            {
                var config = SentisGameplayImprovementsPlugin.Config;
                var inSphere = new List<MyEntity>(entities.Count);
                var seen = new HashSet<MyEntity>();
                foreach (var entity in entities)
                {
                    if (entity is MyCharacter { UsingEntity: MyCockpit }) continue;
                    if (seen.Add(entity)) inSphere.Add(entity);
                }

                var sphere = m_explosionInfo.ExplosionSphere;
                var damage = m_explosionInfo.Damage;
                if (m_explosionInfo.OwnerEntity is MyFloatingObject crate)
                {
                    var count = crate.Amount.Value.ToIntSafe();
                    var definition = crate.ItemDefinition;
                    if (crate.Item.Content?.SubtypeName == "Explosives")
                    {
                        damage = config.ExplosivesDamage * count;
                        sphere.Radius = 15;
                    }
                    else if (definition is MyAmmoMagazineDefinition magazine)
                    {
                        var ammo = MyDefinitionManager.Static.GetAmmoDefinition(magazine.AmmoDefinitionId);
                        if (ammo is MyProjectileAmmoDefinition projectile)
                        {
                            damage = ExplosionMath.AmmoDamage(projectile.ProjectileMassDamage, 30, config.ProjectileAmmoExplosionMultiplier, count);
                            sphere.Radius = config.AmmoExplosionRadius;
                        }
                        else if (ammo is MyMissileAmmoDefinition missile)
                        {
                            damage = ExplosionMath.AmmoDamage(missile.MissileExplosionDamage, 5000, config.MissileAmmoExplosionMultiplier, count);
                            sphere.Radius = config.AmmoExplosionRadius;
                        }
                    }
                }

                var owner = m_explosionInfo.OwnerEntity;
                Explode(new Blast
                {
                    Sphere = sphere,
                    Damage = damage,
                    Origin = m_explosionInfo.OriginEntity,
                    Excluded = m_explosionInfo.ExcludedEntity,
                    Attacker = owner?.EntityId ?? 0,
                }, inSphere);
            }
            catch (Exception e)
            {
                Log.Error(e, "Explosion failed");
            }
            // the game's own volumetric explosion does not run: this one replaces it
            return false;
        }

        // ------------------------------------------------------------------ one explosion

        private sealed class Blast
        {
            public BoundingSphereD Sphere;
            public float Damage;
            public long Origin;     // what fired it: its grid is spared unless friendly fire is on
            public MyEntity Excluded;   // spared whatever (the game's ExcludedEntity)
            public long Attacker;
            public bool IsWarhead;  // a warhead spares nobody
            public bool Piercing;
        }

        /// <summary>
        /// What one grid of the explosion is: the blocks it hits. The game also marks the shell of
        /// blocks just outside the blast for a new physics shape; nothing about them changes, and
        /// rebuilding their shape was most of what a grid spent after an explosion. The neighbours
        /// of the blocks the blast does damage are marked (<see cref="ApplyVolumetricDamageToGrid"/>).
        /// </summary>
        private sealed class GridPart
        {
            public MyCubeGrid Grid;
            public readonly List<MySlimBlock> Hit = new List<MySlimBlock>();   // each block once: see CollectBlocks
        }

        private static void Explode(Blast blast, List<MyEntity> entities)
        {
            // A warhead's own explosion effect comes back here with no damage: its grids were done already.
            var grids = blast.Damage > 0f ? GridsToHit(blast, entities) : new List<GridPart>();
            // Only the search for blocks goes off the game thread: it reads the grid's cell map,
            // which is a concurrent dictionary. Everything else touches the world.
            Parallel.StartBackground(() =>
            {
                try
                {
                    foreach (var part in grids) CollectBlocks(blast.Sphere, part);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Collecting the blocks of an explosion failed");
                    return;
                }
                Enqueue(() => Finish(blast, entities, grids));
            });
        }

        /// <summary>The grids the explosion reaches, the one that fired it excluded unless friendly fire is on. Game thread.</summary>
        private static List<GridPart> GridsToHit(Blast blast, List<MyEntity> entities)
        {
            MyCubeGrid firedFrom = null;
            MyGroups<MyCubeGrid, MyGridLogicalGroupData>.Group firedFromGroup = null;
            if (!blast.IsWarhead && !MySession.Static.Settings.EnableTurretsFriendlyFire && blast.Origin != 0L &&
                MyEntities.GetEntityById(blast.Origin)?.GetTopMostParent() is MyCubeGrid origin)
            {
                firedFrom = origin;
                firedFromGroup = MyCubeGridGroups.Static.Logical.GetGroup(origin);
            }

            var parts = new List<GridPart>();
            foreach (var entity in entities)
            {
                if (!(entity is MyCubeGrid grid) || grid == blast.Excluded || grid.IsPreview || grid.MarkedForClose || grid.Physics == null) continue;
                if (!blast.IsWarhead)
                {
                    if (!grid.CreatePhysics || grid == firedFrom) continue;
                    if (firedFromGroup != null && MyCubeGridGroups.Static.Logical.GetGroup(grid) == firedFromGroup) continue;
                }
                parts.Add(new GridPart { Grid = grid });
            }
            return parts;
        }

        /// <summary>Blocks inside the explosion.</summary>
        private static void CollectBlocks(BoundingSphereD sphere, GridPart part)
        {
            var grid = part.Grid;
            var cubes = _cubes(grid);
            var inv = grid.PositionComp.WorldMatrixInvScaled;
            Vector3D.Transform(ref sphere.Center, ref inv, out var centre);
            var hitRadius = sphere.Radius;
            var outerRadius = sphere.Radius + grid.GridSize * 0.5 * Math.Sqrt(3.0);
            var hitSphere = new BoundingSphere(centre, (float)hitRadius);
            var halfSlim = new Vector3(grid.GridSize / 2f / 1.25f);
            var seenFat = new HashSet<MyCubeBlock>();

            void Classify(MySlimBlock block)
            {
                if (block.FatBlock != null && !seenFat.Add(block.FatBlock)) return;
                var box = block.FatBlock == null
                    ? new BoundingBox(block.Position * grid.GridSize - halfSlim, block.Position * grid.GridSize + halfSlim)
                    : new BoundingBox(block.Min * grid.GridSize - grid.GridSizeHalf, block.Max * grid.GridSize + grid.GridSizeHalf);
                if (box.Intersects(hitSphere)) part.Hit.Add(block);
            }

            var min = Vector3I.Round((centre - outerRadius) * grid.GridSizeR);
            var max = Vector3I.Round((centre + outerRadius) * grid.GridSizeR);
            var cells = (long)(max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
            if (cells < cubes.Count)
            {
                var key = new Vector3I();
                for (key.X = min.X; key.X <= max.X; key.X++)
                    for (key.Y = min.Y; key.Y <= max.Y; key.Y++)
                        for (key.Z = min.Z; key.Z <= max.Z; key.Z++)
                            if (cubes.TryGetValue(key, out var cube))
                                Classify(cube.CubeBlock);
            }
            else
            {
                foreach (var cube in cubes.Values) Classify(cube.CubeBlock);
            }
        }

        /// <summary>Damage, voxels, characters and shields - everything that changes the world. Game thread.</summary>
        private static void Finish(Blast blast, List<MyEntity> entities, List<GridPart> grids)
        {
            try
            {
                var zones = NoDamageZones(blast.Sphere);
                var shieldedGrid = DamageShieldAndObjects(blast, entities, zones);
                var explosion = new MyGridExplosion();
                explosion.Init(blast.Sphere, blast.Damage);
                foreach (var part in grids)
                {
                    if (part.Grid.MarkedForClose) continue;
                    explosion.AffectedCubeGrids.Add(part.Grid);
                    explosion.AffectedCubeBlocks.UnionWith(part.Hit);
                }

                ComputeDamagedBlocks(explosion, blast.Piercing, shieldedGrid, entities);
                ApplyExplosionOnVoxel(blast.Sphere);
                ApplyVolumetricDamageToGrid(explosion, blast.Attacker, zones);
            }
            catch (Exception e)
            {
                Log.Error(e, "Explosion damage failed");
            }
        }

        /// <summary>The safe zones around the explosion that forbid damage. The game leaves this to each explosion.</summary>
        private static List<MySafeZone> NoDamageZones(BoundingSphereD sphere)
        {
            var zones = MySessionComponentSafeZones.GetSafeZonesInAABB(sphere.GetBoundingBox(), new List<MySafeZone>());
            zones.RemoveAll(zone => !zone.Enabled || zone.AllowedActions.HasFlag(MySafeZoneAction.Damage));
            return zones;
        }

        private static bool InZone(List<MySafeZone> zones, Vector3D point)
        {
            for (var i = 0; i < zones.Count; i++)
                if (zones[i].Contains(point)) return true;
            return false;
        }

        private static void ApplyExplosionOnVoxel(BoundingSphereD sphere)
        {
            if (!MySession.Static.EnableVoxelDestruction || !MySession.Static.HighSimulationQuality) return;
            if (!SentisGameplayImprovementsPlugin.Config.DamageVoxelsFromExplosions) return;

            var overlapping = new List<MyVoxelBase>();
            MySession.Static.VoxelMaps.GetAllOverlappingWithSphere(ref sphere, overlapping);
            var roots = new HashSet<MyVoxelBase>();
            foreach (var voxel in overlapping) roots.Add(voxel.RootVoxel);
            var radius = (float)sphere.Radius * 0.3f;
            foreach (var voxel in roots)
            {
                // as the game: cut locally, and have the cut sent to the clients
                _cutOutVoxelMap(radius, sphere.Center, voxel, true, false);
                voxel.RequestVoxelCutoutSphere(sphere.Center, radius, true, false);
            }
        }

        /// <summary>
        /// Characters and floating objects in the blast, and the shield of the grid it hits.
        /// Returns the grid a shield took the hit for, or -1.
        /// </summary>
        private static long DamageShieldAndObjects(Blast blast, List<MyEntity> entities, List<MySafeZone> zones)
        {
            long shieldedGrid = -1;
            foreach (var entity in entities)
            {
                if (entity == null || entity.MarkedForClose) continue;
                if (InZone(zones, entity.PositionComp.WorldAABB.Center)) continue;

                if (entity is MyCharacter character)
                {
                    // A pilot is part of the cockpit, not in this list; a cockpit the blast destroys
                    // throws its pilot out alive (HurtPilotOfLostCockpit).
                    if (character.UsingEntity is MyCockpit) continue;
                    character.DoDamage(99999, MyDamageType.Explosion, true, attackerId: blast.Attacker);
                }
                else if (entity is MyFloatingObject floating)
                {
                    floating.DoDamage(99999, MyDamageType.Explosion, true, blast.Attacker, null);
                }
                else if (entity.DisplayName == "dShield")
                {
                    var api = SentisGameplayImprovementsPlugin.SApi;
                    var match = api?.MatchEntToShieldFastExt(entity, true);
                    if (match == null || !match.HasValue) continue;
                    var shield = match.Value.Item1;
                    var multiplier = SentisGameplayImprovementsPlugin.Config.WarheadDamageMultiplier;
                    var shieldHp = api.GetMaxHpCap(shield) * (api.GetShieldPercent(shield) / 100) * 10000;
                    shieldedGrid = shieldHp < blast.Damage * multiplier ? -1 : shield.CubeGrid.EntityId;
                    api.PointAttackShieldCon(shield, blast.Sphere.Center, blast.Attacker,
                        (float)(blast.Damage * (blast.Sphere.Radius / multiplier)), 0, false, true);
                }
            }
            return shieldedGrid;
        }

        // ------------------------------------------------------------------ damage

        /// <summary>
        /// The damage each block takes. As the game does it: a ray from every block in the sphere
        /// back to the centre, the damage used up by whatever stands in the way - and what a ray
        /// found about a block is kept, so the rays of the blocks behind it stop there. The blast
        /// goes deeper only through a block it destroys (<see cref="ExplosionMath.PassThrough"/>).
        ///
        /// The plugin used to start every block from scratch - a new record for every one of them,
        /// so every ray was walked, and every physics ray it needed cast, all the way to the centre
        /// again - and gave a block whose centre lies outside the sphere all the damage left instead
        /// of none.
        /// </summary>
        public static void ComputeDamagedBlocks(MyGridExplosion explosion, bool piercing, long shieldedGrid = -1, List<MyEntity> around = null)
        {
            if (piercing)
            {
                foreach (var block in explosion.AffectedCubeBlocks)
                    explosion.DamagedBlocks[block] = explosion.Damage;
                return;
            }

            var ray = new Ray(explosion, around);
            var radius = (float)explosion.Sphere.Radius;
            foreach (var affected in explosion.AffectedCubeBlocks)
            {
                if (affected.CubeGrid.EntityId == shieldedGrid) continue;
                ray.Cast.Clear();
                ray.InCast.Clear();
                var info = ray.CastDDA(affected);
                while (ray.Cast.Count > 0)
                {
                    var block = ray.Cast.Pop();
                    ray.InCast.Remove(block);
                    if (block.FatBlock is MyWarhead)
                    {
                        explosion.DamagedBlocks[block] = 1E+07f;
                        continue;
                    }

                    block.ComputeWorldCenter(out var blockCentre);   // the centre of its box, as WorldAABB.Center, for less
                    var distance = (float)(blockCentre - explosion.Sphere.Center).Length();
                    if (info.DamageRemaining > 0f)
                    {
                        var share = ExplosionMath.Falloff(distance, info.DistanceToExplosion, radius);
                        info.DamageRemaining = ExplosionMath.PassThrough(info.DamageRemaining, share,
                            block.BlockDefinition.GeneralDamageMultiplier, DamageToDestroy(block), out var dealt);
                        if (dealt > 0f) explosion.DamagedBlocks[block] = dealt;
                    }
                    else
                    {
                        info.DamageRemaining = 0f;
                    }
                    info.DistanceToExplosion = Math.Abs(distance);
                    ray.Remaining[block] = info;
                }
            }
        }

        /// <summary>
        /// The damage, in the units <see cref="ApplyVolumetricDamageToGrid"/> is given, that destroys
        /// the block - worked out the way it applies it. An armor block goes at once when the damage
        /// is over its integrity by its deformation ratio, or else takes it through DoDamage; any
        /// other block takes seven times the damage through DoDamage. DoDamage multiplies by the
        /// block's and the grid's damage modifiers and by how unfinished the block is.
        /// </summary>
        private static float DamageToDestroy(MySlimBlock block)
        {
            var grid = block.CubeGrid;
            var root = MyGridPhysicalHierarchy.Static.GetRoot(grid) ?? grid;
            var modifiers = block.BlockGeneralDamageModifier * Math.Min(grid.GridGeneralDamageModifier, root.GridGeneralDamageModifier)
                            * block.BlockDefinition.GeneralDamageMultiplier * block.DamageRatio;
            if (modifiers <= 0f) return float.MaxValue;   // cannot be damaged at all
            var throughDoDamage = block.Integrity / modifiers;
            if (block.FatBlock != null) return throughDoDamage / 7f;
            var deformation = block.DeformationRatio;
            return deformation > 0f ? Math.Min(block.Integrity / deformation, throughDoDamage) : throughDoDamage;
        }

        /// <summary>The game's grid ray (MyGridExplosion.CastDDA / CastPhysicsRay), with what it learns kept for the whole explosion.</summary>
        private sealed class Ray
        {
            private readonly MyGridExplosion _explosion;
            public readonly Dictionary<MySlimBlock, MyGridExplosion.MyRaycastDamageInfo> Remaining =
                new Dictionary<MySlimBlock, MyGridExplosion.MyRaycastDamageInfo>();
            public readonly Stack<MySlimBlock> Cast = new Stack<MySlimBlock>();
            public readonly HashSet<MySlimBlock> InCast = new HashSet<MySlimBlock>();

            public Ray(MyGridExplosion explosion, List<MyEntity> around)
            {
                _explosion = explosion;
                _around = around;
            }

            // What besides a grid itself could stand between its blocks and the explosion: every
            // body in the sphere, a planet only where its ground is within reach. Unknown (null):
            // ask Havok every time.
            private readonly List<MyEntity> _around;
            private readonly Dictionary<MyCubeGrid, List<BoundingBoxD>> _obstacles = new Dictionary<MyCubeGrid, List<BoundingBoxD>>();

            private List<BoundingBoxD> ObstaclesFor(MyCubeGrid grid)
            {
                if (_around == null) return null;
                if (_obstacles.TryGetValue(grid, out var boxes)) return boxes;
                boxes = new List<BoundingBoxD>();
                var sphere = _explosion.Sphere;
                foreach (var entity in _around)
                {
                    if (entity == null || entity == grid || entity.MarkedForClose || entity.Closed) continue;
                    if (entity is MyPlanet planet)
                    {
                        var centre = sphere.Center;
                        var ground = planet.GetClosestSurfacePointGlobal(ref centre);
                        var planetCentre = planet.PositionComp.GetPosition();
                        var underground = Vector3D.DistanceSquared(centre, planetCentre) <= Vector3D.DistanceSquared(ground, planetCentre);
                        if (!underground && Vector3D.Distance(centre, ground) > sphere.Radius) continue;
                    }
                    else if (!(entity is MyVoxelBase) && entity.Physics == null)
                    {
                        continue;   // nothing a ray could hit
                    }
                    else if (entity is MyCubeGrid other && other.BlocksCount == 0)
                    {
                        continue;
                    }
                    boxes.Add(entity.PositionComp.WorldAABB);
                }
                _obstacles[grid] = boxes;
                return boxes;
            }

            /// <summary>Whether anything but the grid itself may be on the way from a point to the explosion.</summary>
            private bool MaybeBlocked(MyCubeGrid grid, Vector3D from)
            {
                var boxes = ObstaclesFor(grid);
                if (boxes == null) return true;
                if (boxes.Count == 0) return false;
                var way = Centre - from;
                var length = way.Normalize();
                var ray = new RayD(from, way);
                foreach (var box in boxes)
                {
                    var at = box.Intersects(ray);
                    if (at.HasValue && at.Value <= length) return true;
                }
                return false;
            }

            private Vector3D Centre => _explosion.Sphere.Center;

            private void Push(MySlimBlock block)
            {
                if (InCast.Add(block)) Cast.Push(block);
            }

            private MyGridExplosion.MyRaycastDamageInfo Full(Vector3D from) =>
                new MyGridExplosion.MyRaycastDamageInfo(_explosion.Damage, (float)(from - Centre).Length());

            public MyGridExplosion.MyRaycastDamageInfo CastDDA(MySlimBlock block)
            {
                if (Remaining.TryGetValue(block, out var known)) return known;
                Push(block);
                block.ComputeWorldCenter(out var worldCentre);
                var grid = block.CubeGrid;
                // One list for every ray: a ray goes on into another (CastPhysicsRay -> CastDDA) only
                // as the last thing it does, when it no longer reads its own cells.
                _cells.Clear();
                grid.RayCastCells(worldCentre, Centre, _cells, null, false, true);
                var centreCell = CentreCell(grid);
                for (var i = 0; i < _cells.Count; i++)
                {
                    var cell = _cells[i];
                    var other = grid.GetCubeBlock(cell);
                    if (other == null)
                    {
                        var from = Vector3D.Transform(cell * grid.GridSize, grid.WorldMatrix);
                        if (cell == centreCell) return Full(from);
                        if (MaybeBlocked(grid, from)) return CastPhysicsRay(grid, cell, from);
                        return ThroughOwnAir(grid, block, i + 1, centreCell, from);
                    }
                    if (other != block)
                    {
                        if (Remaining.TryGetValue(other, out known)) return known;
                        Push(other);
                    }
                    else if (cell == centreCell)
                    {
                        return Full(Vector3D.Transform(cell * grid.GridSize, grid.WorldMatrix));
                    }
                }
                return Full(worldCentre);
            }

            /// <summary>
            /// The way on from an empty cell when nothing but the grid itself is near: what a
            /// physics ray would find is the grid's next block on the way, or nothing.
            /// </summary>
            private MyGridExplosion.MyRaycastDamageInfo ThroughOwnAir(MyCubeGrid grid, MySlimBlock block, int next, Vector3I centreCell, Vector3D from)
            {
                var distance = (float)(from - Centre).Length();
                for (var i = next; i < _cells.Count; i++)
                {
                    var cell = _cells[i];
                    var found = grid.GetCubeBlock(cell);
                    if (found != null && found != block)
                        return InCast.Contains(found) ? new MyGridExplosion.MyRaycastDamageInfo(0f, distance) : CastDDA(found);
                    if (cell == centreCell) break;
                }
                return new MyGridExplosion.MyRaycastDamageInfo(_explosion.Damage, distance);
            }

            private readonly List<Vector3I> _cells = new List<Vector3I>();
            private readonly Dictionary<MyCubeGrid, Vector3I> _centreCells = new Dictionary<MyCubeGrid, Vector3I>();

            private MyCubeGrid _lastGrid;
            private Vector3I _lastCentreCell;

            private Vector3I CentreCell(MyCubeGrid grid)
            {
                if (grid == _lastGrid) return _lastCentreCell;
                if (!_centreCells.TryGetValue(grid, out var cell))
                    _centreCells[grid] = cell = grid.WorldToGridInteger(Centre);
                _lastGrid = grid;
                return _lastCentreCell = cell;
            }

            /// <summary>
            /// Where a physics ray from an empty cell to the explosion ends: at a block, at nothing
            /// (the damage gets through whole) or at something else (it does not).
            /// </summary>
            private struct Trace
            {
                public MySlimBlock Block;
                public bool Clear;
                public float Distance;
            }

            // The rays of the blocks around an explosion meet in the few empty cells next to it, and
            // each one used to ask Havok again the way from there. Nothing moves while an explosion
            // is worked out: the way from a cell is asked once.
            private readonly Dictionary<(MyCubeGrid, Vector3I), Trace> _traces = new Dictionary<(MyCubeGrid, Vector3I), Trace>();

            private MyGridExplosion.MyRaycastDamageInfo CastPhysicsRay(MyCubeGrid grid, Vector3I cell, Vector3D from)
            {
                if (!_traces.TryGetValue((grid, cell), out var trace))
                    _traces[(grid, cell)] = trace = TraceFrom(from);
                if (trace.Block != null)
                    return InCast.Contains(trace.Block)
                        ? new MyGridExplosion.MyRaycastDamageInfo(0f, trace.Distance)
                        : CastDDA(trace.Block);
                return new MyGridExplosion.MyRaycastDamageInfo(trace.Clear ? _explosion.Damage : 0f, trace.Distance);
            }

            /// <summary>The game's CastPhysicsRay, without what depends on the ray asking: where the way ends.</summary>
            private Trace TraceFrom(Vector3D from)
            {
                for (var guard = 0; ; guard++)
                {
                    var hit = MyPhysics.CastRay(from, Centre, 29);
                    IMyEntity entity = null;
                    var position = Vector3D.Zero;
                    if (hit.HasValue)
                    {
                        entity = (hit.Value.HkHitInfo.Body.UserObject as MyPhysicsComponentBase)?.Entity;
                        position = hit.Value.Position;
                    }

                    var normal = Centre - from;
                    var distance = (float)normal.Normalize();
                    var grid = entity as MyCubeGrid ?? (entity as MyCubeBlock)?.CubeGrid;
                    if (grid == null)
                        return new Trace { Clear = !hit.HasValue, Distance = distance };

                    var local = Vector3D.Transform(position, grid.PositionComp.WorldMatrixNormalizedInv) * grid.GridSizeR;
                    var step = Vector3D.TransformNormal(normal, grid.PositionComp.WorldMatrixNormalizedInv) / 8.0;
                    for (var i = 0; i < 5; i++)
                    {
                        var block = grid.GetCubeBlock(Vector3I.Round(local));
                        if (block != null) return new Trace { Block = block, Distance = distance };
                        local += step;
                    }

                    var next = Vector3D.Transform(local * grid.GridSize, grid.WorldMatrix);
                    if (new BoundingBoxD(Vector3D.Min(from, next), Vector3D.Max(from, next)).Contains(Centre) == ContainmentType.Contains)
                        return new Trace { Clear = true, Distance = distance };
                    if (guard >= 10)
                        return new Trace { Clear = false, Distance = distance };
                    from = next;
                }
            }
        }

        /// <summary>
        /// Applies the damage. A block in a zone that forbids damage is spared - the game makes the
        /// same check, the plugin's warheads did not. One block failing does not stop the others.
        /// </summary>
        private static void ApplyVolumetricDamageToGrid(MyGridExplosion explosion, long attackerId, List<MySafeZone> zones)
        {
            _applyingExplosion++;
            _explosionAttacker = attackerId;
            try
            {
                ApplyVolumetricDamageToGridInner(explosion, attackerId, zones);
            }
            finally
            {
                _applyingExplosion--;
            }
        }

        private static void ApplyVolumetricDamageToGridInner(MyGridExplosion explosion, long attackerId, List<MySafeZone> zones)
        {
            var beforeHandlers = MyDamageSystem.Static.HasAnyBeforeHandler;
            foreach (var pair in explosion.DamagedBlocks)
            {
                var block = pair.Key;
                try
                {
                    if (block.CubeGrid.MarkedForClose || block.FatBlock != null && block.FatBlock.MarkedForClose ||
                        block.IsDestroyed || !block.CubeGrid.BlocksDestructionEnabled)
                        continue;
                    if (zones.Count > 0 && InZone(zones, block.WorldPosition)) continue;

                    // An armed warhead the blast reaches goes off itself a couple of frames later -
                    // its own explosion, not a part of this one - instead of being blown apart.
                    if (block.FatBlock is MyWarhead warhead && warhead.IsArmed)
                    {
                        SetOff(warhead, MyUtils.GetRandomInt(ChainMinFrames, ChainMaxFrames + 1));
                        continue;
                    }

                    var amount = pair.Value;
                    if (amount <= 0f) continue;
                    if (beforeHandlers && block.UseDamageSystem)
                    {
                        // A block that takes the damage through DoDamage has the handlers asked
                        // again there, loot and all: here they only say how much gets through.
                        var info = new MyDamageInformation(false, amount, MyDamageType.Explosion, attackerId);
                        LootProcessor.Suppressed = true;
                        try
                        {
                            MyDamageSystem.Static.RaiseBeforeDamageApplied(block, ref info);
                        }
                        finally
                        {
                            LootProcessor.Suppressed = false;
                        }
                        if (info.Amount <= 0f) continue;
                        amount = info.Amount;
                    }

                    if (block.FatBlock == null && block.Integrity / block.DeformationRatio < amount)
                    {
                        // removed outright, without DoDamage: its loot is counted here, once
                        LootProcessor.CalculateLoot(block, new MyDamageInformation(false, amount, MyDamageType.Explosion, attackerId));
                        block.CubeGrid.RemoveDestroyedBlock(block, 0L);
                    }
                    else
                    {
                        if (block.FatBlock != null) amount *= 7f;
                        var cockpit = block.FatBlock as MyCockpit;
                        var pilot = cockpit?.Pilot;
                        block.DoDamage(amount, MyDamageType.Explosion, true, null, attackerId: attackerId);
                        if (pilot != null && (block.IsDestroyed || cockpit.MarkedForClose || cockpit.Closed))
                            ThrowOut(cockpit, pilot);
                    }

                    foreach (var neighbour in block.Neighbours)
                        neighbour.CubeGrid.Physics?.AddDirtyBlock(neighbour);
                    block.CubeGrid.Physics?.AddDirtyBlock(block);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Explosion damage to a block failed");
                }
            }
        }
        // ------------------------------------------------------------------ the pilot of a lost cockpit

        /// <summary>What a pilot is left with when a blast destroys the cockpit around them.</summary>
        private const float PilotHealthLeft = 0.03f;

        // Game thread: set while an explosion's damage is applied, so that a cockpit it destroys
        // is known to have been destroyed by it.
        private static int _applyingExplosion;
        private static long _explosionAttacker;

        private static readonly MethodInfo CharacterDoDamage = typeof(MyCharacter).GetMethod(nameof(MyCharacter.DoDamage),
            BindingFlags.Instance | BindingFlags.Public, null,
            new[] { typeof(float), typeof(MyStringHash), typeof(bool), typeof(long), typeof(MyStringHash?) }, null);

        /// <summary>
        /// A destroyed cockpit throws its pilot out and deals them a thousand damage - the game
        /// kills them. The call is replaced: when a blast destroyed the cockpit, the pilot is only
        /// thrown out, left with <see cref="PilotHealthLeft"/> of their health.
        /// </summary>
        internal static IEnumerable<MsilInstruction> CockpitPilotTranspiler(IEnumerable<MsilInstruction> instructions)
        {
            var list = instructions.ToList();
            var replaced = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var instruction = list[i];
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
                if (!(instruction.Operand is MsilOperandInline<MethodBase> operand) || operand.Value != CharacterDoDamage) continue;
                var call = new MsilInstruction(OpCodes.Call).InlineValue((MethodBase)Method(nameof(HurtPilotOfLostCockpit)));
                foreach (var label in instruction.Labels) call.Labels.Add(label);
                list[i] = call;
                replaced++;
            }
            if (replaced != 1)
                throw new InvalidOperationException("MissilePatch: expected one pilot.DoDamage in MyCockpit.OnUnregisteredFromGridSystems, found " + replaced);
            return list;
        }

        /// <summary>
        /// The pilot of a cockpit the blast destroyed: out of the seat, if the game has not thrown
        /// them out yet, and down to <see cref="PilotHealthLeft"/> of their health.
        /// </summary>
        private static void ThrowOut(MyCockpit cockpit, MyCharacter pilot)
        {
            try
            {
                if (cockpit.Pilot == pilot) cockpit.RemovePilot();
                HurtToWhatIsLeft(pilot);
            }
            catch (Exception e)
            {
                Log.Error(e, "Throwing the pilot out of a destroyed cockpit failed");
            }
        }

        /// <summary>
        /// Damage that leaves the pilot <see cref="PilotHealthLeft"/> of their health, through the
        /// damage system like any damage: safe zones and protected characters are respected. A
        /// pilot already that low takes none.
        /// </summary>
        private static bool HurtToWhatIsLeft(MyCharacter pilot)
        {
            var health = pilot.StatComp?.Health;
            if (health == null || pilot.IsDead || pilot.MarkedForClose) return false;
            var over = health.Value - health.MaxValue * PilotHealthLeft;
            var modifier = pilot.CharacterGeneralDamageModifier;
            if (over <= 0f || modifier <= 0f) return false;
            return pilot.DoDamage(over / modifier, MyDamageType.Explosion, true, _explosionAttacker);
        }

        /// <summary>
        /// Stands in for pilot.DoDamage in the destroyed cockpit. Outside an explosion, or without
        /// ExplosionTweaks, it is that call. Inside one the pilot takes the blast's damage down to
        /// <see cref="PilotHealthLeft"/> of their health, through the damage system like any damage:
        /// safe zones and protected characters are respected.
        /// </summary>
        private static bool HurtPilotOfLostCockpit(MyCharacter pilot, float damage, MyStringHash damageType, bool updateSync,
            long attackerId, MyStringHash? extraInfo)
        {
            if (_applyingExplosion == 0 || pilot == null || !SentisGameplayImprovementsPlugin.Config.ExplosionTweaks)
                return pilot != null && pilot.DoDamage(damage, damageType, updateSync, attackerId, extraInfo);
            try
            {
                return HurtToWhatIsLeft(pilot);
            }
            catch (Exception e)
            {
                Log.Error(e, "Hurting the pilot of a destroyed cockpit failed");
                return false;
            }
        }
    }
}
