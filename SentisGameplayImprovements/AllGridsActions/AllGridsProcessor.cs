using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace SentisGameplayImprovements.AllGridsActions
{
    public class AllGridsProcessor
    {
        public static FallInVoxelDetector FallInVoxelDetector = new FallInVoxelDetector();
        private GridAutoRenamer _autoRenamer = new GridAutoRenamer();
        private OnlineReward _onlineReward = new OnlineReward();
        private PvEGridChecker _pvEGridChecker = new PvEGridChecker();
        private NpcStationsPowerFix _npcStationsPowerFix = new NpcStationsPowerFix();
        
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private int counter = 0;

        public CancellationTokenSource CancellationTokenSource { get; set; }

        public void OnLoaded()
        {
            CancellationTokenSource = new CancellationTokenSource();
            Task.Run(CheckLoop);
        }

        public void OnUnloading()
        {
            CancellationTokenSource.Cancel();
        }

        public async void CheckLoop()
        {
            try
            {
                Log.Info("CheckLoop started");
                while (!CancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        counter++;
                        await Task.Delay(30000);

                        await Task.Run(CheckAllGrids);
                        if (counter % 5 == 0)
                        {
                            await Task.Run(() => _npcStationsPowerFix.RefillPowerStations());
                        }
                        await Task.Run(() =>
                        {
                            try
                            {
                                _onlineReward.RewardOnline();
                            }
                            catch (Exception e)
                            {
                                Log.Error("Async exception " + e);
                            }
                        });
                    }
                    catch (Exception e)
                    {
                        Log.Error("CheckLoop Error", e);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("CheckLoop start Error", e);
            }
        }        

        private void CheckAllGrids()
        {
            try
            {
                foreach (var grid in new HashSet<MyCubeGrid>(EntitiesObserver.MyCubeGrids))
                {
                    if (CancellationTokenSource.Token.IsCancellationRequested)
                        break;
                    if (grid == null)
                    {
                        continue;
                    }
                    SentisGameplayImprovementsPlugin._limiter.CheckGrid(grid);
                    if (SentisGameplayImprovementsPlugin.Config.AutoRestoreFromVoxel)
                    {
                        FallInVoxelDetector.CheckAndSavePos(grid);
                    }

                    if (SentisGameplayImprovementsPlugin.Config.AutoRenameGrids)
                    {
                        _autoRenamer.CheckAndRename(grid);
                    }

                    if (SentisGameplayImprovementsPlugin.Config.DisableNoOwner)
                    {
                        CheckNobodyOwner(grid);
                    }
                    if (SentisGameplayImprovementsPlugin.Config.PvEZoneEnabled)
                    {
                        _pvEGridChecker.CheckGridIsPvE(grid);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

        }

        private void CheckNobodyOwner(MyCubeGrid grid)
        {
            foreach (var myCubeBlock in grid.GetFatBlocks())
            {
                if (myCubeBlock.BlockDefinition.OwnershipIntegrityRatio != 0 && myCubeBlock.OwnerId == 0)
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        try
                        {
                            if (myCubeBlock is IMyFunctionalBlock)
                            {
                                ((IMyFunctionalBlock)myCubeBlock).Enabled = false;
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Warn("Prevent crash", e);
                        }
                    });
                }
            }
        }
    }
}