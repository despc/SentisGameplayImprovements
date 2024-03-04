using System;
using SentisGameplayImprovements.PveZone;
using SOPlugin.GUI;
using Torch;

namespace SentisGameplayImprovements
{
    public class MainConfig : ViewModel
    {

        //contracts
        private double _contractEscortMultiplier = 10;
        private double _contractAcquisitionMultiplier = 30; //Доставка
        private double _contractHaulingtMultiplier = 10; //Перевозка
        private double _contractRepairMultiplier = 10;
        
        //PCU limiter
        private bool _enabledPcuLimiter = true;
        private int _maxStaticGridPCU = 200000;
        private int _maxDinamycGridPCU = 30000;
        private bool _includeConnectedGrids = false;
        
        //alerts
        private bool _enableCheckBeacon = true;
        
        //NPC
        private String _pathToGrids = "C:\\SE\\Arrakis\\NPC";
        private String _guardiansNpcNames = "MKB_9000.sbc,MKB_9000.sbc";
        private int _guardDistanceSpawn = 300;
        
        //other
        private String _ignoreCleanupSubtypes = "Cargo";
        private String _overrideModIds = "";
        private int _disableAnyDamageAfterStartTime = 120;
        
        //Tweaks
        private bool _autoRenameGrids = false;
        private bool _enableRammingForStatic = true;
        private bool _allowPlayersUseConvertCommands = true;
        private bool _autoRestoreFromVoxel = false;
        private bool _disableNoOwner = false;
        private bool _disableTurretUpdate = false;
        private int _minimumMassForKineticDamage = 5000;
        private int _raycastLimit = -1;
        
        //explosions
        private bool _explosionTweaks = false;
        private bool _asyncExplosion = true;
        private float _warheadDamageMultiplier = 2.5f;
        private int _accelerationToDamage = 1000;  // взрыв боеприпаса или взрывчатки от удара об что-то, указывается ускорение объекта которое приводит к взрыву
        private float _explosivesDamage = 10;
        private float _projectileAmmoExplosionMultiplier = 0.1f;
        private float _missileAmmoExplosionMultiplier = 0.3f;
        private float _ammoExplosionRadius = 15f;
        private int _warheadExplosionDelay = 30;
        
        //Online Reward
        private bool _onlineRewardEnabled = true;
        private int _onlineRewardEachMinutes = 60;
        private string _onlineReward = "Ingot_Platinum=50;Ingot_Uranium=50";
        private string _onlineRewardMessage = "Спасибо что остаётесь с нами, награда за игру на сервере в вашем инвентаре.";
        
        //PvE Zone
        private bool _pveZoneEnabled = false;
        private bool _pveDamageFromNpc = false;
        private string _pveZonePos = "-73641.29:-623775.25:-1089014.23";
        private int _pveZoneRadius = 100000;
        
        // Factions&Reputations
        private bool _spawnsGuardsForAttackedFaction = false;
        
        // Voxels
        private bool _damageVoxelsFromExplosions = true;
        private int _noDamageFromVoxelsBeforeSpeed = 30;
        private bool _noDamageFromVoxelsIfNobodyNear = true;
        
        // Protection from assholes
        private bool _disableArtificialMass = false;
        private bool _disableDetachRotorFromTimer = false;
        private bool _disableLockLandingGearOnEnemyShips = false;
        private bool _disableLockLandingGearOnNPCShips = false;
        private bool _disableBuildBlockOnNPC = false;
        
        //loot
        private bool _enableLootSystem = false;
        private bool _customFloatingObjectsCleanup = false;
        private int _floatingObjectsLifetime = 5;
        private bool _connectorDestroyInsteadThrow = false;
        
        [DisplayTab(Name = "Online Reward Enabled", GroupName = "Online Reward", Tab = "Online Reward", Order = 0, Description = "Online Reward Enabled")]
        public bool OnlineRewardEnabled
        {
            get => _onlineRewardEnabled;
            set => SetValue(ref _onlineRewardEnabled, value);
        }
        
        [DisplayTab(Name = "PvE zone enabled", GroupName = "PvE Zone", Tab = "PvE Zone", Order = 0, Description = "PvE zone enabled")]
        public bool PvEZoneEnabled
        {
            get => _pveZoneEnabled;
            set
            {
                SetValue(ref _pveZoneEnabled, value);
                PvECore.ReloadSettings();
            }
        }

        [DisplayTab(Name = "Enable damage from NPC", GroupName = "PvE Zone", Tab = "PvE Zone", Order = 1, Description = "Enable damage from NPC")]
        public bool EnableDamageFromNPC
        {
            get => _pveDamageFromNpc;
            set => SetValue(ref _pveDamageFromNpc, value);
        }
        
        [DisplayTab(Name = "PvE zone position", GroupName = "PvE Zone", Tab = "PvE Zone", Order = 2, Description = "PvE zone position")]
        public String PveZonePos
        {
            get => _pveZonePos;
            set
            {
                SetValue(ref _pveZonePos, value);
                PvECore.ReloadSettings();
            }
        }

        [DisplayTab(Name = "PvE Zone Radius", GroupName = "PvE Zone", Tab = "PvE Zone", Order = 3, Description = "PvE Zone Radius")]
        public int PveZoneRadius
        {
            get => _pveZoneRadius;
            set
            {
                SetValue(ref _pveZoneRadius, value);
                PvECore.ReloadSettings();
            }
        }

        [DisplayTab(Name = "Reward for online", GroupName = "Online Reward", Tab = "Online Reward", Order = 1, Description = "Reward for online")]
        public String OnlineReward { get => _onlineReward; set => SetValue(ref _onlineReward, value); }
        
        [DisplayTab(Name = "Reward for online message", GroupName = "Online Reward", Tab = "Online Reward", Order = 1, Description = "Reward for online message")]
        public String OnlineRewardMessage { get => _onlineRewardMessage; set => SetValue(ref _onlineRewardMessage, value); }
        
        [DisplayTab(Name = "Online reward each N minutes", GroupName = "Online Reward", Tab = "Online Reward", Order = 2, Description = "Online reward each N minutes")]
        public int OnlineRewardEachMinutes { get => _onlineRewardEachMinutes; set => SetValue(ref _onlineRewardEachMinutes, value); }
        
        [DisplayTab(Name = "Dont clean with blocks", GroupName = "Other", Tab = "Other", Order = 0, Description = "Dont clean with blocks")]
        public String IgnoreCleanupSubtypes { get => _ignoreCleanupSubtypes; set => SetValue(ref _ignoreCleanupSubtypes, value); }

        [DisplayTab(Name = "Path to Garage", GroupName = "Other", Tab = "Other", Order = 0, Description = "Path to Garage")]
        public String PathToGrids { get => _pathToGrids; set => SetValue(ref _pathToGrids, value); }
        
        [DisplayTab(Name = "Guardians NPC names", GroupName = "NPC", Tab = "NPC", Order = 0, Description = "Guardians NPC names")]
        public String GuardiansNpcNames { get => _guardiansNpcNames; set => SetValue(ref _guardiansNpcNames, value); }
        
        [DisplayTab(Name = "Guardians spawn distance", GroupName = "NPC", Tab = "NPC", Order = 0, Description = "Guardians spawn distance")]
        public int GuardDistanceSpawn { get => _guardDistanceSpawn; set => SetValue(ref _guardDistanceSpawn, value); }
        
        [DisplayTab(Name = "Contract escort multiplier", GroupName = "Contracts", Tab = "Contracts", Order = 0, Description = "Contract escort reward multiplier")]
        public double ContractEscortMultiplier
        {
            get => _contractEscortMultiplier;
            set => SetValue(ref _contractEscortMultiplier, value);
        }
        [DisplayTab(Name = "Contract acquisition multiplier", GroupName = "Contracts", Tab = "Contracts", Order = 0, Description = "Contract acquisition reward multiplier")]
        public double ContractAcquisitionMultiplier
        {
            get => _contractAcquisitionMultiplier;
            set => SetValue(ref _contractAcquisitionMultiplier, value);
        }
        [DisplayTab(Name = "Contract hauling multiplier", GroupName = "Contracts", Tab = "Contracts", Order = 0, Description = "Contract hauling reward multiplier")]
        public double ContractHaulingtMultiplier
        {
            get => _contractHaulingtMultiplier;
            set => SetValue(ref _contractHaulingtMultiplier, value);
        }
        [DisplayTab(Name = "Contract repair multiplier", GroupName = "Contracts", Tab = "Contracts", Order = 0, Description = "Contract repair reward multiplier")]
        public double ContractRepairMultiplier
        {
            get => _contractRepairMultiplier;
            set => SetValue(ref _contractRepairMultiplier, value);
        }

        [DisplayTab(Name = "Enabled PCU Limiter", GroupName = "PCU limiter", Tab = "PCU limiter", Order = 0, Description = "Enable PCU Limiter")]
        public bool EnabledPcuLimiter
        {
            get => _enabledPcuLimiter;
            set => SetValue(ref _enabledPcuLimiter, value);
        }
        
        [DisplayTab(Name = "Enabled check Beacon", GroupName = "PCU limiter", Tab = "PCU limiter", Order = 0, Description = "Enabled check Beacon")]
        public bool EnableCheckBeacon
        {
            get => _enableCheckBeacon;
            set => SetValue(ref _enableCheckBeacon, value);
        }
        
        [DisplayTab(Name = "Static structures receive damage from ramming", GroupName = "Performance", Tab = "Performance", Order = 0, Description = "Static structures receive damage from ramming")]
        public bool StaticRamming
        {
            get => _enableRammingForStatic;
            set => SetValue(ref _enableRammingForStatic, value);
        }
        
        [DisplayTab(Name = "Allow Players Use Convert Commands", GroupName = "Tweaks", Tab = "Tweaks", Order = 0, Description = "Allow Players Use Convert Commands")]
        public bool AllowPlayersUseConvertCommands
        {
            get => _allowPlayersUseConvertCommands;
            set => SetValue(ref _allowPlayersUseConvertCommands, value);
        }
        
        [DisplayTab(Name = "Max static grid PCU", GroupName = "PCU limiter", Tab = "PCU limiter", Order = 0, Description = "Max static grid PCU")]
        public int MaxStaticGridPCU
        {
            get => _maxStaticGridPCU;
            set => SetValue(ref _maxStaticGridPCU, value);
        }

        [DisplayTab(Name = "Max dynamic grid PCU", GroupName = "PCU limiter", Tab = "PCU limiter", Order = 0, Description = "Max dynamic grid PCU")]
        public int MaxDinamycGridPCU
        {
            get => _maxDinamycGridPCU;
            set => SetValue(ref _maxDinamycGridPCU, value);
        }
        
        [DisplayTab(Name = "Minimum mass for kinetic damage", GroupName = "Tweaks", Tab = "Tweaks", Order = 0, Description = "Minimum mass for kinetic damage")]
        public int MinimumMassForKineticDamage
        {
            get => _minimumMassForKineticDamage;
            set => SetValue(ref _minimumMassForKineticDamage, value);
        }
        
        [DisplayTab(Name = "Raycast Limit", GroupName = "Tweaks", Tab = "Tweaks", Order = 0, Description = "Raycast Limit. Need Restart")]
        public int RaycastLimit
        {
            get => _raycastLimit;
            set => SetValue(ref _raycastLimit, value);
        }
        
        [DisplayTab(Name = "No damage from voxels before speed", GroupName = "Voxels", Tab = "Voxels", Order = 0, Description = "No damage from voxels before speed")]
        public int NoDamageFromVoxelsBeforeSpeed
        {
            get => _noDamageFromVoxelsBeforeSpeed;
            set => SetValue(ref _noDamageFromVoxelsBeforeSpeed, value);
        }
        
        [DisplayTab(Name = "No Damage From Voxels If Nobody Near", GroupName = "Voxels", Tab = "Voxels", Order = 0, Description = "No Damage From Voxels If Nobody Near")]
        public bool NoDamageFromVoxelsIfNobodyNear
        {
            get => _noDamageFromVoxelsIfNobodyNear;
            set => SetValue(ref _noDamageFromVoxelsIfNobodyNear, value);
        }

        [DisplayTab(Name = "Include connected grids", GroupName = "PCU limiter", Tab = "PCU limiter", Order = 0, Description = "Include grinds connected with CONNECTOR")]
        public bool IncludeConnectedGrids
        {
            get => _includeConnectedGrids;
            set => SetValue(ref _includeConnectedGrids, value);
        }
        
        [DisplayTab(Name = "Disable no owner", GroupName = "Tweaks", Tab = "Tweaks", Order = 0, Description = "Disable no owner")]
        public bool DisableNoOwner  
        {
            get => _disableNoOwner;
            set => SetValue(ref _disableNoOwner, value);
        }

        [DisplayTab(Name = "Auto Restore From Voxel", GroupName = "Tweaks", Tab = "Tweaks", Order = 0, Description = "Grids inside voxels rollback for 15sec")]
        public bool AutoRestoreFromVoxel
        {
            get => _autoRestoreFromVoxel;
            set => SetValue(ref _autoRestoreFromVoxel, value);
        }
        [DisplayTab(Name = "Client Only Mods", GroupName = "Other", Tab = "Other", Order = 0, Description = "Client Only Mods splitted with comma")]
        public String OverrideModIds { get => _overrideModIds; set => SetValue(ref _overrideModIds, value); }
        
        [DisplayTab(Name = "Auto Rename Grids", GroupName = "Tweaks", Tab = "Tweaks", Order = 0, Description = "Auto Rename Grids")]
        public bool AutoRenameGrids
        {
            get => _autoRenameGrids;
            set => SetValue(ref _autoRenameGrids, value);
        }
        
        [DisplayTab(Name = "Disable any damage after start time(sec)", GroupName = "Tweaks", Tab = "Tweaks", Order = 0, Description = "Disable any damage after start time(sec)")]
        public int DisableAnyDamageAfterStartTime
        {
            get => _disableAnyDamageAfterStartTime;
            set => SetValue(ref _disableAnyDamageAfterStartTime, value);
        }
        
        
        [DisplayTab(Name = "Async explosion", GroupName = "Explosions", Tab = "Explosions", Order = 8, Description = "Async explosion")]
        public bool AsyncExplosion { get => _asyncExplosion; set => SetValue(ref _asyncExplosion, value); }
        
        [DisplayTab(Name = "Explosion Tweaks Enabled", GroupName = "Explosions", Tab = "Explosions", Order = -1, Description = "Explosion Tweaks Enabled")]
        public bool ExplosionTweaks { get => _explosionTweaks; set => SetValue(ref _explosionTweaks, value); }
        
        [DisplayTab(Name = "Explosives damage", GroupName = "Explosions", Tab = "Explosions", Order = 0, Description = "Explosives damage")]
        public float ExplosivesDamage { get => _explosivesDamage; set => SetValue(ref _explosivesDamage, value); }
        
        [DisplayTab(Name = "Projectile Ammo Explosion Multiplier", GroupName = "Explosions", Tab = "Explosions", Order = 0, Description = "Projectile Ammo Explosion Multiplier")]
        public float ProjectileAmmoExplosionMultiplier { get => _projectileAmmoExplosionMultiplier; set => SetValue(ref _projectileAmmoExplosionMultiplier, value); }
        
        [DisplayTab(Name = "Missile Ammo Explosion Multiplier", GroupName = "Explosions", Tab = "Explosions", Order = 0, Description = "Missile Ammo Explosion Multiplier")]
        public float MissileAmmoExplosionMultiplier { get => _missileAmmoExplosionMultiplier; set => SetValue(ref _missileAmmoExplosionMultiplier, value); }
        [DisplayTab(Name = "Ammo Explosion Radius", GroupName = "Explosions", Tab = "Explosions", Order = 0, Description = "Ammo Explosion Radius")]
        public float AmmoExplosionRadius { get => _ammoExplosionRadius; set => SetValue(ref _ammoExplosionRadius, value); }
                
        [DisplayTab(Name = "Warhead Explosion Delay", GroupName = "Explosions", Tab = "Explosions", Order = 0, Description = "Warhead Explosion Delay")]
        public int WarheadExplosionDelay { get => _warheadExplosionDelay; set => SetValue(ref _warheadExplosionDelay, value); }
        
        [DisplayTab(Name = "Warhead damage multiplier", GroupName = "Explosions", Tab = "Explosions", Order = 0, Description = "Warhead damage multiplier")]
        public float WarheadDamageMultiplier { get => _warheadDamageMultiplier; set => SetValue(ref _warheadDamageMultiplier, value); }
        
        [DisplayTab(Name = "Acceleration to Damage", GroupName = "Explosions", Tab = "Explosions", Order = 0, Description = "Acceleration to Damage")]
        public int AccelerationToDamage { get => _accelerationToDamage; set => SetValue(ref _accelerationToDamage, value); }

        [DisplayTab(Name = "Disable Vanilla Turret Update", GroupName = "Performance", Tab = "Performance", Order = 0, Description = "Disable Vanilla Turret Update if all turrets on WeaponCore")]
        public bool DisableTurretUpdate
        {
            get => _disableTurretUpdate;
            set => SetValue(ref _disableTurretUpdate, value);
        }
          
        // Factions&Reputations
        [DisplayTab(Name = "Spawns guards for attacked faction", GroupName = "Factions&Reputations", Tab = "Factions&Reputations", Order = 0, Description = "Spawns guards for attacked faction")]
        public bool SpawnsGuardsForAttackedFaction
        {
            get => _spawnsGuardsForAttackedFaction;
            set => SetValue(ref _spawnsGuardsForAttackedFaction, value);
        }
        
        // Protection from assholes
        [DisplayTab(Name = "Disable artificial mass", GroupName = "Protection from assholes", Tab = "Protection from assholes", Order = 99, Description = "Disable artificial mass")]
        public bool DisableArtificialMass
        {
            get => _disableArtificialMass;
            set => SetValue(ref _disableArtificialMass, value);
        }
        
        [DisplayTab(Name = "Disable lock landing gear on enemy", GroupName = "Protection from assholes", Tab = "Protection from assholes", Order = 99, Description = "Disable lock landing gear on enemy")]
        public bool DisableLockLandingGearOnEnemyShips
        {
            get => _disableLockLandingGearOnEnemyShips;
            set => SetValue(ref _disableLockLandingGearOnEnemyShips, value);
        }
        
        [DisplayTab(Name = "Disable lock landing gear on NPC", GroupName = "Protection from assholes", Tab = "Protection from assholes", Order = 99, Description = "Disable lock landing gear on NPC")]
        public bool DisableLockLandingGearOnNPCShips
        {
            get => _disableLockLandingGearOnNPCShips;
            set => SetValue(ref _disableLockLandingGearOnNPCShips, value);
        }

        [DisplayTab(Name = "Disable build block on NPC", GroupName = "Protection from assholes", Tab = "Protection from assholes", Order = 99, Description = "Disable build block on NPC")]
        public bool DisableBuildBlockOnNPC
        {
            get => _disableBuildBlockOnNPC;
            set => SetValue(ref _disableBuildBlockOnNPC, value);
        }
        
        // Protection from assholes
        [DisplayTab(Name = "Damage voxels from explosions", GroupName = "Voxels", Tab = "Voxels", Order = 0, Description = "Damage voxels from explosions")]
        public bool DamageVoxelsFromExplosions
        {
            get => _damageVoxelsFromExplosions;
            set => SetValue(ref _damageVoxelsFromExplosions, value);
        }
        
        [DisplayTab(Name = "Loot system enabled", GroupName = "Loot system", Tab = "Loot system", Order = 0, Description = "Loot system enabled")]
        public bool LootSystemEnabled
        {
            get => _enableLootSystem;
            set => SetValue(ref _enableLootSystem, value);
        }
        
        [DisplayTab(Name = "Custom floating objects cleanup", GroupName = "Loot system", Tab = "Loot system", Order = 1, Description = "Custom floating objects cleanup")]
        public bool CustomFloatingObjectsCleanup
        {
            get => _customFloatingObjectsCleanup;
            set => SetValue(ref _customFloatingObjectsCleanup, value);
        }
        
        [DisplayTab(Name = "Floating objects lifetime", GroupName = "Loot system", Tab = "Loot system", Order = 2, Description = "Floating objects lifetime")]
        public int FloatingObjectsLifetime
        {
            get => _floatingObjectsLifetime;
            set => SetValue(ref _floatingObjectsLifetime, value);
        }
        
        [DisplayTab(Name = "Connector destroy instead throw", GroupName = "Loot system", Tab = "Loot system", Order = 1, Description = "Connector destroy instead throw (only ores and ingots)")]
        public bool ConnectorDestroyInsteadThrow
        {
            get => _connectorDestroyInsteadThrow;
            set => SetValue(ref _connectorDestroyInsteadThrow, value);
        }
    }
}