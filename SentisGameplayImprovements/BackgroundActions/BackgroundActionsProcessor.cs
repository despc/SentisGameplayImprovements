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
        private OnlineReward _onlineReward = new OnlineReward();

        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private int counter = 0;

        public CancellationTokenSource CancellationTokenSource { get; set; }

        public void OnLoaded()
        {
            CancellationTokenSource = new CancellationTokenSource();
            Task.Run(CheckLoop);
            Task.Run(FastCheckLoop);
            Task.Run(NotSoFastFastCheckLoop);
            Task.Run(FallThroughLoop);
            Task.Run(GameThreadSlicesLoop);
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
        
        /// <summary>Hands a slice of the fall-through check to the game thread every 100 ms.</summary>
        public void FallThroughLoop()
        {
            while (!CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(100);
                    if (SentisGameplayImprovementsPlugin.Config.AutoRestoreFromVoxel)
                        MyAPIGateway.Utilities.InvokeOnGameThread(CheckFallThrough);
                }
                catch (Exception e)
                {
                    Log.Error(e, "FallThroughLoop error");
                }
            }
        }

        private readonly GridSweep _gridSweep = new GridSweep();

        /// <summary>Hands a slice of the PCU limit check and of the grid sweep to the game thread every 100 ms.</summary>
        public void GameThreadSlicesLoop()
        {
            while (!CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(100);
                    var config = SentisGameplayImprovementsPlugin.Config;
                    if (config.EnabledPcuLimiter)
                        MyAPIGateway.Utilities.InvokeOnGameThread(SentisGameplayImprovementsPlugin._limiter.CheckSlice);
                    if (config.AutoRenameGrids || config.DisableNoOwner)
                        MyAPIGateway.Utilities.InvokeOnGameThread(_gridSweep.CheckSlice);
                }
                catch (Exception e)
                {
                    Log.Error(e, "GameThreadSlicesLoop error");
                }
            }
        }

        private static void CheckFallThrough()
        {
            try
            {
                FallInVoxelDetector.CheckSlice();
            }
            catch (Exception e)
            {
                Log.Error(e, "Fall-through check failed");
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

    }
}