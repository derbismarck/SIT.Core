﻿using BepInEx.Configuration;
using BepInEx.Logging;

namespace SIT.Core.Configuration
{
    public class PluginConfigSettings
    {
        public ConfigFile Config { get; }
        public ManualLogSource Logger { get; }

        public static PluginConfigSettings Instance { get; private set; }

        public CoopConfigSettings CoopSettings { get; }

        public SITAdvancedSettings AdvancedSettings { get; }

        public PluginConfigSettings(ManualLogSource logger, ConfigFile config)
        {
            Logger = logger;
            Config = config;
            CoopSettings = new CoopConfigSettings(logger, config);
            AdvancedSettings = new SITAdvancedSettings(logger, config);
            Instance = this;
        }

        public void GetSettings()
        {

        }

        public class SITAdvancedSettings
        {
            public ConfigFile Config { get; }
            public ManualLogSource Logger { get; }

            public SITAdvancedSettings(ManualLogSource logger, ConfigFile config)
            {
                Logger = logger;
                Config = config;
                GetSettings();
            }

            public bool UseSITGarbageCollector { get; set; }

            public void GetSettings()
            {
                UseSITGarbageCollector = false;

            }
        }

        public class CoopConfigSettings
        {
            public ConfigFile Config { get; }
            public ManualLogSource Logger { get; }

            public CoopConfigSettings(ManualLogSource logger, ConfigFile config)
            {
                Logger = logger;
                Config = config;
                GetSettings();
            }

            public bool SETTING_DEBUGSpawnDronesOnServer { get; set; } = false;
            public bool SETTING_DEBUGShowPlayerList { get; set; } = false;
            public int SETTING_PlayerStateTickRateInMS { get; set; } = 333;
            public bool SETTING_HeadshotsAlwaysKill { get; set; } = true;
            public bool SETTING_ShowFeed { get; set; } = true;
            public int SITWebSocketPort { get; set; } = 6970;

            public bool AllPlayersSpawnTogether { get; set; } = true;
            public bool ArenaMode { get; set; } = false;
            public bool EnableAISpawnWaveSystem { get; set; } = true;

            public bool ForceHighPingMode { get; set; } = false;
            public bool RunThroughOnServerStop { get; set; } = true;

            public bool BotWavesDisableStopper { get; set; } = false;

            public void GetSettings()
            {
                SETTING_DEBUGSpawnDronesOnServer = Plugin.Instance.Config.Bind
                ("Coop", "ShowDronesOnServer", false, new ConfigDescription("Whether to spawn the client drones on the server -- for debugging")).Value;

                SETTING_DEBUGShowPlayerList = Plugin.Instance.Config.Bind
                   ("Coop", "ShowPlayerList", false, new ConfigDescription("Whether to show the player list on the GUI -- for debugging")).Value;

                SETTING_PlayerStateTickRateInMS = Plugin.Instance.Config.Bind
                  ("Coop", "PlayerStateTickRateInMS", 333, new ConfigDescription("The rate at which Player States will be synchronized")).Value;
                //if (SETTING_PlayerStateTickRateInMS > 0)
                //    SETTING_PlayerStateTickRateInMS = SETTING_PlayerStateTickRateInMS * -1;
                //else if (SETTING_PlayerStateTickRateInMS == 0)
                //    SETTING_PlayerStateTickRateInMS = -333;
                SETTING_PlayerStateTickRateInMS = 333;

                SETTING_HeadshotsAlwaysKill = false;

                SETTING_ShowFeed = Plugin.Instance.Config.Bind
                  ("Coop", "ShowFeed", true, new ConfigDescription("Enable the feed on the bottom right of the screen which shows player/bot spawns, kills, etc.")).Value;

                SITWebSocketPort = Plugin.Instance.Config.Bind("Coop", "SITPort", 6970, new ConfigDescription("SIT.Core Websocket Port DEFAULT = 6970")).Value;

                AllPlayersSpawnTogether = Plugin.Instance.Config.Bind
               ("Coop", "AllPlayersSpawnTogether", true, new ConfigDescription("Whether to spawn all players in the same place")).Value;

                ArenaMode = Plugin.Instance.Config.Bind
                ("Coop", "ArenaMode", false, new ConfigDescription("Arena Mode - For the meme's (DEBUG). Can SIT be less laggy than Live Tarkov in PvP?")).Value;

                EnableAISpawnWaveSystem = Plugin.Instance.Config.Bind("Coop", "EnableAISpawnWaveSystem", true
                        , new ConfigDescription("Whether to run the Wave Spawner System. If this is False. No AI will spawn. Useful for testing in a PvP only environment.")).Value;

                ForceHighPingMode = Plugin.Instance.Config.Bind("Coop", "ForceHighPingMode", false
                        , new ConfigDescription("Forces the High Ping Mode which allows some actions to not round-trip. This may be useful if you have large input lag")).Value;

                RunThroughOnServerStop = false;

                BotWavesDisableStopper = true;


                Logger.LogDebug($"SETTING_DEBUGSpawnDronesOnServer: {SETTING_DEBUGSpawnDronesOnServer}");
                Logger.LogDebug($"SETTING_DEBUGShowPlayerList: {SETTING_DEBUGShowPlayerList}");
                Logger.LogDebug($"SETTING_PlayerStateTickRateInMS: {SETTING_PlayerStateTickRateInMS}");
                Logger.LogDebug($"SETTING_HeadshotsAlwaysKill: {SETTING_HeadshotsAlwaysKill}");
                Logger.LogDebug($"SETTING_ShowFeed: {SETTING_ShowFeed}");
                Logger.LogDebug($"SITWebSocketPort: {SITWebSocketPort}");
                Logger.LogDebug($"AllPlayersSpawnTogether: {AllPlayersSpawnTogether}");
                Logger.LogDebug($"ArenaMode: {ArenaMode}");
                Logger.LogDebug($"ForceHighPingMode: {ForceHighPingMode}");
                Logger.LogDebug($"RunThroughOnServerStop: {RunThroughOnServerStop}");

                if (ArenaMode)
                {
                    Logger.LogInfo($"x!Arena Mode Activated!x");
                    AllPlayersSpawnTogether = false;
                    EnableAISpawnWaveSystem = false;
                }
            }
        }

    }
}
