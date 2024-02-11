using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;
using NLog;
using Sandbox.Game.Entities;
using SentisGameplayImprovements.AllGridsActions;
using SentisGameplayImprovements.DelayedLogic;
using SentisGameplayImprovements.PveZone;
using SOPlugin.GUI;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Session;

namespace SentisGameplayImprovements
{
    public class SentisGameplayImprovementsPlugin : TorchPluginBase, IWpfPlugin
    {
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private static TorchSessionManager SessionManager;
        private static Persistent<MainConfig> _config;
        public static MainConfig Config => _config.Data;
        public UserControl _control = null;
        public static SentisGameplayImprovementsPlugin Instance { get; private set; }
        public static PcuLimiter _limiter = new PcuLimiter();
        private AllGridsProcessor _allGridsProcessor = new AllGridsProcessor();
        public DelayedProcessor DelayedProcessor = new DelayedProcessor();
        public static ShieldApi SApi = new ShieldApi();

        public override void Init(ITorchBase torch)
        {
            Instance = this;
            DelayedProcessor.Instance = DelayedProcessor;
            Log.Info("Init Sentis Gameplay Improvements Plugin");
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
                DelayedProcessor.OnUnloading();
            }
            else
            {
                if (newState != TorchSessionState.Loaded)
                    return;
                _allGridsProcessor.OnLoaded();
                PvECore.Init();
                DamagePatch.Init();
                DelayedProcessor.OnLoaded();
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