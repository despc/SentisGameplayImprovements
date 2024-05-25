﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NAPI;
using NLog;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GameSystems;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Torch.Managers.PatchManager;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRage.Groups;
using VRage.Utils;
using VRageMath;

namespace SentisGameplayImprovements
{
    [PatchShim]
    public static class MissilePatch
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Patch(PatchContext ctx)
        {
            var MyWarheadExplodeMethod = typeof(MyWarhead).GetMethod(
                nameof(MyWarhead.Explode), BindingFlags.Instance | BindingFlags.Public);
            ctx.GetPattern(MyWarheadExplodeMethod).Prefixes.Add(
                typeof(MissilePatch).GetMethod(nameof(MyWarheadExplodePatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));

            var MyWarheadDoDamageMethod = typeof(MyWarhead)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .First(info => info.Name.Contains("DoDamage"));
            ctx.GetPattern(MyWarheadDoDamageMethod).Prefixes.Add(
                typeof(MissilePatch).GetMethod(nameof(MyWarheadDoDamagePatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));


            var MyWarheadOnDestroyMethod = typeof(MyWarhead)
                .GetMethod(nameof(MyWarhead.OnDestroy),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            ctx.GetPattern(MyWarheadOnDestroyMethod).Prefixes.Add(
                typeof(MissilePatch).GetMethod(nameof(MyWarheadOnDestroyPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            
            
            var MyExplosionType = typeof(MyVoxelBase).Assembly.GetType("Sandbox.Game.MyExplosion");
            var MyExplosionApplyVolumetricExplosion = MyExplosionType
                .GetMethod("ApplyVolumetricExplosion",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            ctx.GetPattern(MyExplosionApplyVolumetricExplosion).Prefixes.Add(
                typeof(MissilePatch).GetMethod(nameof(ApplyVolumetricExplosionPatched),
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
            
            
            //Подсмотрено в DePatch
            ctx.Prefix(typeof(MyExplosionInfo), "get_AffectVoxels", typeof(MissilePatch), nameof(AffectVoxelsPatch));
        }
        
        private static bool AffectVoxelsPatch()
        {
            if (!SentisGameplayImprovementsPlugin.Config.DamageVoxelsFromExplosions)
            {
                return false;
            }
            return true;
        }

        private static void GetBlocksInsideSpheres(MyCubeGrid grid,
            ref BoundingSphereD sphere1,
            ref BoundingSphereD sphere2,
            ref BoundingSphereD sphere3,
            HashSet<MySlimBlock> blocks1,
            HashSet<MySlimBlock> blocks2,
            HashSet<MySlimBlock> blocks3,
            bool respectDeformationRatio,
            float detectionBlockHalfSize,
            ref MatrixD invWorldGrid)
        {
            ConcurrentDictionary<Vector3I, MyCube> m_cubes = (ConcurrentDictionary<Vector3I, MyCube>)grid.easyGetField("m_cubes");
            HashSet<MyCubeBlock> m_processedBlocks = new HashSet<MyCubeBlock>();
            blocks1.Clear();
            blocks2.Clear();
            blocks3.Clear();
            m_processedBlocks.Clear();
            Vector3D result;
            Vector3D.Transform(ref sphere3.Center, ref invWorldGrid, out result);
            Vector3I vector3I1 = Vector3I.Round((result - sphere3.Radius) * (double)grid.GridSizeR);
            Vector3I vector3I2 = Vector3I.Round((result + sphere3.Radius) * (double)grid.GridSizeR);
            Vector3 vector3 = new Vector3(detectionBlockHalfSize);
            BoundingSphereD sphere4 = new BoundingSphereD(result, sphere1.Radius);
            BoundingSphereD sphere5 = new BoundingSphereD(result, sphere2.Radius);
            BoundingSphereD sphere6 = new BoundingSphereD(result, sphere3.Radius);
            if ((vector3I2.X - vector3I1.X) * (vector3I2.Y - vector3I1.Y) * (vector3I2.Z - vector3I1.Z) <
                m_cubes.Count)
            {
                Vector3I key = new Vector3I();
                for (key.X = vector3I1.X; key.X <= vector3I2.X; ++key.X)
                {
                    for (key.Y = vector3I1.Y; key.Y <= vector3I2.Y; ++key.Y)
                    {
                        for (key.Z = vector3I1.Z; key.Z <= vector3I2.Z; ++key.Z)
                        {
                            MyCube myCube;
                            if (m_cubes.TryGetValue(key, out myCube))
                            {
                                MySlimBlock cubeBlock = myCube.CubeBlock;
                                if (cubeBlock.FatBlock == null || !m_processedBlocks.Contains(cubeBlock.FatBlock))
                                {
                                    m_processedBlocks.Add(cubeBlock.FatBlock);
                                    if (respectDeformationRatio)
                                    {
                                        sphere4.Radius = sphere1.Radius * (double)cubeBlock.DeformationRatio;
                                        sphere5.Radius = sphere2.Radius * (double)cubeBlock.DeformationRatio;
                                        sphere6.Radius = sphere3.Radius * (double)cubeBlock.DeformationRatio;
                                    }

                                    BoundingBox boundingBox = cubeBlock.FatBlock == null
                                        ? new BoundingBox(cubeBlock.Position * grid.GridSize - vector3,
                                            cubeBlock.Position * grid.GridSize + vector3)
                                        : new BoundingBox(cubeBlock.Min * grid.GridSize - grid.GridSizeHalf,
                                            cubeBlock.Max * grid.GridSize + grid.GridSizeHalf);
                                    if (boundingBox.Intersects((BoundingSphere)sphere6))
                                    {
                                        if (boundingBox.Intersects((BoundingSphere)sphere5))
                                        {
                                            if (boundingBox.Intersects((BoundingSphere)sphere4))
                                                blocks1.Add(cubeBlock);
                                            else
                                                blocks2.Add(cubeBlock);
                                        }
                                        else
                                            blocks3.Add(cubeBlock);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (MyCube myCube in (IEnumerable<MyCube>)m_cubes.Values)
                {
                    MySlimBlock cubeBlock = myCube.CubeBlock;
                    if (cubeBlock.FatBlock == null || !m_processedBlocks.Contains(cubeBlock.FatBlock))
                    {
                        m_processedBlocks.Add(cubeBlock.FatBlock);
                        if (respectDeformationRatio)
                        {
                            sphere4.Radius = sphere1.Radius * (double)cubeBlock.DeformationRatio;
                            sphere5.Radius = sphere2.Radius * (double)cubeBlock.DeformationRatio;
                            sphere6.Radius = sphere3.Radius * (double)cubeBlock.DeformationRatio;
                        }

                        BoundingBox boundingBox = cubeBlock.FatBlock == null
                            ? new BoundingBox(cubeBlock.Position * grid.GridSize - vector3,
                                cubeBlock.Position * grid.GridSize + vector3)
                            : new BoundingBox(cubeBlock.Min * grid.GridSize - grid.GridSizeHalf,
                                cubeBlock.Max * grid.GridSize + grid.GridSizeHalf);
                        if (boundingBox.Intersects((BoundingSphere)sphere6))
                        {
                            if (boundingBox.Intersects((BoundingSphere)sphere5))
                            {
                                if (boundingBox.Intersects((BoundingSphere)sphere4))
                                    blocks1.Add(cubeBlock);
                                else
                                    blocks2.Add(cubeBlock);
                            }
                            else
                                blocks3.Add(cubeBlock);
                        }
                    }
                }
            }
            
        }


        private static bool ApplyVolumetricExplosionPatched(ref MyExplosionInfo m_explosionInfo,
            List<MyEntity> entities, ref bool __result)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks)
            {
                return true;
            }
            
            try
            {
                HashSet<MyEntity> chars = entities.Where(entity => entity is MyCharacter).ToHashSet();

                foreach (var character in chars)
                {
                    if (character is MyCharacter myCharacter && myCharacter.IsUsing is MyCockpit)
                    {
                        entities.Remove(character);
                    }
                }
                
                HashSet<MyEntity> entitiesSet = new HashSet<MyEntity>(entities);
                var explosionSphere = m_explosionInfo.ExplosionSphere;
                float damage = m_explosionInfo.Damage;
                if (m_explosionInfo.OwnerEntity is MyFloatingObject)
                {
                    var myFloatingObject = ((MyFloatingObject) m_explosionInfo.OwnerEntity);
                    var count = myFloatingObject.Amount.Value.ToIntSafe();
                    var itemDefinition = myFloatingObject.ItemDefinition;
                    if ("Explosives".Equals(itemDefinition.DisplayNameText))
                    {
                        damage = SentisGameplayImprovementsPlugin.Config.ExplosivesDamage * count;
                        explosionSphere.Radius = 15;
                    }

                    if (itemDefinition is MyAmmoMagazineDefinition)
                    {
                        var ammoDefinitionId = ((MyAmmoMagazineDefinition) itemDefinition).AmmoDefinitionId;
                        var myAmmoDefinition = MyDefinitionManager.Static.GetAmmoDefinition(ammoDefinitionId);
                        if (myAmmoDefinition is MyProjectileAmmoDefinition)
                        {
                            var ProjectileMassDamage = ((MyProjectileAmmoDefinition) myAmmoDefinition).ProjectileMassDamage;
                            if (ProjectileMassDamage == 0)
                            {
                                ProjectileMassDamage = 30;
                            }
                            damage = ProjectileMassDamage
                                     * SentisGameplayImprovementsPlugin.Config.ProjectileAmmoExplosionMultiplier * count;
                                explosionSphere.Radius = SentisGameplayImprovementsPlugin.Config.AmmoExplosionRadius;
                            
                        }
                        if (myAmmoDefinition is MyMissileAmmoDefinition)
                        {
                            var missileExplosionDamage = ((MyMissileAmmoDefinition) myAmmoDefinition).MissileExplosionDamage;
                            if (missileExplosionDamage == 0)
                            {
                                missileExplosionDamage = 5000;
                            }

                            damage = missileExplosionDamage
                                     * SentisGameplayImprovementsPlugin.Config.MissileAmmoExplosionMultiplier * count;
                                explosionSphere.Radius = SentisGameplayImprovementsPlugin.Config.AmmoExplosionRadius;
                            
                        }
                    }
                }
                ApplyVolumetricExplosionOnGrid(damage, ref explosionSphere, 0L, new List<MyEntity>(entitiesSet), 0);
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

            return false;
        }
        private static void MakeExplosionAndDamage(long attackerId, Vector3D explosionPosition,
            float explosionRadius, float explosionDamage, long originEntity, bool isPearcingDamage,
            bool isWarhead = false)
        {
            BoundingSphereD explosionSphere = new BoundingSphereD(explosionPosition, explosionRadius);
            var topMostEntitiesInSphere =
                new HashSet<MyEntity>(MyEntities.GetTopMostEntitiesInSphere(ref explosionSphere));
            ApplyVolumetricExplosionOnGrid(
                explosionDamage, ref explosionSphere, originEntity,
                new List<MyEntity>(topMostEntitiesInSphere), attackerId, isWarhead, isPearcingDamage);
        }

        private static void ApplyExplosionOnVoxel(BoundingSphereD sphere)
        {
            if ( !MySession.Static.EnableVoxelDestruction || !MySession.Static.HighSimulationQuality)
                return;
            
            if (!SentisGameplayImprovementsPlugin.Config.DamageVoxelsFromExplosions)
            {
               return;
            }
            
            List<MyVoxelBase> voxelsTmp = new List<MyVoxelBase>();
            List<MyVoxelBase> voxelsToCutTmp = new List<MyVoxelBase>();
            MySession.Static.VoxelMaps.GetAllOverlappingWithSphere(ref sphere, voxelsTmp);
            for (int index = voxelsTmp.Count - 1; index >= 0; --index)
                voxelsToCutTmp.Add(voxelsTmp[index].RootVoxel);
            voxelsTmp.Clear();
            foreach (MyVoxelBase voxelMap in voxelsToCutTmp)
            {
                bool createDebris = true;
                var type = typeof(MyVoxelBase).Assembly.GetType("Sandbox.Game.MyExplosion");
                ReflectionUtils.InvokeStaticMethod(type, "CutOutVoxelMap",
                    new object[] {(float) sphere.Radius * 0.3f, sphere.Center, voxelMap, createDebris, false});
                voxelMap.RequestVoxelCutoutSphere(sphere.Center, (float) sphere.Radius * 0.3f, createDebris, false);
            }
            voxelsToCutTmp.Clear();
        }

        public static void ComputeDamagedBlocks(MyGridExplosion m_gridExplosion, bool pearcingDamage, long onShieldGridId = -1)
        {
            Dictionary<MySlimBlock, float> m_damagedBlocks = new Dictionary<MySlimBlock, float>();


            if (pearcingDamage)
            {
                foreach (MySlimBlock affectedCubeBlock in m_gridExplosion.AffectedCubeBlocks)
                {
                    m_gridExplosion.DamagedBlocks.Add(affectedCubeBlock, m_gridExplosion.Damage);
                }

                return;
            }

            foreach (MySlimBlock affectedCubeBlock in m_gridExplosion.AffectedCubeBlocks)
            {
                if (affectedCubeBlock.CubeGrid.EntityId == onShieldGridId)
                {
                    continue;
                }
                Dictionary<MySlimBlock, MyGridExplosion.MyRaycastDamageInfo> m_damageRemaining =
                    new Dictionary<MySlimBlock, MyGridExplosion.MyRaycastDamageInfo>();
                Stack<MySlimBlock> m_castBlocks = new Stack<MySlimBlock>();
                MyGridExplosion.MyRaycastDamageInfo raycastDamageInfo =
                    CastDDA(affectedCubeBlock, m_castBlocks, m_damageRemaining, m_gridExplosion);
                while (m_castBlocks.Count > 0)
                {
                    MySlimBlock key = m_castBlocks.Pop();
                    if (key.FatBlock is MyWarhead)
                    {
                        m_damagedBlocks[key] = 1E+07f;
                        continue;
                    }

                    if (m_damagedBlocks.ContainsKey(key))
                    {
                        break;
                    }

                    float blockCenterToExplosionCenter =
                        (float) (key.WorldAABB.Center - m_gridExplosion.Sphere.Center).Length();
                    if ((double) raycastDamageInfo.DamageRemaining > 0.0)
                    {
                        float num2 =
                            MathHelper.Clamp(
                                (float) (1.0 - ((double) blockCenterToExplosionCenter -
                                                (double) raycastDamageInfo.DistanceToExplosion) /
                                    (m_gridExplosion.Sphere.Radius -
                                     (double) raycastDamageInfo.DistanceToExplosion)), 0.0f, 1f);

                        if ((double) num2 > 0.0)
                        {
                            m_damagedBlocks.Add(key,
                                raycastDamageInfo.DamageRemaining * num2 * key.BlockDefinition.GeneralDamageMultiplier);
                            var effectiveBlockHP = (double) key.Integrity / key.BlockDefinition.GeneralDamageMultiplier;
                            raycastDamageInfo.DamageRemaining = Math.Max(0.0f,
                                (float) ((double) raycastDamageInfo.DamageRemaining * (double) num2 -
                                         effectiveBlockHP));
                        }
                        else
                            m_damagedBlocks.Add(key, raycastDamageInfo.DamageRemaining);
                    }
                    else
                        raycastDamageInfo.DamageRemaining = 0.0f;

                    raycastDamageInfo.DistanceToExplosion = Math.Abs(blockCenterToExplosionCenter);
                    m_damageRemaining.Add(key, raycastDamageInfo);
                }
            }

            foreach (var mDamagedBlock in m_damagedBlocks)
            {
                m_gridExplosion.DamagedBlocks.Add(mDamagedBlock.Key, mDamagedBlock.Value);
            }
        }

        private static MyGridExplosion.MyRaycastDamageInfo CastDDA(MySlimBlock cubeBlock,
            Stack<MySlimBlock> m_castBlocks,
            Dictionary<MySlimBlock,
                MyGridExplosion.MyRaycastDamageInfo> m_damageRemaining, MyGridExplosion m_gridExplosion)
        {
            if (m_damageRemaining.ContainsKey(cubeBlock))
                return m_damageRemaining[cubeBlock];
            int stackOverflowGuard = 0;
            m_castBlocks.Push(cubeBlock);
            Vector3D worldCenter;
            cubeBlock.ComputeWorldCenter(out worldCenter);
            List<Vector3I> m_cells = new List<Vector3I>();
            cubeBlock.CubeGrid.RayCastCells(worldCenter, m_gridExplosion.Sphere.Center, m_cells, new Vector3I?(),
                false, true);
            (m_gridExplosion.Sphere.Center - worldCenter).Normalize();
            foreach (Vector3I cell in m_cells)
            {
                Vector3D fromWorldPos =
                    Vector3D.Transform(cell * cubeBlock.CubeGrid.GridSize, cubeBlock.CubeGrid.WorldMatrix);
                int num = MyDebugDrawSettings.DEBUG_DRAW_EXPLOSION_DDA_RAYCASTS ? 1 : 0;
                MySlimBlock cubeBlock1 = cubeBlock.CubeGrid.GetCubeBlock(cell);
                if (cubeBlock1 == null)
                    return IsExplosionInsideCell(cell, cubeBlock.CubeGrid, m_gridExplosion)
                        ? new MyGridExplosion.MyRaycastDamageInfo(m_gridExplosion.Damage,
                            (float) (fromWorldPos - m_gridExplosion.Sphere.Center).Length())
                        : CastPhysicsRay(fromWorldPos, ref stackOverflowGuard, m_gridExplosion, m_castBlocks,
                            m_damageRemaining);
                if (cubeBlock1 != cubeBlock)
                {
                    if (m_damageRemaining.ContainsKey(cubeBlock1))
                        return m_damageRemaining[cubeBlock1];
                    if (!m_castBlocks.Contains(cubeBlock1))
                        m_castBlocks.Push(cubeBlock1);
                }
                else if (IsExplosionInsideCell(cell, cubeBlock.CubeGrid, m_gridExplosion))
                    return new MyGridExplosion.MyRaycastDamageInfo(m_gridExplosion.Damage,
                        (float) (fromWorldPos - m_gridExplosion.Sphere.Center).Length());
            }

            return new MyGridExplosion.MyRaycastDamageInfo(m_gridExplosion.Damage,
                (float) (worldCenter - m_gridExplosion.Sphere.Center).Length());
        }

        private static bool
            IsExplosionInsideCell(Vector3I cell, MyCubeGrid cellGrid, MyGridExplosion m_gridExplosion) =>
            cellGrid.WorldToGridInteger(m_gridExplosion.Sphere.Center) == cell;

        private static MyGridExplosion.MyRaycastDamageInfo CastPhysicsRay(Vector3D fromWorldPos,
            ref int stackOverflowGuard, MyGridExplosion m_gridExplosion, Stack<MySlimBlock> m_castBlocks,
            Dictionary<MySlimBlock, MyGridExplosion.MyRaycastDamageInfo> m_damageRemaining)
        {
            Vector3D position = Vector3D.Zero;
            IMyEntity myEntity = (IMyEntity) null;
            MyPhysics.HitInfo? nullable = MyPhysics.CastRay(fromWorldPos, m_gridExplosion.Sphere.Center, 29);
            if (nullable.HasValue)
            {
                myEntity = nullable.Value.HkHitInfo.Body.UserObject != null
                    ? ((MyPhysicsComponentBase) nullable.Value.HkHitInfo.Body.UserObject).Entity
                    : (IMyEntity) null;
                position = nullable.Value.Position;
            }

            Vector3D normal = m_gridExplosion.Sphere.Center - fromWorldPos;
            float distanceToExplosion = (float) normal.Normalize();
            MyCubeGrid myCubeGrid = null;
            if (!(myEntity is MyCubeGrid) && myEntity is MyCubeBlock myCubeBlock)
            {
                myCubeGrid = myCubeBlock.CubeGrid;
            }
            else if (myEntity is MyCubeGrid)
            {
                myCubeGrid = (MyCubeGrid) myEntity;
            }

            if (myCubeGrid != null)
            {
                Vector3D vector3D1 = Vector3D.Transform(position, myCubeGrid.PositionComp.WorldMatrixNormalizedInv) *
                                     (double) myCubeGrid.GridSizeR;
                Vector3D vector3D2 =
                    Vector3D.TransformNormal(normal, myCubeGrid.PositionComp.WorldMatrixNormalizedInv) * 1.0 / 8.0;
                for (int index = 0; index < 5; ++index)
                {
                    Vector3I pos = Vector3I.Round(vector3D1);
                    MySlimBlock cubeBlock = myCubeGrid.GetCubeBlock(pos);
                    if (cubeBlock != null)
                        return m_castBlocks.Contains(cubeBlock)
                            ? new MyGridExplosion.MyRaycastDamageInfo(0.0f, distanceToExplosion)
                            : CastDDA(cubeBlock, m_castBlocks, m_damageRemaining, m_gridExplosion);
                    vector3D1 += vector3D2;
                }

                Vector3D fromWorldPos1 =
                    Vector3D.Transform(vector3D1 * (double) myCubeGrid.GridSize, myCubeGrid.WorldMatrix);
                if (new BoundingBoxD(Vector3D.Min(fromWorldPos, fromWorldPos1),
                        Vector3D.Max(fromWorldPos, fromWorldPos1)).Contains(m_gridExplosion.Sphere.Center) ==
                    ContainmentType.Contains)
                    return new MyGridExplosion.MyRaycastDamageInfo(m_gridExplosion.Damage, distanceToExplosion);
                ++stackOverflowGuard;
                if (stackOverflowGuard > 10)
                {
                    int num = MyDebugDrawSettings.DEBUG_DRAW_EXPLOSION_HAVOK_RAYCASTS ? 1 : 0;
                    return new MyGridExplosion.MyRaycastDamageInfo(0.0f, distanceToExplosion);
                }

                int num1 = MyDebugDrawSettings.DEBUG_DRAW_EXPLOSION_HAVOK_RAYCASTS ? 1 : 0;
                return CastPhysicsRay(fromWorldPos1, ref stackOverflowGuard, m_gridExplosion, m_castBlocks,
                    m_damageRemaining);
            }

            if (!nullable.HasValue)
                return new MyGridExplosion.MyRaycastDamageInfo(m_gridExplosion.Damage, distanceToExplosion);
            int num2 = MyDebugDrawSettings.DEBUG_DRAW_EXPLOSION_HAVOK_RAYCASTS ? 1 : 0;
            return new MyGridExplosion.MyRaycastDamageInfo(0.0f, distanceToExplosion);
        }

        private static void ApplyVolumetricExplosionOnGrid(float MissileExplosionDamage,
            ref BoundingSphereD sphere,
            long OriginEntity,
            List<MyEntity> entities, long attackerId, bool isWarhead = false, bool isPearcingDamage = false)
        {
            MyGridExplosion m_gridExplosion = new MyGridExplosion();
            m_gridExplosion.Init(sphere, MissileExplosionDamage);
            var Node2 = (MyCubeGrid) null;
            MyGroups<MyCubeGrid, MyGridLogicalGroupData>.Group group = null;
            if (!MySession.Static.Settings.EnableTurretsFriendlyFire && OriginEntity != 0L)
            {
                MyEntity entityById = MyEntities.GetEntityById(OriginEntity);
                if (entityById != null)
                {
                    var topMostParent = entityById.GetTopMostParent((Type) null);
                    if (topMostParent is MyCubeGrid)
                    {
                        Node2 = (MyCubeGrid) topMostParent;
                        group = MyCubeGridGroups.Static.Logical.GetGroup((MyCubeGrid) topMostParent);
                    }
                }
            }

            if (SentisGameplayImprovementsPlugin.Config.AsyncExplosion)
            {
                var d = sphere;
                Parallel.StartBackground(() => CollectBlocksAsync(d, new List<MyEntity>(entities), isWarhead, Node2,
                    group, m_gridExplosion, attackerId, isPearcingDamage));

                void CollectBlocksAsync(BoundingSphereD sphere_t, List<MyEntity> entities_t, bool isWarhead_t,
                    MyCubeGrid Node2_t, MyGroups<MyCubeGrid, MyGridLogicalGroupData>.Group group_t,
                    MyGridExplosion m_gridExplosion_t,
                    long attackerId_t, bool isPearcingDamage_t)
                {
                    try
                    {
                        CollectBlocks(sphere_t, entities_t, isWarhead_t, Node2_t, group_t, m_gridExplosion_t);
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            try
                            {
                                var onShieldGridId = DamageShieldAndObjects(attackerId, entities_t, sphere_t, m_gridExplosion_t.Damage);
                                ComputeDamagedBlocks(m_gridExplosion_t, isPearcingDamage_t, onShieldGridId);
                                ApplyExplosionOnVoxel(sphere_t);
                                ApplyVolumetricDamageToGrid(m_gridExplosion_t, attackerId_t);
                            }
                            catch (Exception e)
                            {
                            }
                        });
                    }
                    catch (Exception e)
                    {
                    }
                }

                return;
            }

            CollectBlocks(sphere, entities, isWarhead, Node2, group, m_gridExplosion);
            ApplyExplosionOnVoxel(sphere);
            var damageShieldAndObjects = DamageShieldAndObjects(attackerId, entities, sphere, m_gridExplosion.Damage);
            ComputeDamagedBlocks(m_gridExplosion, isPearcingDamage, damageShieldAndObjects);
            ApplyVolumetricDamageToGrid(m_gridExplosion, attackerId);
        }

        private static long DamageShieldAndObjects(long attackerId, List<MyEntity> entities_t, BoundingSphereD sphere_t,
            float damage)
        {
            long onShieldGridId = -1;
            foreach (var myEntity in entities_t)
            {
                if (myEntity is MyCharacter)
                {
                    var myCharacter = ((MyCharacter) myEntity);
                    if (myCharacter.IsUsing is MyCockpit)
                    {
                        var charDamage = ((MyCharacter) myEntity).Integrity - 25;
                        if (charDamage > 0)
                        {
                            myCharacter.DoDamage(charDamage, MyDamageType.Explosion, true, attackerId: attackerId);
                        }
                        continue;
                    }
                    myCharacter.DoDamage(99999, MyDamageType.Explosion, true, attackerId: attackerId);
                }

                if (myEntity is MyFloatingObject)
                {
                    ((MyFloatingObject) myEntity).DoDamage(99999, MyDamageType.Explosion, true, attackerId: attackerId, null);
                }
                
                if ("dShield".Equals(myEntity.DisplayName))
                {
                    IMyTerminalBlock shield = null;
                    var entToShield =
                        SentisGameplayImprovementsPlugin.SApi.MatchEntToShieldFastExt(myEntity, true);
                    if (entToShield.HasValue)
                    {
                        shield = entToShield.Value.Item1;
                        
                        var currentShieldHp = (SentisGameplayImprovementsPlugin.SApi.GetMaxHpCap(shield) * (SentisGameplayImprovementsPlugin.SApi.GetShieldPercent(shield) / 100)) * 10000;
                        if (currentShieldHp < damage * SentisGameplayImprovementsPlugin.Config.WarheadDamageMultiplier)
                        {
                            onShieldGridId = -1;
                        }
                        else
                        {
                            onShieldGridId = shield.CubeGrid.EntityId;
                        }
                        SentisGameplayImprovementsPlugin.SApi.PointAttackShieldCon(shield, sphere_t.Center,
                            attackerId, (float) (damage * (sphere_t.Radius / SentisGameplayImprovementsPlugin.Config.WarheadDamageMultiplier)), 0, false, true);
                    }

                    
                }
            }
            return onShieldGridId;
        }

        private static void CollectBlocks(BoundingSphereD sphere, List<MyEntity> entities, bool isWarhead,
            MyCubeGrid Node2, MyGroups<MyCubeGrid, MyGridLogicalGroupData>.Group group,
            MyGridExplosion m_gridExplosion)
        {
            foreach (MyEntity entity in entities)
            {
                if (entity.IsPreview)
                {
                    continue;
                }
                
                var Node = entity as MyCubeGrid;
                if (Node == null)
                {
                    continue;
                }

                if (isWarhead || (
                        (Node.CreatePhysics && Node != Node2) &&
                        (group == null || MyCubeGridGroups.Static.Logical.GetGroup(Node) != group)))
                {
                    m_gridExplosion.AffectedCubeGrids.Add(Node);
                    float detectionBlockHalfSize = (float) ((double) Node.GridSize / 2.0 / 1.25);
                    MatrixD invWorldGrid = Node.PositionComp.WorldMatrixInvScaled;
                    BoundingSphereD sphere1 = new BoundingSphereD(sphere.Center,
                        Math.Max(0.100000001490116, sphere.Radius - (double) Node.GridSize));
                    BoundingSphereD sphere2 = new BoundingSphereD(sphere.Center, sphere.Radius);
                    BoundingSphereD sphere3 = new BoundingSphereD(sphere.Center,
                        sphere.Radius + (double) Node.GridSize * 0.5 * Math.Sqrt(3.0));
                    HashSet<MySlimBlock> m_explodedBlocksInner = new HashSet<MySlimBlock>();
                    HashSet<MySlimBlock> m_explodedBlocksExact = new HashSet<MySlimBlock>();
                    HashSet<MySlimBlock> m_explodedBlocksOuter = new HashSet<MySlimBlock>();
                    GetBlocksInsideSpheres(Node, ref sphere1, ref sphere2, ref sphere3, m_explodedBlocksInner,
                        m_explodedBlocksExact, m_explodedBlocksOuter, false, detectionBlockHalfSize,
                        ref invWorldGrid);
                    m_explodedBlocksInner.UnionWith((IEnumerable<MySlimBlock>) m_explodedBlocksExact);
                    m_gridExplosion.AffectedCubeBlocks.UnionWith(
                        (IEnumerable<MySlimBlock>) m_explodedBlocksInner);
                    foreach (MySlimBlock block in m_explodedBlocksOuter)
                        Node.Physics.AddDirtyBlock(block);
                    m_explodedBlocksInner.Clear();
                    m_explodedBlocksExact.Clear();
                    m_explodedBlocksOuter.Clear();
                }
            }
        }

        private static void ApplyVolumetricDamageToGrid(MyGridExplosion damageInfo, long attackerId)
        {
            Dictionary<MySlimBlock, float> damagedBlocks = damageInfo.DamagedBlocks;
            HashSet<MySlimBlock> affectedCubeBlocks = damageInfo.AffectedCubeBlocks;
            HashSet<MyCubeGrid> affectedCubeGrids = damageInfo.AffectedCubeGrids;

            bool anyBeforeHandler = MyDamageSystem.Static.HasAnyBeforeHandler;
            foreach (KeyValuePair<MySlimBlock, float> keyValuePair in damagedBlocks)
            {
                MySlimBlock key = keyValuePair.Key;
                if (!key.CubeGrid.MarkedForClose && (key.FatBlock == null || !key.FatBlock.MarkedForClose) &&
                    (!key.IsDestroyed && key.CubeGrid.BlocksDestructionEnabled))
                {
                    float amount = keyValuePair.Value;
                    if (anyBeforeHandler && key.UseDamageSystem)
                    {
                        MyDamageInformation info =
                            new MyDamageInformation(false, amount, MyDamageType.Explosion, attackerId);
                        MyDamageSystem.Static.RaiseBeforeDamageApplied((object) key, ref info);
                        if ((double) info.Amount > 0.0)
                            amount = info.Amount;
                        else
                            continue;
                    }

                    if (key.FatBlock == null &&
                        (double) key.Integrity / (double) key.DeformationRatio < (double) amount)
                    {
                        key.CubeGrid.RemoveDestroyedBlock(key, 0L);
                    }
                    else
                    {
                        if (key.FatBlock != null)
                            amount *= 7f;
                        key.DoDamage(amount, MyDamageType.Explosion, true, null, attackerId: attackerId);
                        //Отключим урон от деформации и посмотрим что будет
                        // if (!key.IsDestroyed)
                        //     key.CubeGrid.ApplyDestructionDeformation(key, 1f, new MyHitInfo?(), attackerId);
                    }

                    foreach (MySlimBlock neighbour in key.Neighbours)
                        neighbour.CubeGrid.Physics.AddDirtyBlock(neighbour);
                    key.CubeGrid.Physics.AddDirtyBlock(key);
                }
            }
        }

        private static bool MyWarheadOnDestroyPatched()
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks)
            {
                return true;
            }
            return false;
        }

        private static bool MyWarheadDoDamagePatched(MyWarhead __instance, ref bool __result,
            float damage,
            MyStringHash damageType,
            bool sync,
            MyHitInfo? hitInfo,
            long attackerId)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks)
            {
                return true;
            }
            
            try
            {
                if (!__instance.IsArmed)
                {
                    return true;
                }

                __result = true;
                var maxDelay = SentisGameplayImprovementsPlugin.Config.WarheadExplosionDelay;
                var explosionDelay = 10;
                if (maxDelay > 10)
                {
                    explosionDelay = MyUtils.GetRandomInt(maxDelay / 2, maxDelay);
                }
                
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        try
                        {
                            bool m_marked = (bool)GetInstanceField(typeof(MyWarhead), __instance, "m_marked");
                            if (!m_marked)
                            {
                                InvokeInstanceMethod(typeof(MyWarhead), __instance, "MarkForExplosion", new Object[0]);
                                InvokeInstanceMethod(typeof(MyWarhead), __instance, "ExplodeDelayed",
                                    new Object[] { 500 });
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                            throw;
                        }
                    },
                    StartAt: MySession.Static.GameplayFrameCounter + explosionDelay);
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }

            return false;
        }

        private static bool MyWarheadExplodePatched(MyWarhead __instance)
        {
            if (!SentisGameplayImprovementsPlugin.Config.ExplosionTweaks)
            {
                return true;
            }
            try
            {
                bool m_isExploded = (bool) GetInstanceField(typeof(MyWarhead), __instance, "m_isExploded");
                MyWarheadDefinition m_warheadDefinition =
                    (MyWarheadDefinition) GetInstanceField(typeof(MyWarhead), __instance, "m_warheadDefinition");
                if (m_isExploded || !MySession.Static.WeaponsEnabled || __instance.CubeGrid.Physics == null)
                    return false;
                //this.m_isExploded = true;
                SetInstanceField(typeof(MyWarhead), __instance, "m_isExploded", true);
                bool m_marked = (bool) GetInstanceField(typeof(MyWarhead), __instance, "m_marked");
                if (!m_marked)
                {
                    InvokeInstanceMethod(typeof(MyWarhead), __instance, "MarkForExplosion", new Object[0]);
                }

                BoundingSphereD m_explosionFullSphere =
                    (BoundingSphereD) GetInstanceField(typeof(MyWarhead), __instance, "m_explosionFullSphere");
                MyExplosionTypeEnum explosionTypeEnum = m_explosionFullSphere.Radius > 6.0
                    ? (m_explosionFullSphere.Radius > 20.0
                        ? (m_explosionFullSphere.Radius > 40.0
                            ? MyExplosionTypeEnum.WARHEAD_EXPLOSION_50
                            : MyExplosionTypeEnum.WARHEAD_EXPLOSION_30)
                        : MyExplosionTypeEnum.WARHEAD_EXPLOSION_15)
                    : MyExplosionTypeEnum.WARHEAD_EXPLOSION_02;
                MyExplosionInfo explosionInfo = new MyExplosionInfo()
                {
                    PlayerDamage = 0.0f,
                    Damage = 0,
                    ExplosionType = explosionTypeEnum,
                    ExplosionSphere = m_explosionFullSphere,
                    LifespanMiliseconds = 700,
                    HitEntity = __instance,
                    ParticleScale = 1f,
                    OwnerEntity = __instance.CubeGrid,
                    Direction = new Vector3?((Vector3) __instance.WorldMatrix.Forward),
                    VoxelExplosionCenter = m_explosionFullSphere.Center,
                    ExplosionFlags = MyExplosionFlags.CREATE_DEBRIS | MyExplosionFlags.AFFECT_VOXELS |
                                     MyExplosionFlags.CREATE_DECALS | MyExplosionFlags.CREATE_PARTICLE_EFFECT |
                                     MyExplosionFlags.CREATE_SHRAPNELS | MyExplosionFlags.APPLY_DEFORMATION,
                    VoxelCutoutScale = 1f,
                    PlaySound = true,
                    ApplyForceAndDamage = true,
                    ObjectsRemoveDelayInMiliseconds = 40
                };
                if (__instance.CubeGrid.Physics != null)
                    explosionInfo.Velocity = __instance.CubeGrid.Physics.LinearVelocity;
                var instanceOwnerId = __instance.OwnerId;
                var instanceEntityId = __instance.EntityId;

                MyExplosions.AddExplosion(ref explosionInfo);
                MakeExplosionAndDamage(instanceOwnerId, m_explosionFullSphere.Center,
                    (float) m_explosionFullSphere.Radius,
                    m_warheadDefinition.WarheadExplosionDamage * SentisGameplayImprovementsPlugin.Config.WarheadDamageMultiplier, instanceEntityId, false, true);

                __instance.CubeGrid.RemoveDestroyedBlock(__instance.SlimBlock, instanceOwnerId);
                MyDamageSystem.Static.RaiseDestroyed(__instance.SlimBlock, new MyDamageInformation(false, 999999, MyDamageType.Explosion, attackerId: instanceOwnerId));
                return false;
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

            return false;
        }


        internal static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }

        internal static void SetInstanceField(Type type, object instance, string fieldName, Object value)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            field.SetValue(instance, value);
        }

        internal static void InvokeInstanceMethod(Type type, object instance, string methodName, Object[] args)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Static;
            var method = type.GetMethod(methodName, bindFlags);
            method.Invoke(instance, args);
        }
    }
}