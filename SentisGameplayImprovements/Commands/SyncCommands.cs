using System;
using System.Linq;
using Sandbox.Game.World;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace SentisGameplayImprovements
{
    /// <summary>
    /// The character as the client sees it put right (<see cref="CharacterSync"/>): a player for itself, an admin for any
    /// player online.
    /// </summary>
    public class SyncCommands : CommandModule
    {
        [Command("sync", "Рассинхрон персонажа: сброс на сервере и заново отправленные гриды вокруг (сначала это; не помогло - !sync resend)")]
        [Permission(MyPromoteLevel.None)]
        public void Sync() => Run(Self(), "all");

        [Command("sync reset", "Рассинхрон персонажа: персонаж ставится туда, где он на сервере, скорость и опора сбрасываются")]
        [Permission(MyPromoteLevel.None)]
        public void SyncReset() => Run(Self(), "reset");

        [Command("sync resend", "Рассинхрон персонажа: персонаж заново отправляется клиенту (на миг пропадёт и вернётся)")]
        [Permission(MyPromoteLevel.None)]
        public void SyncResend() => Run(Self(), "resend");

        [Command("sync player", "!sync player <ник или SteamId> [all|reset|resend]: то же для другого игрока")]
        [Permission(MyPromoteLevel.Admin)]
        public void SyncPlayer(string who, string what = "all")
        {
            var player = Find(who);
            if (player == null)
            {
                Context.Respond($"Игрок '{who}' не в сети");
                return;
            }
            Run(player, what.ToLowerInvariant());
        }

        private MyPlayer Self()
        {
            if (Context.Player is MyPlayer player) return player;
            Context.Respond("Это команда игрока; для другого - !sync player <ник>");
            return null;
        }

        private static MyPlayer Find(string who)
        {
            var online = MySession.Static.Players.GetOnlinePlayers();
            if (ulong.TryParse(who, out var steamId))
            {
                var bySteam = online.FirstOrDefault(p => p.Id.SteamId == steamId);
                if (bySteam != null) return bySteam;
            }
            return online.FirstOrDefault(p => string.Equals(p.DisplayName, who, StringComparison.OrdinalIgnoreCase))
                   ?? online.FirstOrDefault(p => p.DisplayName.IndexOf(who, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Run(MyPlayer player, string what)
        {
            if (player == null) return;
            try
            {
                switch (what)
                {
                    case "reset":
                        Context.Respond(player.DisplayName + ": " + CharacterSync.Reset(player));
                        break;
                    case "resend":
                        Context.Respond(player.DisplayName + ": " + CharacterSync.ResendCharacter(player));
                        break;
                    case "all":
                        // the grids first: what it stands on is found before the support is told anew
                        var grids = CharacterSync.ResendGrids(player);
                        Context.Respond($"{player.DisplayName}: {CharacterSync.Reset(player)}; гридов вокруг отправлено заново: {grids.Sent} (под ногами не трогали: {grids.Kept}). Не помогло - !sync resend");
                        break;
                    default:
                        Context.Respond("Что делать: all, reset или resend");
                        break;
                }
            }
            catch (Exception e)
            {
                SentisGameplayImprovementsPlugin.Log.Error(e, "Sync " + what + " for " + player.DisplayName);
                Context.Respond("Не вышло: " + e.Message);
            }
        }
    }
}
