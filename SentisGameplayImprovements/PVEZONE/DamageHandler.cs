using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace SentisGameplayImprovements.PveZone
{
    /// <summary>
    /// Stops damage in the PvE zone from another player:
    /// <list type="bullet">
    /// <item>to a grid - weapons, tools, explosions; thrusters do no damage to a grid there at all;</item>
    /// <item>to a character - weapons, tools, explosions, another player's ship or thruster; an NPC, one's own
    /// explosion, the ground, the air and the cold still hurt.</item>
    /// </list>
    /// </summary>
    public static class DamageHandler
    {
        /// <summary>Registers the handler with the damage system of the session just loaded.</summary>
        public static void Init()
        {
            if (MyAPIGateway.Session != null)
                MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(1, ProcessDamage);
        }

        private static void ProcessDamage(object target, ref MyDamageInformation info)
        {
            if (!SentisGameplayImprovementsPlugin.Config.PvEZoneEnabled || info.Amount == 0) return;
            switch (target)
            {
                case MySlimBlock block when PvECore.IsProtected(block.CubeGrid):
                    ProcessGridDamage(block.CubeGrid, ref info);
                    break;
                case MyCharacter character when PvECore.IsInZone(character.PositionComp.GetPosition()):
                    ProcessCharacterDamage(character, ref info);
                    break;
            }
        }

        private static void ProcessGridDamage(MyCubeGrid grid, ref MyDamageInformation info)
        {
            if (!TryGetAttacker(ref info, out var attacker, out var attackerEntity)) return;
            if (attackerEntity is MyThrust || Blocks(attacker, grid)) Stop(ref info);
        }

        private static void ProcessCharacterDamage(MyCharacter character, ref MyDamageInformation info)
        {
            if (!TryGetAttacker(ref info, out var attacker, out _)) return;
            // NPCs hurt characters in the zone whatever EnableDamageFromNPC says
            if (Blocks(attacker, character.GetPlayerIdentityId(), npcDamageAllowed: true)) Stop(ref info);
        }

        /// <summary>
        /// The identity the damage comes from. False when the damage goes through as it is: no attacker and
        /// not an explosion (a fall, the air, the cold), or the ground. An explosion with no attacker - a
        /// missile of a player who is offline or dead (its owner entity is the player's character), ammo
        /// cooking off - is stopped here and false returned too.
        /// </summary>
        private static bool TryGetAttacker(ref MyDamageInformation info, out long attacker, out MyEntity attackerEntity)
        {
            attacker = 0;
            attackerEntity = null;
            if (info.AttackerId == 0)
            {
                if (info.Type == MyDamageType.Explosion) Stop(ref info);
                return false;
            }
            if (!MyEntities.TryGetEntityById(info.AttackerId, out attackerEntity, allowClosed: true))
            {
                attacker = info.AttackerId;
                return true;
            }
            if (attackerEntity is MyVoxelBase) return false;
            attacker = GetAttackerId(attackerEntity);
            return true;
        }

        /// <summary>Whether damage from the identity <paramref name="attacker"/> to <paramref name="grid"/> is stopped, the grid being in the zone.</summary>
        public static bool Blocks(long attacker, MyCubeGrid grid) =>
            Blocks(attacker, grid.BigOwners.Count > 0 ? grid.BigOwners[0] : 0L, SentisGameplayImprovementsPlugin.Config.EnableDamageFromNPC);

        /// <summary>Whether damage from the identity <paramref name="attacker"/> to what <paramref name="victim"/> has in the zone is stopped.</summary>
        public static bool Blocks(long attacker, long victim, bool npcDamageAllowed)
        {
            if (attacker == 0 || victim == 0 || attacker == victim) return false;
            // an id that is no identity (an entity nobody owns) and a grid with no known owner: let it be
            if (Sync.Players.TryGetIdentity(attacker) == null || Sync.Players.TryGetIdentity(victim) == null) return false;

            var players = MySession.Static.Players;
            var factions = MySession.Static.Factions;
            var parties = new PvEParties
            {
                Attacker = attacker,
                Victim = victim,
                AttackerIsNpc = players.IdentityIsNpc(attacker),
                VictimIsNpc = players.IdentityIsNpc(victim),
                AttackerSteamId = players.TryGetSteamId(attacker),
                VictimSteamId = players.TryGetSteamId(victim),
                AttackerFactionId = factions.TryGetPlayerFaction(attacker)?.FactionId ?? 0,
                VictimFactionId = factions.TryGetPlayerFaction(victim)?.FactionId ?? 0,
            };
            return PvERules.Blocks(parties, npcDamageAllowed);
        }

        private static void Stop(ref MyDamageInformation info)
        {
            info.Amount = 0f;
            info.IsDeformation = false;
        }

        /// <summary>The identity behind the entity a damage came from.</summary>
        public static long GetAttackerId(MyEntity attackerEntity)
        {
            switch (attackerEntity)
            {
                case MyHandDrill handDrill: return handDrill.OwnerIdentityId;
                case MyAutomaticRifleGun rifle: return rifle.OwnerIdentityId;
                case MyEngineerToolBase tool: return tool.OwnerIdentityId;
                case MyUserControllableGun gun: return gun.OwnerId;
                case MyCubeGrid grid: return grid.BigOwners.Count > 0 ? grid.BigOwners[0] : 0L;
                case MyShipToolBase shipTool: return shipTool.OwnerId;
                case MyConveyorSorter weaponCore: return weaponCore.OwnerId;
                case MyCharacter character: return character.GetPlayerIdentityId();
                case MyWarhead warhead: return warhead.OwnerId;
                case MyCubeBlock block: return block.OwnerId;
                default: return 0;
            }
        }
    }
}
