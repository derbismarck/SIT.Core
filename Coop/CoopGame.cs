using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Bots;
using EFT.Game.Spawning;
using EFT.InputSystem;
using EFT.Interactive;
using EFT.UI;
using EFT.Weather;
using JsonType;
using SIT.Coop.Core.Matchmaker;
using SIT.Coop.Core.Player;
using SIT.Core.Configuration;
using SIT.Core.Coop.FreeCamera;
using SIT.Core.Core;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace SIT.Core.Coop
{
    /// <summary>
    /// A custom Game Type
    /// </summary>
    public sealed class CoopGame : BaseLocalGame<GamePlayerOwner>, IBotGame
    {

        public ISession BackEndSession { get { return PatchConstants.BackEndSession; } }

        BotControllerClass IBotGame.BotsController
        {
            get
            {
                if (botControllerClass == null)
                {
                    botControllerClass = (BotControllerClass)ReflectionHelpers.GetFieldFromTypeByFieldType(base.GetType(), typeof(BotControllerClass)).GetValue(this);
                }
                return botControllerClass;
            }
        }

        private static BotControllerClass botControllerClass;

        public BotControllerClass PBotsController
        {
            get
            {
                if (botControllerClass == null)
                {
                    botControllerClass = (BotControllerClass)ReflectionHelpers.GetFieldFromTypeByFieldType(base.GetType(), typeof(BotControllerClass)).GetValue(this);
                }
                return botControllerClass;
            }
        }

        public IWeatherCurve WeatherCurve
        {
            get
            {
                if (WeatherController.Instance != null)
                    return new WeatherCurve(new WeatherClass[1] { new WeatherClass() });

                return null;
            }
        }

        private static ManualLogSource Logger;


        // Token: 0x0600844F RID: 33871 RVA: 0x0025D580 File Offset: 0x0025B780
        internal static CoopGame Create(
            InputTree inputTree
            , Profile profile
            , GameDateTime backendDateTime
            , Insurance insurance
            , MenuUI menuUI
            , CommonUI commonUI
            , PreloaderUI preloaderUI
            , GameUI gameUI
            , LocationSettings.Location location
            , TimeAndWeatherSettings timeAndWeather
            , WavesSettings wavesSettings
            , EDateTime dateTime
            , Callback<ExitStatus, TimeSpan, ClientMetrics> callback
            , float fixedDeltaTime
            , EUpdateQueue updateQueue
            , ISession backEndSession
            , TimeSpan sessionTime)
        {
            botControllerClass = null;

            Logger = BepInEx.Logging.Logger.CreateLogSource("Coop Game Mode");
            Logger.LogInfo("CoopGame.Create");

            if (wavesSettings.BotAmount == EBotAmount.NoBots && MatchmakerAcceptPatches.IsServer)
                wavesSettings.BotAmount = EBotAmount.Medium;

            CoopGame coopGame = BaseLocalGame<GamePlayerOwner>
                .smethod_0<CoopGame>(inputTree, profile, backendDateTime, insurance, menuUI, commonUI, preloaderUI, gameUI, location, timeAndWeather, wavesSettings, dateTime
                , callback, fixedDeltaTime, updateQueue, backEndSession, new TimeSpan?(sessionTime));

            // Non Waves Scenario setup
            coopGame.nonWavesSpawnScenario_0 = (NonWavesSpawnScenario)ReflectionHelpers.GetMethodForType(typeof(NonWavesSpawnScenario), "smethod_0").Invoke
                (null, new object[] { coopGame, location, coopGame.PBotsController });
            coopGame.nonWavesSpawnScenario_0.ImplementWaveSettings(wavesSettings);

            // Waves Scenario setup
            coopGame.wavesSpawnScenario_0 = (WavesSpawnScenario)ReflectionHelpers.GetMethodForType(typeof(WavesSpawnScenario), "smethod_0").Invoke
                (null, new object[] {
                    coopGame.gameObject
                    , location.waves
                    , new Action<Wave>((wave) => coopGame.PBotsController.ActivateBotsByWave(wave))
                    , location });

            var bosswavemanagerValue = ReflectionHelpers.GetMethodForType(typeof(BossWaveManager), "smethod_0").Invoke
                (null, new object[] { location.BossLocationSpawn, new Action<BossLocationSpawn>((bossWave) => { coopGame.PBotsController.ActivateBotsByWave(bossWave); }) });
            //(null, new object[] { location.BossLocationSpawn, new Action<BossLocationSpawn>((bossWave) => { coopGame.PBotsController.ActivateBotsByWave(bossWave); }) });
            ReflectionHelpers.GetFieldFromTypeByFieldType(typeof(CoopGame), typeof(BossWaveManager)).SetValue(coopGame, bosswavemanagerValue);
            coopGame.BossWaveManager = bosswavemanagerValue as BossWaveManager;

            coopGame.StartCoroutine(coopGame.ReplicatedWeather());
            //coopGame.StartCoroutine(coopGame.DebugObjects());
            coopGame.func_1 = (EFT.Player player) => GamePlayerOwner.Create<GamePlayerOwner>(player, inputTree, insurance, backEndSession, commonUI, preloaderUI, gameUI, coopGame.GameDateTime, location);

            //GCHelpers.EnableGC();
            //coopGame.timeAndWeatherSettings = timeAndWeather;

            return coopGame;
        }

        //BossLocationSpawn[] bossSpawnAdjustments;

        public void CreateCoopGameComponent()
        {
            var coopGameComponent = CoopGameComponent.GetCoopGameComponent();
            if (coopGameComponent != null)
            {
                GameObject.Destroy(coopGameComponent);
            }

            if (CoopPatches.CoopGameComponentParent == null)
                CoopPatches.CoopGameComponentParent = new GameObject("CoopGameComponentParent");

            coopGameComponent = CoopPatches.CoopGameComponentParent.GetOrAddComponent<CoopGameComponent>();
            coopGameComponent.LocalGameInstance = this;

            //coopGameComponent = gameWorld.GetOrAddComponent<CoopGameComponent>();
            if (!string.IsNullOrEmpty(MatchmakerAcceptPatches.GetGroupId()))
                coopGameComponent.ServerId = MatchmakerAcceptPatches.GetGroupId();
            else
            {
                GameObject.Destroy(coopGameComponent);
                coopGameComponent = null;
                Logger.LogError("========== ERROR = COOP ========================");
                Logger.LogError("No Server Id found, Deleting Coop Game Component");
                Logger.LogError("================================================");
                throw new Exception("No Server Id found");
            }

            if (!MatchmakerAcceptPatches.IsClient)
                StartCoroutine(HostPinger());

            if (!MatchmakerAcceptPatches.IsClient)
                StartCoroutine(GameTimerSync());


            StartCoroutine(ClientLoadingPinger());

        }

        private IEnumerator ClientLoadingPinger()
        {
            var waitSeconds = new WaitForSeconds(1f);

            while (true)
            {
                if (PlayerOwner == null)
                    yield return waitSeconds;

                // Send a message of nothing to keep the Socket Alive whilst loading
                AkiBackendCommunication.Instance.PostDownWebSocketImmediately("");

                yield return waitSeconds;

            }
        }

        private IEnumerator DebugObjects()
        {
            var waitSeconds = new WaitForSeconds(10f);

            while (true)
            {
                if (PlayerOwner == null)
                    yield return waitSeconds;
                //foreach(var o in  .FindObjectsOfTypeAll(typeof(GameObject)))
                //{
                //   Logger.LogInfo(o.ToString());
                //}
                foreach (var c in PlayerOwner.Player.GetComponents(typeof(GameObject)))
                {
                    Logger.LogInfo(c.ToString());
                }
                yield return waitSeconds;

            }
        }

        private IEnumerator HostPinger()
        {
            var waitSeconds = new WaitForSeconds(1f);

            while (true)
            {
                yield return waitSeconds;
                AkiBackendCommunication.Instance.SendDataToPool("{ \"HostPing\": " + DateTime.Now.Ticks + " }");
            }
        }

        private IEnumerator GameTimerSync()
        {
            var waitSeconds = new WaitForSeconds(5f);

            while (true)
            {
                yield return waitSeconds;

                if (GameTimer.SessionTime.HasValue)
                    AkiBackendCommunication.Instance.SendDataToPool("{ \"RaidTimer\": " + GameTimer.SessionTime.Value.Ticks + " }");
            }
        }

        private WeatherDebug WeatherClear { get; set; } = new WeatherDebug()
        {
            Enabled = true,
            CloudDensity = -0.7f,
            Fog = 0,
            LightningThunderProbability = 0,
            MBOITFog = false,
            Rain = 0,
            ScatterGreyscale = 0,
            Temperature = 24,
            WindMagnitude = 0,
            WindDirection = WeatherDebug.Direction.North,
            TopWindDirection = Vector2.up
        };

        private WeatherDebug WeatherSlightCloud { get; } = new WeatherDebug()
        {
            Enabled = true,
            CloudDensity = -0.35f,
            Fog = 0.004f,
            LightningThunderProbability = 0,
            MBOITFog = false,
            Rain = 0,
            ScatterGreyscale = 0,
            Temperature = 24,
            WindDirection = WeatherDebug.Direction.North,
            WindMagnitude = 0.02f,
            TopWindDirection = Vector2.up
        };

        private WeatherDebug WeatherCloud { get; } = new WeatherDebug()
        {
            Enabled = true,
            CloudDensity = 0f,
            Fog = 0.01f,
            LightningThunderProbability = 0,
            MBOITFog = false,
            Rain = 0,
            ScatterGreyscale = 0,
            Temperature = 20,
            WindDirection = WeatherDebug.Direction.North,
            WindMagnitude = 0.02f,
            TopWindDirection = Vector2.up
        };

        private WeatherDebug WeatherRainDrizzle { get; } = new WeatherDebug()
        {
            Enabled = true,
            CloudDensity = 0f,
            Fog = 0.01f,
            LightningThunderProbability = 0,
            MBOITFog = false,
            Rain = 0.01f,
            ScatterGreyscale = 0,
            Temperature = 19,
            WindDirection = WeatherDebug.Direction.North,
            WindMagnitude = 0.02f,
            TopWindDirection = Vector2.up
        };

        //private TimeAndWeatherSettings timeAndWeatherSettings { get; set; }


        public IEnumerator ReplicatedWeather()
        {
            var waitSeconds = new WaitForSeconds(15f);
            //Logger.LogDebug($"ReplicatedWeather:timeAndWeatherSettings:HourOfDay:{timeAndWeatherSettings.HourOfDay}");

            while (true)
            {
                yield return waitSeconds;
                if (WeatherController.Instance != null)
                {
                    WeatherController.Instance.SetWeatherForce(new WeatherClass() { });

                    Logger.LogDebug($"ReplicatedWeather:EscapeDateTime:{GameTimer.EscapeDateTime}");
                    Logger.LogDebug($"ReplicatedWeather:PastTime:{GameTimer.PastTime}");
                    Logger.LogDebug($"ReplicatedWeather:SessionTime:{GameTimer.SessionTime}");
                    Logger.LogDebug($"ReplicatedWeather:StartDateTime:{GameTimer.StartDateTime}");


                    WeatherController.Instance.WeatherDebug.Enabled = true;
                    WeatherController.Instance.WeatherDebug.CloudDensity = -0.35f;
                    WeatherController.Instance.WeatherDebug.Fog = 0;
                    WeatherController.Instance.WeatherDebug.LightningThunderProbability = 0;
                    WeatherController.Instance.WeatherDebug.MBOITFog = false;
                    WeatherController.Instance.WeatherDebug.Rain = 0;
                    WeatherController.Instance.WeatherDebug.ScatterGreyscale = 0;
                    WeatherController.Instance.WeatherDebug.Temperature = 24;
                    WeatherController.Instance.WeatherDebug.WindDirection = WeatherDebug.Direction.North;
                    WeatherController.Instance.WeatherDebug.TopWindDirection = Vector2.up;

                    // ----------------------------------------------------------------------------------
                    // Create synchronized time

                    //var hourOfDay = WeatherController.Instance.

                    //WeatherController.Instance.WeatherDebug.SetHour(
                    //    timeAndWeatherSettings.HourOfDay >= 6 && timeAndWeatherSettings.HourOfDay <= 8
                    //    ? 7
                    //    : timeAndWeatherSettings.HourOfDay >= 9 && timeAndWeatherSettings.HourOfDay <= 18
                    //    ? 12
                    //    : timeAndWeatherSettings.HourOfDay >= 19 && timeAndWeatherSettings.HourOfDay <= 21
                    //    ? 20
                    //    : 3
                    //    );
                }
            }
        }


      

        public Dictionary<string, EFT.Player> Bots { get; set; } = new Dictionary<string, EFT.Player>();

        private async Task<LocalPlayer> CreatePhysicalBot(Profile profile, Vector3 position)
        {
            if (MatchmakerAcceptPatches.IsClient)
                return null;

            Logger.LogDebug($"CreatePhysicalBot: {profile.ProfileId}");
            if (Bots != null && Bots.Count(x => x.Value != null && x.Value.PlayerHealthController.IsAlive) >= MaxBotCount)
                return null;

            LocalPlayer localPlayer;
            if (!base.Status.IsRunned())
            {
                localPlayer = null;
            }
            else if (this.Bots.ContainsKey(profile.Id))
            {
                localPlayer = null;
            }
            else
            {
                int num = 999 + Bots.Count;
                profile.SetSpawnedInSession(profile.Info.Side == EPlayerSide.Savage);
             
                localPlayer
                   = (CoopPlayer)(await CoopPlayer.Create(
                       num
                       , position
                       , Quaternion.identity
                       , "Player"
                       , ""
                       , EPointOfView.ThirdPerson
                       , profile
                       , true
                       , base.UpdateQueue
                       , EFT.Player.EUpdateMode.Auto
                       , EFT.Player.EUpdateMode.Auto
                       , BackendConfigManager.Config.CharacterController.BotPlayerMode
                   , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseSensitivity
                   , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseAimingSensitivity
                    , FilterCustomizationClass1.Default));
                localPlayer.Location = base.Location_0.Id;
                if (this.Bots.ContainsKey(localPlayer.ProfileId))
                {
                    GameObject.Destroy(localPlayer);
                    return null;
                }
                else
                {
                    this.Bots.Add(localPlayer.ProfileId, localPlayer);
                }


                //GCHelpers.EnableGC();
                ////GCHelpers.Collect(true);
                //GCHelpers.DisableGC();
                //GC.Collect(4, GCCollectionMode.Forced, false, false);

            }
            return localPlayer;
        }


        public async Task<LocalPlayer> CreatePhysicalPlayer(int playerId, Vector3 position, Quaternion rotation, string layerName, string prefix, EPointOfView pointOfView, Profile profile, bool aiControl, EUpdateQueue updateQueue, EFT.Player.EUpdateMode armsUpdateMode, EFT.Player.EUpdateMode bodyUpdateMode, CharacterControllerSpawner.Mode characterControllerMode, Func<float> getSensitivity, Func<float> getAimingSensitivity, IStatisticsManager statisticsManager, QuestControllerClass questController)
        {
            profile.SetSpawnedInSession(value: false);
            return await LocalPlayer.Create(playerId, position, rotation, "Player", "", EPointOfView.FirstPerson, profile, aiControl: false, base.UpdateQueue, armsUpdateMode, EFT.Player.EUpdateMode.Auto, BackendConfigManager.Config.CharacterController.ClientPlayerMode, () => Singleton<SettingsManager>.Instance.Control.Settings.MouseSensitivity, () => Singleton<SettingsManager>.Instance.Control.Settings.MouseAimingSensitivity, new StatisticsManagerForPlayer1(), new FilterCustomizationClass(), questController, isYourPlayer: true);
        }

        public string InfiltrationPoint;

        public override void vmethod_0()
        {
        }

        /// <summary>
        /// Matchmaker countdown
        /// </summary>
        /// <param name="timeBeforeDeploy"></param>
        public override void vmethod_1(float timeBeforeDeploy)
        {

            base.vmethod_1(timeBeforeDeploy);
        }

        public static void SendOrReceiveSpawnPoint(EFT.Player player)
        {
            Logger.LogDebug(player.ProfileId + " " + player.Profile.Nickname);
            if (!player.ProfileId.StartsWith("pmc"))
                return;

            var position = player.Transform.position;
            if (!MatchmakerAcceptPatches.IsClient)
            {
                Dictionary<string, object> packet = new()
                {
                    {
                        "m",
                        "SpawnPointForCoop"
                    },
                    {
                        "serverId",
                        CoopGameComponent.GetServerId()
                    },
                    {
                        "x",
                        position.x
                    },
                    {
                        "y",
                        position.y
                    },
                    {
                        "z",
                        position.z
                    }
                };
                Logger.LogInfo("Setting Spawn Point to " + position);
                AkiBackendCommunication.Instance.PostJson("/coop/server/update", packet.ToJson());
                //var json = Request.Instance.GetJson($"/coop/server/spawnPoint/{CoopGameComponent.GetServerId()}");
                //Logger.LogInfo("Retreived Spawn Point " + json);
            }
            else if (MatchmakerAcceptPatches.IsClient)
            {
                if (PluginConfigSettings.Instance.CoopSettings.AllPlayersSpawnTogether)
                {
                    var json = AkiBackendCommunication.Instance.GetJson($"/coop/server/spawnPoint/{CoopGameComponent.GetServerId()}");
                    Logger.LogInfo("Retreived Spawn Point " + json);
                    var retrievedPacket = json.ParseJsonTo<Dictionary<string, string>>();
                    var x = float.Parse(retrievedPacket["x"].ToString());
                    var y = float.Parse(retrievedPacket["y"].ToString());
                    var z = float.Parse(retrievedPacket["z"].ToString());
                    var teleportPosition = new Vector3(x, y, z);
                    player.Teleport(teleportPosition, true);
                }
            }
            //}
        }

        /// <summary>
        /// Creating the EFT.LocalPlayer
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <param name="layerName"></param>
        /// <param name="prefix"></param>
        /// <param name="pointOfView"></param>
        /// <param name="profile"></param>
        /// <param name="aiControl"></param>
        /// <param name="updateQueue"></param>
        /// <param name="armsUpdateMode"></param>
        /// <param name="bodyUpdateMode"></param>
        /// <param name="characterControllerMode"></param>
        /// <param name="getSensitivity"></param>
        /// <param name="getAimingSensitivity"></param>
        /// <param name="statisticsManager"></param>
        /// <param name="questController"></param>
        /// <returns></returns>
        public override async Task<LocalPlayer> vmethod_2(int playerId, Vector3 position, Quaternion rotation, string layerName, string prefix, EPointOfView pointOfView, Profile profile, bool aiControl, EUpdateQueue updateQueue, EFT.Player.EUpdateMode armsUpdateMode, EFT.Player.EUpdateMode bodyUpdateMode, CharacterControllerSpawner.Mode characterControllerMode, Func<float> getSensitivity, Func<float> getAimingSensitivity, IStatisticsManager statisticsManager, QuestControllerClass questController)
        {
            //Logger.LogInfo("Creating CoopPlayer!");
            this.CreateCoopGameComponent();
            CoopGameComponent.GetCoopGameComponent().LocalGameInstance = this;


            var myPlayer = await CoopPlayer
               .Create(
               playerId
               , position
               , rotation
               , "Player"
               , ""
               , EPointOfView.FirstPerson
               , profile
               , aiControl: false
               , base.UpdateQueue
               , armsUpdateMode
               , EFT.Player.EUpdateMode.Auto
               , BackendConfigManager.Config.CharacterController.ClientPlayerMode
               , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseSensitivity
               , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseAimingSensitivity
               , new FilterCustomizationClass()
               , questController
               , isYourPlayer: true);
            SendOrReceiveSpawnPoint(myPlayer);

            // ---------------------------------------------
            // Here we can wait for other players, if desired
            //if (MatchmakerAcceptPatches.IsServer)
            //{
                await Task.Run(async () =>
                {
                    while(CoopGameComponent.GetCoopGameComponent() == null)
                    {

                    }

                    //var numbersOfPlayersToWaitFor = MatchmakerAcceptPatches.HostExpectedNumberOfPlayers - CoopGameComponent.GetCoopGameComponent().PlayerUsers.Length;
                    var numbersOfPlayersToWaitFor = MatchmakerAcceptPatches.HostExpectedNumberOfPlayers - CoopGameComponent.GetCoopGameComponent().PlayerUsers.Length;
                    do
                    {
                        numbersOfPlayersToWaitFor = MatchmakerAcceptPatches.HostExpectedNumberOfPlayers - CoopGameComponent.GetCoopGameComponent().PlayerUsers.Length;
                        if (MatchmakerAcceptPatches.TimeHasComeScreenController != null)
                        {
                            MatchmakerAcceptPatches.TimeHasComeScreenController.ChangeStatus($"Waiting for {numbersOfPlayersToWaitFor} Player(s)");
                        }

                        await Task.Delay(1000);

                    } while (numbersOfPlayersToWaitFor > 0);
                });
            //}

            // ---------------------------------------------


            CoopPatches.EnableDisablePatches();


           

            profile.SetSpawnedInSession(value: false);

            return myPlayer;
            //return base.vmethod_2(playerId, position, rotation, layerName, prefix, pointOfView, profile, aiControl, updateQueue, armsUpdateMode, bodyUpdateMode, characterControllerMode, getSensitivity, getAimingSensitivity, statisticsManager, questController);
        }

        /// <summary>
        /// Reconnection handling.
        /// </summary>
        public override void vmethod_3()
        {
            base.vmethod_3();
        }

        /// <summary>
        /// Bot System Starter -> Countdown
        /// </summary>
        /// <param name="startDelay"></param>
        /// <param name="controllerSettings"></param>
        /// <param name="spawnSystem"></param>
        /// <param name="runCallback"></param>
        /// <returns></returns>
        public override IEnumerator vmethod_4(float startDelay, BotControllerSettings controllerSettings, ISpawnSystem spawnSystem, Callback runCallback)
        {
            //Logger.LogDebug("vmethod_4");

            var shouldSpawnBots = !MatchmakerAcceptPatches.IsClient && PluginConfigSettings.Instance.CoopSettings.EnableAISpawnWaveSystem;
            if (!shouldSpawnBots)
            {
                controllerSettings.BotAmount = EBotAmount.NoBots;

                if (!PluginConfigSettings.Instance.CoopSettings.EnableAISpawnWaveSystem)
                    Logger.LogDebug("Bot Spawner System has been turned off - Wave System is Disabled");

                if (MatchmakerAcceptPatches.IsSinglePlayer)
                    Logger.LogDebug("Bot Spawner System has been turned off - You are running as Single Player");

                if (MatchmakerAcceptPatches.IsClient)
                    Logger.LogDebug("Bot Spawner System has been turned off - You are running as Client");
            }

            var nonwaves = (WaveInfo[])ReflectionHelpers.GetFieldFromTypeByFieldType(this.nonWavesSpawnScenario_0.GetType(), typeof(WaveInfo[])).GetValue(this.nonWavesSpawnScenario_0);

            LocalGameBotCreator profileCreator =
                new(BackEndSession
                , this.wavesSpawnScenario_0.SpawnWaves
                , Location_0.BossLocationSpawn
                , nonwaves
                , true);

            BotCreator botCreator = new(this, profileCreator, this.CreatePhysicalBot);
            BotZone[] botZones = LocationScene.GetAllObjects<BotZone>(false).ToArray<BotZone>();
            this.PBotsController.Init(this
                , botCreator
                , botZones
                , spawnSystem
                , this.wavesSpawnScenario_0.BotLocationModifier
                , controllerSettings.IsEnabled && controllerSettings.BotAmount != EBotAmount.NoBots
                , false // controllerSettings.IsScavWars
                , true
                , false
                , false
                , Singleton<GameWorld>.Instance
                , base.Location_0.OpenZones)
                ;

            Logger.LogInfo($"Location: {Location_0.Name}");

            MaxBotCount = Location_0.BotMax != 0 ? Location_0.BotMax : controllerSettings.BotAmount switch
            {
                EBotAmount.AsOnline => 10,
                EBotAmount.Low => 11,
                EBotAmount.Medium => 12,
                EBotAmount.High => 14,
                EBotAmount.Horde => 15,
                _ => 16,
            };

            MaxBotCount = 20; // BotMax is not obeyed for some reason, let's just always use 20.

            int numberOfBots = shouldSpawnBots ? MaxBotCount : 0;
            //Logger.LogDebug($"vmethod_4: Number of Bots: {numberOfBots}");

            this.PBotsController.SetSettings(numberOfBots, this.BackEndSession.BackEndConfig.BotPresets, this.BackEndSession.BackEndConfig.BotWeaponScatterings);
            this.PBotsController.AddActivePLayer(this.PlayerOwner.Player);

            yield return new WaitForSeconds(startDelay);
            if (shouldSpawnBots)
            {
                this.BossWaveManager.Run(EBotsSpawnMode.Anyway);

                if (this.nonWavesSpawnScenario_0 != null)
                    this.nonWavesSpawnScenario_0.Run();

                Logger.LogDebug($"Running Wave Scenarios");

                if (this.wavesSpawnScenario_0.SpawnWaves != null && this.wavesSpawnScenario_0.SpawnWaves.Length != 0)
                {
                    Logger.LogDebug($"Running Wave Scenarios with Spawn Wave length : {this.wavesSpawnScenario_0.SpawnWaves.Length}");
                    this.wavesSpawnScenario_0.Run(EBotsSpawnMode.Anyway);
                }

                StartCoroutine(StopBotSpawningAfterTimer());
            }
            else
            {
                if (this.wavesSpawnScenario_0 != null)
                    this.wavesSpawnScenario_0.Stop();
                if (this.nonWavesSpawnScenario_0 != null)
                    this.nonWavesSpawnScenario_0.Stop();
                if (this.BossWaveManager != null)
                    this.BossWaveManager.Stop();
            }

           

            yield return new WaitForEndOfFrame();
            Logger.LogInfo("vmethod_4.SessionRun");
            CreateExfiltrationPointAndInitDeathHandler();

            // No longer need this ping. Load complete and all other data should keep happening after this point.
            StopCoroutine(ClientLoadingPinger());
            //GCHelpers.ClearGarbage(emptyTheSet: true, unloadAssets: false);

            // Add FreeCamController to GameWorld GameObject
            Singleton<GameWorld>.Instance.gameObject.GetOrAddComponent<FreeCameraController>();
            yield break;
        }

        private IEnumerator StopBotSpawningAfterTimer()
        {
            //  If this true we skip the stopping!
            if (PluginConfigSettings.Instance.CoopSettings.BotWavesDisableStopper)
            {
                yield break;
            }

            yield return new WaitForSeconds(180);
            if (this.wavesSpawnScenario_0 != null)
            {
                this.wavesSpawnScenario_0.Stop();
            }

            if (this.nonWavesSpawnScenario_0 != null)
            {
                this.nonWavesSpawnScenario_0.Stop();
            }

            if (this.BossWaveManager != null)
                this.BossWaveManager.Stop();
        }

        //public override void vmethod_5()
        //{
        //    return;
        //}
        /// <summary>
        /// Died event handler
        /// </summary>
        public void CreateExfiltrationPointAndInitDeathHandler()
        {
            Logger.LogInfo("CreateExfiltrationPointAndInitDeathHandler");

            SpawnPoints spawnPoints = SpawnPoints.CreateFromScene(DateTime.Now, base.Location_0.SpawnPointParams);
            int spawnSafeDistance = ((Location_0.SpawnSafeDistanceMeters > 0) ? Location_0.SpawnSafeDistanceMeters : 100);
            SpawnSystemSettings settings = new(Location_0.MinDistToFreePoint, Location_0.MaxDistToFreePoint, Location_0.MaxBotPerZone, spawnSafeDistance);
            SpawnSystem = SpawnSystemFactory.CreateSpawnSystem(settings, () => Time.time, Singleton<GameWorld>.Instance, PBotsController, spawnPoints);

            base.GameTimer.Start();
            //base.vmethod_5();
            gparam_0.vmethod_0();
            //gparam_0.Player.ActiveHealthController.DiedEvent += HealthController_DiedEvent;
            gparam_0.Player.HealthController.DiedEvent += HealthController_DiedEvent;

            ISpawnPoint spawnPoint = SpawnSystem.SelectSpawnPoint(ESpawnCategory.Player, Profile_0.Info.Side);
            InfiltrationPoint = spawnPoint.Infiltration;
            Profile_0.Info.EntryPoint = InfiltrationPoint;
            Logger.LogDebug(InfiltrationPoint);
            ExfiltrationControllerClass.Instance.InitAllExfiltrationPoints(Location_0.exits, justLoadSettings: false, "");
            ExfiltrationPoint[] exfilPoints = ExfiltrationControllerClass.Instance.EligiblePoints(Profile_0);
            base.GameUi.TimerPanel.SetTime(DateTime.UtcNow, Profile_0.Info.Side, base.GameTimer.SessionSeconds(), exfilPoints);
            foreach (ExfiltrationPoint exfiltrationPoint in exfilPoints)
            {
                exfiltrationPoint.OnStartExtraction += ExfiltrationPoint_OnStartExtraction;
                exfiltrationPoint.OnCancelExtraction += ExfiltrationPoint_OnCancelExtraction;
                exfiltrationPoint.OnStatusChanged += ExfiltrationPoint_OnStatusChanged;
                UpdateExfiltrationUi(exfiltrationPoint, contains: false, initial: true);
            }

            base.dateTime_0 = DateTime.UtcNow;
            base.Status = GameStatus.Started;
            ConsoleScreen.ApplyStartCommands();
        }

        public Dictionary<string, (float, long, string)> ExtractingPlayers = new();
        public List<string> ExtractedPlayers = new();

        private void ExfiltrationPoint_OnCancelExtraction(ExfiltrationPoint point, EFT.Player player)
        {
            Logger.LogDebug("ExfiltrationPoint_OnCancelExtraction");
            Logger.LogDebug(point.Status);

            ExtractingPlayers.Remove(player.ProfileId);

            MyExitLocation = null;
            //player.SwitchRenderer(true);
        }

        private void ExfiltrationPoint_OnStartExtraction(ExfiltrationPoint point, EFT.Player player)
        {
            Logger.LogDebug("ExfiltrationPoint_OnStartExtraction");
            Logger.LogDebug(point.Settings.Name);
            Logger.LogDebug(point.Status);
            //Logger.LogInfo(point.ExfiltrationStartTime);
            Logger.LogDebug(point.Settings.ExfiltrationTime);
            bool playerHasMetRequirements = !point.UnmetRequirements(player).Any();
            //if (playerHasMetRequirements && !ExtractingPlayers.ContainsKey(player.ProfileId) && !ExtractedPlayers.Contains(player.ProfileId))
            if (!ExtractingPlayers.ContainsKey(player.ProfileId) && !ExtractedPlayers.Contains(player.ProfileId))
                ExtractingPlayers.Add(player.ProfileId, (point.Settings.ExfiltrationTime, DateTime.Now.Ticks, point.Settings.Name));
            //player.SwitchRenderer(false);

            MyExitLocation = point.Settings.Name;


        }

        private void ExfiltrationPoint_OnStatusChanged(ExfiltrationPoint point, EExfiltrationStatus status)
        {
            UpdateExfiltrationUi(point, point.Entered.Any((EFT.Player x) => x.ProfileId == Profile_0.Id));
            Logger.LogDebug("ExfiltrationPoint_OnStatusChanged");
            Logger.LogDebug(status);
            if (status == EExfiltrationStatus.Countdown)
            {

            }
            if (status == EExfiltrationStatus.NotPresent)
            {

            }
        }

        public ExitStatus MyExitStatus { get; set; } = ExitStatus.Survived;
        public string MyExitLocation { get; set; } = null;
        public ISpawnSystem SpawnSystem { get; set; }
        public int MaxBotCount { get; private set; }

        private void HealthController_DiedEvent(EDamageType obj)
        {
            //Logger.LogInfo(ScreenManager.Instance.CurrentScreenController.ScreenType);

            //Logger.LogInfo("CoopGame.HealthController_DiedEvent");


            gparam_0.Player.HealthController.DiedEvent -= method_15;
            gparam_0.Player.HealthController.DiedEvent -= HealthController_DiedEvent;

            PlayerOwner.vmethod_1();
            MyExitStatus = ExitStatus.Killed;
            MyExitLocation = null;

        }

        public override void Stop(string profileId, ExitStatus exitStatus, string exitName, float delay = 0f)
        {
            Logger.LogInfo("CoopGame.Stop");

            // Notify that I have left the Server
            AkiBackendCommunication.Instance.PostDownWebSocketImmediately(new Dictionary<string, object>() {
                { "m", "PlayerLeft" },
                { "profileId", Singleton<GameWorld>.Instance.MainPlayer.ProfileId },
                { "serverId", CoopGameComponent.GetServerId() }

            });

            // If I am the Host/Server, then ensure all the bots have left too
            if (MatchmakerAcceptPatches.IsServer)
            {
                foreach (var p in CoopGameComponent.GetCoopGameComponent().Players)
                {
                    AkiBackendCommunication.Instance.PostDownWebSocketImmediately(new Dictionary<string, object>() {

                            { "m", "PlayerLeft" },
                            { "profileId", p.Value.ProfileId },
                            { "serverId", CoopGameComponent.GetServerId() }

                        });
                }
            }

            CoopPatches.LeftGameDestroyEverything();

            if (this.BossWaveManager != null)
                this.BossWaveManager.Stop();

            if (this.nonWavesSpawnScenario_0 != null)
                this.nonWavesSpawnScenario_0.Stop();

            if (this.wavesSpawnScenario_0 != null)
                this.wavesSpawnScenario_0.Stop();

            // @konstantin90s suggestion to disable patches as the game closes
            CoopPatches.EnableDisablePatches();

            base.Stop(profileId, exitStatus, exitName, delay);
        }

        public override void CleanUp()
        {
            base.CleanUp();
            BaseLocalGame<GamePlayerOwner>.smethod_4(this.Bots);
        }

        private BossWaveManager BossWaveManager;

        private WavesSpawnScenario wavesSpawnScenario_0;

        private NonWavesSpawnScenario nonWavesSpawnScenario_0;

        private Func<EFT.Player, GamePlayerOwner> func_1;


        public new void method_6(string backendUrl, string locationId, int variantId)
        {
            Logger.LogInfo("CoopGame:method_6");
            return;
        }
    }
}
