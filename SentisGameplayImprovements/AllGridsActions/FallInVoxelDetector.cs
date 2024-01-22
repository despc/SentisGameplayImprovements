using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Scripts.Shared;
using VRage.Game.ModAPI;
using VRageMath;

namespace SentisGameplayImprovements.AllGridsActions
{
    public class FallInVoxelDetector
    {
        public Dictionary<long, PositionAndOrientation> gridsPos = new Dictionary<long, PositionAndOrientation>();

        public void SavePos(MyCubeGrid grid)
        {
            gridsPos[grid.EntityId] =
                new PositionAndOrientation(grid.PositionComp.GetPosition(), grid.PositionComp.GetOrientation());
        }

        public void RestorePos(MyCubeGrid grid, bool force = false)
        {
            if (gridsPos.TryGetValue(grid.EntityId, out PositionAndOrientation pos))
            {
                MyAPIGateway.Utilities.InvokeOnGameThread(() => { DoRestoreSync(grid, force, pos); });
            }
        }

        public void DoRestoreSync(MyCubeGrid grid, bool force, PositionAndOrientation pos)
        {
            try
            {
                var grids = grid.GetConnectedGrids(GridLinkTypeEnum.Mechanical);
                var aabb = new BoundingBoxD(grid.PositionComp.WorldAABB.Min, grid.PositionComp.WorldAABB.Max);
                foreach (var g in grids)
                {
                    if (g.IsStatic && !force)
                    {
                        
                        SentisGameplayImprovementsPlugin.Log.Warn("Don't process static grid " + grid.DisplayName);
                        return;
                    }

                    aabb.Include(g.PositionComp.WorldAABB);
                }

                MyPlanet planet = null;
                var currentPosition = grid.PositionComp.GetPosition();
                foreach (var p in EntitiesObserver.Planets)
                {
                    if (planet == null)
                    {
                        planet = p;
                        continue;
                    }

                    if (Vector3D.Distance(planet.PositionComp.GetPosition(), currentPosition) >
                        Vector3D.Distance(p.PositionComp.GetPosition(), currentPosition))
                    {
                        planet = p;
                    }
                }

                if (planet != null && Vector3D.Distance(planet.GetClosestSurfacePointGlobal(currentPosition), currentPosition) < 100)
                {
                    return;
                }
                
                foreach (var g in grids)
                {
                    g.Physics.AngularVelocity = Vector3.Zero;
                    g.Physics.LinearVelocity = Vector3.Zero;
                }

                var vec = (pos.Position - planet.PositionComp.WorldMatrix.Translation);
                vec.Normalize();
                currentPosition = pos.Position + vec * (aabb.Size.Max() + 5);

                var m = grid.WorldMatrix;
                m.Translation = currentPosition;
                grid.Teleport(m);
                SentisGameplayImprovementsPlugin.Log.Warn("Restored from voxels grid " + grid.DisplayName);
            }
            catch (Exception e)
            {
                SentisGameplayImprovementsPlugin.Log.Warn("RestorePos Prevent crash", e);
            }
        }


        public void CheckAndSavePos(MyCubeGrid grid)
        {
            if (grid == null || grid.MarkedForClose || grid.Physics == null || grid.Physics.IsStatic)
            {
                return;
            }

            if (Voxels.IsGridInsideVoxel(grid))
            {
                if (grid.BlocksCount < 5 || grid.Physics.LinearVelocity.Length() < 40)
                {
                    return;
                }
                RestorePos(grid);
                return;
            }

            SavePos(grid);
        }
    }

    public class PositionAndOrientation
    {
        private Vector3D _position;
        private MatrixD _orientation;

        public PositionAndOrientation(Vector3D position, MatrixD orientation)
        {
            _position = position;
            _orientation = orientation;
        }

        public Vector3D Position
        {
            get => _position;
            set => _position = value;
        }

        public MatrixD Orientation
        {
            get => _orientation;
            set => _orientation = value;
        }
    }
}