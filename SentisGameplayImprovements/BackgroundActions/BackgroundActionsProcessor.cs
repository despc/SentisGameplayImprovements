using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SentisGameplayImprovements.AllGridsActions;
using SentisGameplayImprovements.Assholes;

namespace SentisGameplayImprovements.BackgroundActions
{
    public class BackgroundActionsProcessor
    {
        public static FallInVoxelDetector FallInVoxelDetector = new FallInVoxelDetector();
        private GridAutoRenamer _autoRenamer = new GridAutoRenamer();
        private OnlineReward _onlineReward = new OnlineReward();
        private PvEGridChecker _pvEGridChecker = new PvEGridChecker();

        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private int counter = 0;

        public CancellationTokenSource CancellationTokenSource { get; set; }

        public void OnLoaded()
        {
            CancellationTokenSource = new CancellationTokenSource();
            Task.Run(CheckLoop);
            Task.Run(FastCheckLoop);
            Task.Run(NotSoFastFastCheckLoop);
        }

        public void OnUnloading()
        {
            CancellationTokenSource.Cancel();
        }

        public void FastCheckLoop()
        {
            try
            {
                Log.Info("FastCheckLoop started");
                while (!CancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        Thread.Sleep(1000);
                        Voxels.ProcessVoxelsContacts();
                    }
                    catch (Exception e)
                    {
                        Log.Error("FastCheckLoop Error", e);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("CheckLoop start Error", e);
            }
        }
        
        public void NotSoFastFastCheckLoop()
        {
            try
            {
                Log.Info("NotSoFastFastCheckLoop started");
                while (!CancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        Thread.Sleep(5000);
                        FloatingObjectsProcessor.CheckFloatingObjects();
                        FloatingObjectsProcessor.SpawnAccumulatedLoot();
                    }
                    catch (Exception e)
                    {
                        Log.Error("NotSoFastFastCheckLoop Error", e);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("NotSoFastFastCheckLoop start Error", e);
            }
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

                    if (SentisGameplayImprovementsPlugin.Config.EnabledPcuLimiter)
                    {
                        SentisGameplayImprovementsPlugin._limiter.CheckGrid(grid);
                    }

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