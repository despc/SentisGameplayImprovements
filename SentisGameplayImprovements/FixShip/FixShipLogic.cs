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

        /// <summary>One fix a player a minute: the cockpit's own cooldown is the client's, a client of its own has none.</summary>
        private const int CooldownSeconds = 60;

        private static readonly Dictionary<long, DateTime> LastFix = new Dictionary<long, DateTime>();

        public static void DoFixShip(long gridId, long sender)
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                try
                {
                    var now = DateTime.UtcNow;
                    DateTime last;
                    if (LastFix.TryGetValue(sender, out last) && (now - last).TotalSeconds < CooldownSeconds)
                    {
                        Notify(sender, $"FixShip: повторить можно через {CooldownSeconds - (int)(now - last).TotalSeconds} с");
                        return;
                    }

                    string refusal;
                    var group = FindGroupByGridId(gridId, sender, out refusal);
                    if (group.Count == 0)
                    {
                        if (refusal != null) Notify(sender, refusal);
                        return;
                    }

                    LastFix[sender] = now;
                    if (LastFix.Count > 1024)
                    {
                        var past = LastFix.Where(p => (now - p.Value).TotalSeconds >= CooldownSeconds).Select(p => p.Key).ToList();
                        foreach (var id in past) LastFix.Remove(id);
                    }

                    FixGroups(group);
                }
                catch (Exception e)
                {
                    Log.Error("Fixship from COCK failed ", e);
                }
            });
        }

        public static void FixGroups(List<MyCubeGrid> groups)
        {
            FixGroup(groups);
        }

        /// <summary>
        /// A grid is the player's to fix when the player is among its main owners (has the most blocks of it, as for
        /// a rename) or a main owner is of the player's faction. The whole group is made anew, so every grid of it
        /// that has an owner must be the player's so: a ship docked to another's by a connector must undock first
        /// (only the asked grid was checked, and the other's ship was cut and pasted too, its pilot thrown out).
        /// </summary>
        public static List<MyCubeGrid> FindGroupByGridId(long gridId, long sender, out string refusal)
        {
            refusal = null;
            var grid = MyAPIGateway.Entities.GetEntityById(gridId) as MyCubeGrid;
            if (grid == null || grid.MarkedForClose) return new List<MyCubeGrid>();

            if (!IsPlayersGrid(grid, sender))
            {
                Log.Warn($"Try fixship enemy grid {grid.DisplayName}, sender - {sender}");
                refusal = "FixShip: грид не ваш и не вашей фракции";
                return new List<MyCubeGrid>();
            }

            List<MyCubeGrid> groupNodes =
                MyCubeGridGroups.Static.GetGroups(GridLinkTypeEnum.Logical).GetGroupNodes(grid);
            foreach (var node in groupNodes)
            {
                if (node == grid || node.BigOwners == null || node.BigOwners.Count == 0) continue;
                if (IsPlayersGrid(node, sender)) continue;
                Log.Warn($"Fixship of {grid.DisplayName} refused: {node.DisplayName} of another is in the group, sender - {sender}");
                refusal = $"FixShip: к кораблю присоединён чужой грид «{node.DisplayName}», отстыкуйтесь";
                return new List<MyCubeGrid>();
            }

            return groupNodes;
        }

        private static bool IsPlayersGrid(MyCubeGrid grid, long player)
        {
            var owners = grid.BigOwners;
            if (owners == null || owners.Count == 0) return false;
            if (owners.Contains(player)) return true;
            var playerFaction = MySession.Static.Factions.TryGetPlayerFaction(player);
            if (playerFaction == null) return false;
            foreach (var owner in owners)
            {
                var ownerFaction = MySession.Static.Factions.TryGetPlayerFaction(owner);
                if (ownerFaction != null && ownerFaction.FactionId == playerFaction.FactionId) return true;
            }

            return false;
        }

        private static void Notify(long player, string text)
        {
            try
            {
                Sandbox.Game.MyVisualScriptLogicProvider.ShowNotification(text, 5000, "Red", player);
            }
            catch (Exception e)
            {
                Log.Warn(e, "FixShip notification");
            }
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