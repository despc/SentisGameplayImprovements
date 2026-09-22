namespace SentisGameplayImprovements.PveZone
{
    /// <summary>The two sides of a damage in the PvE zone, as identities, and what is known about them.</summary>
    public struct PvEParties
    {
        public long Attacker;
        public long Victim;
        public bool AttackerIsNpc;
        public bool VictimIsNpc;
        public ulong AttackerSteamId;
        public ulong VictimSteamId;
        public long AttackerFactionId;
        public long VictimFactionId;
    }

    /// <summary>Who may damage whose grid in the PvE zone.</summary>
    public static class PvERules
    {
        /// <summary>
        /// Whether the damage is stopped. It goes through when either side is unknown (no owner, no
        /// identity), when the grid is the attacker's own (same identity or the same player), when both are
        /// in one faction, and - with <paramref name="npcDamageAllowed"/> - when either side is an NPC.
        /// </summary>
        public static bool Blocks(PvEParties p, bool npcDamageAllowed)
        {
            if (p.Attacker == 0 || p.Victim == 0 || p.Attacker == p.Victim) return false;
            if (npcDamageAllowed && (p.AttackerIsNpc || p.VictimIsNpc)) return false;
            if (p.AttackerSteamId != 0 && p.AttackerSteamId == p.VictimSteamId) return false;
            if (p.AttackerFactionId != 0 && p.AttackerFactionId == p.VictimFactionId) return false;
            return true;
        }
    }
}
