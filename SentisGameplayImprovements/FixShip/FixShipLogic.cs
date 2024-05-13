using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace SentisGameplayImprovements
{
    public class FixShipLogic
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void DoFixShip(long gridId, long sender)
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                try
                {
                    FixGroups(FindGroupByGridId(gridId, sender));
                }
                catch (Exception e)
                {
                    Log.Error("Fixship from COCK failed ", e);
                }
            });
        }

        private static void FixGroups(List<MyCubeGrid> groups)
        {
            FixGroup(groups);
        }

        public static List<MyCubeGrid> FindGroupByGridId(long gridId, long sender)
        {
            MyCubeGrid grid = (MyCubeGrid)MyAPIGateway.Entities.GetEntityById(gridId);
            if (grid.BigOwners == null || grid.BigOwners.Count == 0)
            {
                return new List<MyCubeGrid>();
            }

            if (!grid.BigOwners.Contains(sender))
            {
                var owner = grid.BigOwners.FirstOrDefault<long>();
                IMyFaction senderFaction = MySession.Static.Factions.TryGetPlayerFaction(sender);
                IMyFaction ownerFaction = MySession.Static.Factions.TryGetPlayerFaction(owner);

                if (senderFaction == null || ownerFaction == null)
                {
                    Log.Warn($"Try fixship enemy grid {grid.DisplayName}, sender - {sender}");
                    return new List<MyCubeGrid>();
                }

                if (senderFaction.FactionId != ownerFaction.FactionId)
                {
                    Log.Warn($"Try fixship enemy grid {grid.DisplayName}, sender - {sender}");
                    return new List<MyCubeGrid>();
                }
            }

            List<MyCubeGrid> groupNodes =
                MyCubeGridGroups.Static.GetGroups(GridLinkTypeEnum.Logical).GetGroupNodes(grid);
            return groupNodes;
        }

        private static void FixGroup(List<MyCubeGrid> groups)
        {
            string str = "Server";

            List<MyObjectBuilder_EntityBase> objectBuilders = new List<MyObjectBuilder_EntityBase>();
            List<MyCubeGrid> myCubeGridList = new List<MyCubeGrid>();
            Dictionary<Vector3I, MyCharacter> characters = new Dictionary<Vector3I, MyCharacter>();

            foreach (MyCubeGrid nodeData in groups)
            {
                myCubeGridList.Add(nodeData);
                // nodeData.Physics.LinearVelocity = Vector3.Zero;
                MyObjectBuilder_EntityBase objectBuilder = nodeData.GetObjectBuilder(true);
                if (!objectBuilders.Contains(objectBuilder))
                {
                    objectBuilders.Add(objectBuilder);
                }

                foreach (var myCubeBlock in nodeData.GetFatBlocks())
                {
                    if (myCubeBlock is MyCockpit)
                    {
                        var cockpit = (MyCockpit) myCubeBlock;
                        MyCharacter myCharacter = cockpit.Pilot;
                        if (myCharacter == null)
                        {
                            continue;
                        }
                        characters[cockpit.Position] = myCharacter;
                        DamagePatch.ProtectedChars.Add(myCharacter.EntityId);
                        cockpit.RemovePilot();
                    }
                }
            }

            foreach (MyCubeGrid myCubeGrid in myCubeGridList)
            {
                IMyEntity myEntity = (IMyEntity) myCubeGrid;
                Log.Warn("Player used ShipFixerPlugin from COCK on Grid " +
                         myCubeGrid.DisplayName + " for cut & paste!");

                myEntity.Close();
            }

            MyAPIGateway.Entities.RemapObjectBuilderCollection(
                (IEnumerable<MyObjectBuilder_EntityBase>) objectBuilders);
            foreach (MyObjectBuilder_EntityBase cubeGrid in objectBuilders)
                
                MyAPIGateway.Entities.CreateFromObjectBuilderParallel(cubeGrid,
                    completionCallback: ((Action<IMyEntity>) (entity =>
                    {
                        ((MyCubeGrid) entity).DetectDisconnectsAfterFrame();
                        MyAPIGateway.Entities.AddEntity(entity);
                        foreach (var myCubeBlock in ((MyCubeGrid) entity).GetFatBlocks())
                        {
                            if (myCubeBlock is MyCockpit)
                            {
                                var cockpit = (MyCockpit) myCubeBlock;
                                MyCharacter myCharacter;
                                if (!characters.TryGetValue(cockpit.Position, out myCharacter))
                                {
                                    continue;
                                }
                                
                                MyAPIGateway.Parallel.StartBackground(() =>
                                {
                                    MyAPIGateway.Parallel.Sleep(2000); 
                                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                    {
                                        ((IMyCockpit)cockpit).AttachPilot(myCharacter);
                                        DamagePatch.ProtectedChars.Remove(myCharacter.EntityId);
                                    });
                                });
                            }
                        }
                    })));
        }
    }
}