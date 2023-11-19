using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using NLog;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using SentisGameplayImprovements.AllGridsActions;
using SentisGameplayImprovements.PveZone;
using SOPlugin.GUI;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Session;
using VRage.Network;
using VRageMath;

namespace SentisGameplayImprovements
{
    public class SentisGameplayImprovementsPlugin : TorchPluginBase, IWpfPlugin
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static TorchSessionManager SessionManager;
        private static Persistent<MainConfig> _config;
        public static MainConfig Config => _config.Data;
        public static Dictionary<long,long> stuckGrids = new Dictionary<long, long>();
        public static Random _random = new Random();
        public UserControl _control = null;
        public static SentisGameplayImprovementsPlugin Instance { get; private set; }
        public static PcuLimiter _limiter = new PcuLimiter();
        private AllGridsProcessor _allGridsProcessor = new AllGridsProcessor();
        public static ShieldApi SApi = new ShieldApi();

        public override void Init(ITorchBase torch)
        {
            Instance = this;
            Log.Info("Init Sentis Adventures Plugin");
            SetupConfig();
            SessionManager = Torch.Managers.GetManager<TorchSessionManager>();
            if (SessionManager == null)
                return;
            var configOverrideModIds = Config.OverrideModIds;
            MyEntities.OnEntityAdd += EntitiesObserver.MyEntitiesOnOnEntityAdd;
            MyEntities.OnEntityRemove += EntitiesObserver.MyEntitiesOnOnEntityRemove;
            SessionManager.SessionStateChanged += SessionManager_SessionStateChanged;
            
            if (string.IsNullOrEmpty(configOverrideModIds))
            {
                foreach (var modId in configOverrideModIds.Split(','))
                {
                    if (string.IsNullOrEmpty(configOverrideModIds))
                    {
                        try
                        {
                            var modIdL = Convert.ToUInt64(modId);
                            SessionManager.AddOverrideMod(modIdL);
                        }
                        catch (Exception e)
                        {
                            Log.Warn("Skip wrong modId " + modId);
                        }
                    }
                }
            }
        }

        private void SessionManager_SessionStateChanged(
            ITorchSession session,
            TorchSessionState newState)
        {
            if (newState == TorchSessionState.Unloading)
            {
                _allGridsProcessor.OnUnloading();
            }
            else
            {
                if (newState != TorchSessionState.Loaded)
                    return;
                _allGridsProcessor.OnLoaded();
                PvECore.Init();
                InitShieldApi();
            }
        }

        public async void InitShieldApi()
        {
            try
            {
                await Task.Delay(120000);
                SApi.Load();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public void UpdateGui()
        {
            try
            {
                // var npcCount = AllGridsProcessor.NpcLifetimeDict.Count;
                //
                Instance.UpdateUI((x) =>
                {
                    // var gui = x as ConfigGUI;
                    // gui.AdventuresInfo.Text =
                    //     $"Npc count in world: {npcCount}";
                });
            }
            catch (Exception e)
            {
                Log.Error(e, "WTF?");
            }
        }

        public void UpdateUI(Action<UserControl> action)
        {
            try
            {
                if (_control != null)
                {
                    _control.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            action.Invoke(_control);
                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "Something wrong in executing function:" + action);
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Cant UpdateUI");
            }
        }

        public override void Update()
        {
            if (MySandboxGame.Static.SimulationFrameCounter % 600 == 0)
            {
                Task.Run(UpdateGui);
            }
            
            if (MySandboxGame.Static.SimulationFrameCounter % 120 == 0)
            {
                Task.Run(ProcessVoxelsContacts);
            }
        }

        private static void ProcessVoxelsContacts()
        {
            foreach (var gridVoxelContactInfo in DamagePatch.contactInfo)
            {
                var entityId = gridVoxelContactInfo.Key;
                var cubeGrid = gridVoxelContactInfo.Value.MyCubeGrid;
                var contactCount = gridVoxelContactInfo.Value.Count;
                
                if (contactCount > 50)
                {
                    Log.Error("Entity  " + cubeGrid.DisplayName + " position " +
                              cubeGrid.PositionComp.GetPosition() + " contact count - " + contactCount);
                }

                if (contactCount < 800)
                {
                    continue;
                }

                if (stuckGrids.ContainsKey(entityId))
                {
                    if (stuckGrids[entityId] > 5)
                    {
                        if (!Vector3.IsZero(
                                MyGravityProviderSystem.CalculateNaturalGravityInPoint(cubeGrid.WorldMatrix
                                    .Translation)))
                        {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                            {
                                cubeGrid.Physics?.SetSpeeds(Vector3.Zero, Vector3.Zero);
                                cubeGrid.ConvertToStatic();
                            });
                            
                            try
                            {
                                MyMultiplayer.RaiseEvent(cubeGrid,
                                    x => x.ConvertToStatic);
                                foreach (var player in MySession.Static.Players.GetOnlinePlayers())
                                {
                                    MyMultiplayer.RaiseEvent(cubeGrid,
                                        x => x.ConvertToStatic, new EndpointId(player.Id.SteamId));
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "()Exception in RaiseEvent.");
                            }
                        }
                        else
                        {
                            try
                            {
                                Log.Info("Teleport stuck grid " + cubeGrid.DisplayName);
                                MatrixD worldMatrix = cubeGrid.WorldMatrix;
                                var position = cubeGrid.PositionComp.GetPosition();

                                var garbageLocation = new Vector3D(position.X + _random.Next(-10000, 10000),
                                    position.Y + _random.Next(-10000, 10000),
                                    position.Z + _random.Next(-10000, 10000));
                                worldMatrix.Translation = garbageLocation;
                                MyAPIGateway.Utilities.InvokeOnGameThread(() => { cubeGrid.Teleport(worldMatrix); });
                            }
                            catch (Exception e)
                            {
                                Log.Error("Exception in time try teleport entity to garbage", e);
                            }
                        }

                        stuckGrids.Remove(entityId);
                        continue;
                    }

                    stuckGrids[entityId] += 1;
                    continue;
                }

                stuckGrids[entityId] = 1;
            }

            DamagePatch.contactInfo.Clear();
        }

        public UserControl GetControl()
        {
            if (_control == null)
            {
                _control = new ConfigGUI();
            }

            return _control;
        }

        private void SetupConfig()
        {
            _config = Persistent<MainConfig>.Load(Path.Combine(StoragePath, "SentisGameplayImprovements.cfg"));
        }

        public override void Dispose()
        {
            _config.Save(Path.Combine(StoragePath, "SentisGameplayImprovements.cfg"));
            _allGridsProcessor.CancellationTokenSource.Cancel();
            base.Dispose();
        }
    }
}