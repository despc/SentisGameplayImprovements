using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace SentisGameplayImprovements.Commands
{
    /// <summary>
    /// Runtime admin controls. Usable from chat (for example: !toolradius welder 2)
    /// or the Torch command console; no server restart is required.
    /// </summary>
    public sealed class ShipToolRadiusCommands : CommandModule
    {
        [Command("toolradius", "Show current welder, grinder and drill work-radius multipliers")]
        [Permission(MyPromoteLevel.Admin)]
        public void Show()
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            Context.Respond($"Ship tool radii: welder={config.WelderRadiusMultiplier:0.###}, " +
                            $"grinder={config.GrinderRadiusMultiplier:0.###}, " +
                            $"drill={config.DrillRadiusMultiplier:0.###}; forward offset: welder={config.WelderForwardOffset:0.###} m, " +
                            $"grinder={config.GrinderForwardOffset:0.###} m");
        }

        [Command("toolradius welder", "Set ship-welder work-radius multiplier (min 0.1, no upper limit), live")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetWelder(float multiplier)
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            config.WelderRadiusMultiplier = multiplier;
            SentisGameplayImprovementsPlugin.SaveConfig();
            Context.Respond($"Welder work-radius multiplier set to {config.WelderRadiusMultiplier:0.###}");
        }

        [Command("toolradius grinder", "Set ship-grinder work-radius multiplier (0.1-10), live")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetGrinder(float multiplier)
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            config.GrinderRadiusMultiplier = multiplier;
            SentisGameplayImprovementsPlugin.SaveConfig();
            Context.Respond($"Grinder work-radius multiplier set to {config.GrinderRadiusMultiplier:0.###}");
        }

        [Command("toolradius welderoffset", "Move the ship-welder work sphere forward by this many metres (-10..10, negative - back), live")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetWelderOffset(float metres)
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            config.WelderForwardOffset = metres;
            SentisGameplayImprovementsPlugin.SaveConfig();
            Context.Respond($"Welder work sphere moved forward by {config.WelderForwardOffset:0.###} m");
        }

        [Command("toolradius grinderoffset", "Move the ship-grinder work sphere forward by this many metres (-10..10, negative - back), live")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetGrinderOffset(float metres)
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            config.GrinderForwardOffset = metres;
            SentisGameplayImprovementsPlugin.SaveConfig();
            Context.Respond($"Grinder work sphere moved forward by {config.GrinderForwardOffset:0.###} m");
        }

        [Command("toolradius drill", "Set ship-drill detection and voxel-cutout radius multiplier (0.1-10), live")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetDrill(float multiplier)
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            config.DrillRadiusMultiplier = multiplier;
            SentisGameplayImprovementsPlugin.SaveConfig();
            Context.Respond($"Drill work-radius multiplier set to {config.DrillRadiusMultiplier:0.###}");
        }
    }
}
