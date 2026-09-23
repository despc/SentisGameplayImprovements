using System;
using System.Collections.Generic;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SentisGameplayImprovements.AllGridsActions;

namespace SentisGameplayImprovements.BackgroundActions
{
    /// <summary>
    /// Every <see cref="PassSeconds"/> seconds every grid is looked at once, on the game thread and spread over
    /// frames (<see cref="CheckSlice"/>, at most <see cref="BlocksPerSlice"/> blocks a call):
    /// <list type="bullet">
    /// <item><c>AutoRenameGrids</c> - a grid with the game's default name gets its owner's;</item>
    /// <item><c>DisableNoOwner</c> - a functional block nobody owns (of the kind that has an owner) is switched off.</item>
    /// </list>
    /// It used to run on a pool thread, reading every block of the world while the game changed them and sending
    /// the game thread a call for every unowned block, switched off already or not, each time.
    /// </summary>
    public class GridSweep
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public const double PassSeconds = 30;
        private const int BlocksPerSlice = 20000;
        private const int GridCost = 10;

        private readonly GridAutoRenamer _renamer = new GridAutoRenamer();
        private readonly List<MyCubeGrid> _queue = new List<MyCubeGrid>();
        private int _cursor;
        private DateTime _lastPass = DateTime.UtcNow;

        public void CheckSlice()
        {
            var config = SentisGameplayImprovementsPlugin.Config;
            if (!config.AutoRenameGrids && !config.DisableNoOwner) return;
            if (_cursor >= _queue.Count)
            {
                if ((DateTime.UtcNow - _lastPass).TotalSeconds < PassSeconds) return;
                _lastPass = DateTime.UtcNow;
                _queue.Clear();
                _cursor = 0;
                _queue.AddRange(EntitiesObserver.MyCubeGrids);
            }

            var cost = 0;
            while (_cursor < _queue.Count && cost < BlocksPerSlice)
            {
                var grid = _queue[_cursor++];
                cost += GridCost;
                if (grid == null || grid.MarkedForClose || grid.Closed) continue;
                try
                {
                    if (config.AutoRenameGrids) _renamer.CheckAndRename(grid);
                    if (config.DisableNoOwner) cost += SwitchOffUnowned(grid);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Grid sweep failed on " + grid.DisplayName);
                }
            }
        }

        /// <summary>Switches off the grid's functional blocks that have no owner; returns how many blocks it looked at.</summary>
        public static int SwitchOffUnowned(MyCubeGrid grid)
        {
            var blocks = grid.GetFatBlocks();
            foreach (var block in blocks)
            {
                if (block.BlockDefinition.OwnershipIntegrityRatio != 0 && block.OwnerId == 0 &&
                    block is IMyFunctionalBlock functional && functional.Enabled)
                    functional.Enabled = false;
            }
            return blocks.Count;
        }
    }
}
