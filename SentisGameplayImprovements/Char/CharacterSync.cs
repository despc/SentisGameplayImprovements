using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NLog;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Replication;
using Sandbox.Game.Replication.StateGroups;
using Sandbox.Game.World;
using VRage.Network;
using VRageMath;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// A player's own character as the client sees it put right by the server (the commands !sync, !sync reset,
    /// !sync resend; for another player !sync player).
    ///
    /// The client runs its own character ahead of the server (predicted) and corrects it by the server's snapshots,
    /// relative to what it stands on or flies by (its support, <c>ClosestParentId</c>). It jumps to the server's place
    /// only when its own history of that moment has the same support (<c>MyPredictedSnapshotSync</c>): with the support
    /// told otherwise, or no history of the moment, the client can keep its own place - the character elsewhere than on
    /// the server, flying through grids that are there. The server does not know where the client sees it (the client
    /// sends only its camera in spectator), so it can only put things right, not measure them:
    /// <list type="bullet">
    /// <item><see cref="Reset"/>: off a ladder, stopped, teleported where it stands on the server (its physics remade),
    /// its support told anew, its physics state sent at once - the client's prediction gets a support of the server's
    /// and starts over;</item>
    /// <item><see cref="ResendGrids"/>: the grids round it the client has taken from it and given anew, as when they come
    /// into range (not the grid it stands on or sits in, nor those joined to it: they would vanish under it for the
    /// seconds a stream takes);</item>
    /// <item><see cref="ResendCharacter"/>: the character itself taken from the client and given anew - the client makes
    /// it again from the server's state; it takes the control back by itself (its cache of who controls what), and the
    /// server gives it once more when the client has the character.</item>
    /// </list>
    /// Game thread.
    /// </summary>
    public static class CharacterSync
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>Grids round the character resent, metres; and how many at most.</summary>
        public const double GridsRadius = 300;
        public const int MaxGrids = 40;
        /// <summary>How long the control is waited to be given back after a resend of the character, frames.</summary>
        private const int ControlWaitFrames = 60 * 30;

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static MethodInfo _removeForClient, _addForClient, _isReady, _hasReplicable;
        private static FieldInfo _clientStates;

        private static void Bind()
        {
            if (_clientStates != null) return;
            var clientType = typeof(MyReplicationServer).Assembly.GetType("VRage.Network.MyClient") ?? throw new TypeLoadException("VRage.Network.MyClient");
            _removeForClient = typeof(MyReplicationServer).GetMethod("RemoveForClient", Any, null, new[] { typeof(IMyReplicable), clientType, typeof(bool) }, null)
                               ?? throw new MissingMethodException("MyReplicationServer.RemoveForClient");
            _addForClient = typeof(MyReplicationServer).GetMethod("AddForClient", Any, null, new[] { typeof(IMyReplicable), typeof(Endpoint), clientType, typeof(bool), typeof(bool) }, null)
                            ?? throw new MissingMethodException("MyReplicationServer.AddForClient");
            _isReady = clientType.GetMethod("IsReplicableReady", Any, null, new[] { typeof(IMyReplicable) }, null) ?? throw new MissingMethodException("MyClient.IsReplicableReady");
            _hasReplicable = clientType.GetMethod("HasReplicable", Any, null, new[] { typeof(IMyReplicable) }, null) ?? throw new MissingMethodException("MyClient.HasReplicable");
            _clientStates = typeof(MyReplicationServer).GetField("m_clientStates", Any) ?? throw new MissingFieldException("MyReplicationServer.m_clientStates");
        }

        private static MyReplicationServer Server => MyMultiplayer.Static?.ReplicationLayer as MyReplicationServer;

        private static object ClientOf(ulong steamId, out Endpoint endpoint)
        {
            Bind();
            endpoint = new Endpoint(new EndpointId(steamId), 0);
            var states = (IDictionary)_clientStates.GetValue(Server);
            return states.Contains(endpoint) ? states[endpoint] : null;
        }

        /// <summary>Off a ladder, stopped, teleported where the server has it, its support told anew. What was done, for the player.</summary>
        public static string Reset(MyPlayer player)
        {
            var character = player?.Character;
            if (character == null || character.MarkedForClose) return "нет персонажа";
            if (character.IsDead) return "персонаж мёртв";
            if (character.Parent != null) return "персонаж в кресле: сброс только для персонажа снаружи (выйдите из кресла или используйте !sync resend)";
            if (character.IsOnLadder) character.GetOffLadder();
            character.Physics?.ClearSpeed();
            // the same place: its physics taken out and put back (MyEntity.Teleport), a jump the client takes at once
            var matrix = character.WorldMatrix;
            character.Teleport(matrix);
            character.Physics?.ClearSpeed();
            // the support told anew: the server finds it again, and the client, told another one than its own, starts
            // its prediction over from the server's place
            character.ClosestParentId = 0;
            SendState(character);
            Log.Info($"Sync: reset {Name(player)} at {Format(matrix.Translation)}");
            return $"персонаж поставлен туда, где он на сервере ({Format(matrix.Translation)})";
        }

        /// <summary>The grids round the character taken from its client and given anew: how many, and how many left as they were under it.</summary>
        public static (int Sent, int Kept) ResendGrids(MyPlayer player)
        {
            var character = player?.Character;
            var server = Server;
            if (character == null || server == null) return (0, 0);
            var client = ClientOf(player.Id.SteamId, out var endpoint);
            if (client == null) return (0, 0);
            var at = character.PositionComp.GetPosition();
            // what it stands on or sits in, and all joined to it, stays
            var keep = new HashSet<MyCubeGrid>();
            foreach (var under in Underfoot(character))
                foreach (var joined in MyCubeGridGroups.Static.Physical.GetGroupNodes(under))
                    keep.Add(joined);
            var sphere = new BoundingSphereD(at, GridsRadius);
            var grids = MyEntities.GetTopMostEntitiesInSphere(ref sphere).OfType<MyCubeGrid>()
                .Where(g => !g.MarkedForClose && !g.IsPreview && g.Physics != null && !keep.Contains(g))
                .OrderBy(g => g.PositionComp.WorldAABB.DistanceSquared(at)).ToList();
            var sent = 0;
            foreach (var grid in grids)
            {
                if (sent >= MaxGrids) break;
                var replicable = MyExternalReplicable.FindByObject(grid);
                if (replicable == null || !(bool)_hasReplicable.Invoke(client, new object[] { replicable })) continue;
                Resend(server, replicable, endpoint, client);
                sent++;
            }
            Log.Info($"Sync: {sent} grids within {GridsRadius:0} m resent to {Name(player)} (kept {keep.Count} it stands on or sits in)");
            return (sent, keep.Count);
        }

        /// <summary>The character taken from its client and given anew; the control given back once the client has it. What was done.</summary>
        public static string ResendCharacter(MyPlayer player)
        {
            var character = player?.Character;
            var server = Server;
            if (character == null || character.MarkedForClose || server == null) return "нет персонажа";
            if (character.IsDead) return "персонаж мёртв";
            var client = ClientOf(player.Id.SteamId, out var endpoint);
            if (client == null) return "клиент не найден";
            var replicable = MyExternalReplicable.FindByObject(character);
            if (replicable == null) return "персонаж не реплицируется";
            Resend(server, replicable, endpoint, client);
            Log.Info($"Sync: the character of {Name(player)} resent to its client");
            var frames = 0;
            void GiveControl()
            {
                try
                {
                    if (character.MarkedForClose || character.IsDead || player.Character != character) return;
                    var current = ClientOf(player.Id.SteamId, out _);
                    if (current == null) return;
                    if (!(bool)_isReady.Invoke(current, new object[] { replicable }))
                    {
                        if (++frames < ControlWaitFrames) MySandboxGame.Static.Invoke(GiveControl, "SgiSyncControl");
                        else Log.Warn($"Sync: the client of {Name(player)} did not take its character again in {ControlWaitFrames / 60} s");
                        return;
                    }
                    // the control as the server has it, once more: the client has its character anew
                    var controlled = player.Controller.ControlledEntity as VRage.Game.Entity.MyEntity ?? character;
                    MySession.Static.Players.SetControlledEntity(player.Id, controlled);
                    Log.Info($"Sync: the control of {controlled.DisplayName} given back to {Name(player)} after {frames} frames");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Sync: control back");
                }
            }
            MySandboxGame.Static.Invoke(GiveControl, "SgiSyncControl");
            return "персонаж отправлен клиенту заново";
        }

        private static void Resend(MyReplicationServer server, IMyReplicable replicable, Endpoint endpoint, object client)
        {
            _removeForClient.Invoke(server, new[] { replicable, client, (object)true });
            _addForClient.Invoke(server, new[] { replicable, endpoint, client, (object)false, (object)false });
        }

        /// <summary>The character's physics state sent to the clients now, not at its turn.</summary>
        private static void SendState(MyCharacter character)
        {
            var replicable = MyExternalReplicable.FindByObject(character);
            var server = Server;
            if (replicable == null || server == null) return;
            var groups = new List<IMyStateGroup>();
            replicable.GetStateGroups(groups);
            foreach (var group in groups.OfType<MyCharacterPhysicsStateGroup>()) server.AddToDirtyGroups(group);
        }

        /// <summary>The grids the character stands on (its support) or sits in.</summary>
        private static IEnumerable<MyCubeGrid> Underfoot(MyCharacter character)
        {
            if (character.Parent?.GetTopMostParent() is MyCubeGrid seat) yield return seat;
            if (MyEntities.GetEntityById(character.ClosestParentId) is MyCubeGrid support) yield return support;
            var proxy = character.Physics?.CharacterProxy;
            if (proxy == null) yield break;
            var under = new List<VRage.Game.Entity.MyEntity>();
            proxy.GetSupportingEntities(under);
            foreach (var grid in under.OfType<MyCubeGrid>()) yield return grid;
        }

        private static string Name(MyPlayer player) => $"'{player.DisplayName}' ({player.Id.SteamId})";
        private static string Format(Vector3D at) => $"{at.X:0} {at.Y:0} {at.Z:0}";
    }
}
