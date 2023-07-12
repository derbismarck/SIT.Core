using Comfort.Common;
using EFT;
using EFT.Bots;
using EFT.InputSystem;
using EFT.UI;
using EFT.Weather;
using JsonType;
using SIT.Coop.Core.Matchmaker;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static EFT.Player;

namespace SIT.Core.Coop
{
    /// <summary>
    /// A custom Game Type
    /// </summary>
    public sealed class CoopGame : BaseLocalGame<GamePlayerOwner>, IBotGame
    {

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

            CoopGame coopGame = BaseLocalGame<GamePlayerOwner>
                .smethod_0<CoopGame>(inputTree, profile, backendDateTime, insurance, menuUI, commonUI, preloaderUI, gameUI, location, timeAndWeather, wavesSettings, dateTime
                , callback, fixedDeltaTime, updateQueue, backEndSession, new TimeSpan?(sessionTime));
            WildSpawnWave[] array = CoopGame.smethod_6(wavesSettings, location.waves);
            coopGame.nonWavesSpawnScenario_0 = (NonWavesSpawnScenario)ReflectionHelpers.GetMethodForType(typeof(NonWavesSpawnScenario) ,"smethod_0").Invoke
                (null, new object[] { coopGame, location, coopGame.PBotsController });
            coopGame.wavesSpawnScenario_0 = (WavesSpawnScenario)ReflectionHelpers.GetMethodForType(typeof(WavesSpawnScenario), "smethod_0").Invoke
                (null, new object[] { coopGame.gameObject, array, new Action<Wave>((wave) => coopGame.PBotsController.ActivateBotsByWave(wave)), location });// WavesSpawnScenario.smethod_0(@class.game.gameObject, array, new Action<Wave>(@class.method_0), location);
            BossLocationSpawn[] array2 = CoopGame.smethod_7(wavesSettings, location.BossLocationSpawn);

            var bosswavemanagerValue = ReflectionHelpers.GetMethodForType(typeof(BossWaveManager), "smethod_0").Invoke
                (null, new object[] { array2, new Action<BossLocationSpawn>((bossWave) => { coopGame.PBotsController.ActivateBotsByWave(bossWave); }) });
            ReflectionHelpers.GetFieldFromTypeByFieldType(typeof(CoopGame), typeof(BossWaveManager)).SetValue(coopGame, bosswavemanagerValue);

            coopGame.StartCoroutine(coopGame.ReplicatedWeather());

            return coopGame;
        }

        public IEnumerator ReplicatedWeather()
        {
            var waitSeconds = new WaitForSeconds(15f);

            while (true)
            {
                yield return waitSeconds;
                if(WeatherController.Instance != null)
                    WeatherController.Instance.SetWeatherForce(new WeatherClass() { });
            }
        }

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
                if(WeatherController.Instance != null)
                    return WeatherController.Instance.WeatherCurve;

                return null;
            }
        }



        public override IEnumerator vmethod_4(float startDelay, BotControllerSettings controllerSettings, ISpawnSystem spawnSystem, Callback runCallback)
        {
            //if(MatchmakerAcceptPatches.IsClient)
            //{
            //    yield return new WaitForSeconds(startDelay);
            //    yield return base.vmethod_4(startDelay, controllerSettings, spawnSystem, runCallback);
            //    yield break;
            //}

            BotsPresets botsPresets =
                new BotsPresets(BackEndSession
                , this.wavesSpawnScenario_0.SpawnWaves
                , new BossLocationSpawn[1] { new BossLocationSpawn() { Activated = false } }
                , (WaveInfo[])ReflectionHelpers.GetFieldFromTypeByFieldType(this.nonWavesSpawnScenario_0.GetType(), typeof(WaveInfo[])).GetValue(this.nonWavesSpawnScenario_0)
                , false);
            BotCreator botCreator = new BotCreator(this, botsPresets, new Func<Profile, Vector3, Task<LocalPlayer>>(this.method_16));
            BotZone[] array = LocationScene.GetAllObjects<BotZone>(false).ToArray<BotZone>();
            this.PBotsController.Init(this, botCreator, array, spawnSystem, this.wavesSpawnScenario_0.BotLocationModifier, controllerSettings.IsEnabled, controllerSettings.IsScavWars, false, false, false, Singleton<GameWorld>.Instance, base.Location_0.OpenZones);
            int numberOfBots = !MatchmakerAcceptPatches.IsClient ? 20 : 0;
            this.PBotsController.SetSettings(numberOfBots, this.BackEndSession.BackEndConfig.BotPresets, this.BackEndSession.BackEndConfig.BotWeaponScatterings);
            this.PBotsController.AddActivePLayer(this.gparam_0.Player);
            yield return new WaitForSeconds(startDelay);
            if (!MatchmakerAcceptPatches.IsClient)
            {
                if (this.wavesSpawnScenario_0.SpawnWaves != null && this.wavesSpawnScenario_0.SpawnWaves.Length != 0)
                {
                    this.wavesSpawnScenario_0.Run(EBotsSpawnMode.Anyway);
                }
                else
                {
                    this.nonWavesSpawnScenario_0.Run();
                }
                this.BossWaveManager.Run(EBotsSpawnMode.Anyway);
            }
            yield return base.vmethod_4(startDelay, controllerSettings, spawnSystem, runCallback);
            yield break;
        }

        private static WildSpawnWave[] smethod_6(WavesSettings wavesSettings, WildSpawnWave[] waves)
        {
            foreach (WildSpawnWave wildSpawnWave in waves)
            {
                wildSpawnWave.slots_min = (wildSpawnWave.slots_max = wavesSettings.BotAmount.ToBotAmountSlots(wildSpawnWave.slots_min, wildSpawnWave.slots_max));
                if (wavesSettings.IsTaggedAndCursed && wildSpawnWave.WildSpawnType == WildSpawnType.assault)
                {
                    wildSpawnWave.WildSpawnType = WildSpawnType.cursedAssault;
                }
                if (wavesSettings.IsBosses)
                {
                    wildSpawnWave.time_min += 5;
                    wildSpawnWave.time_max += 15;
                }
                wildSpawnWave.BotDifficulty = wavesSettings.BotDifficulty.ToBotDifficulty();
            }
            return waves;
        }

        private static BossLocationSpawn[] smethod_7(WavesSettings wavesSettings, BossLocationSpawn[] bossLocationSpawn)
        {
            if (!wavesSettings.IsBosses)
            {
                return new BossLocationSpawn[0];
            }
            foreach (BossLocationSpawn bossLocationSpawn2 in bossLocationSpawn)
            {
                List<int> source;
                try
                {
                    source = bossLocationSpawn2.BossEscortAmount.Split(',').Select(int.Parse).ToList();
                    bossLocationSpawn2.ParseMainTypesTypes();
                }
                catch (Exception)
                {
                    continue;
                }
                float bossChance = ((bossLocationSpawn2.BossChance > 0f) ? 100f : (-1f));
                if (bossLocationSpawn2.BossType == WildSpawnType.sectantPriest || bossLocationSpawn2.BossType == WildSpawnType.sectantWarrior || bossLocationSpawn2.BossType == WildSpawnType.bossZryachiy || bossLocationSpawn2.BossType == WildSpawnType.followerZryachiy)
                {
                    bossChance = -1f;
                }
                bossLocationSpawn2.BossChance = bossChance;
                switch (wavesSettings.BotAmount)
                {
                    case EBotAmount.Low:
                        bossLocationSpawn2.BossEscortAmount = source.Min((int x) => x).ToString();
                        break;
                    case EBotAmount.Medium:
                        {
                            int num = source.Max((int x) => x);
                            int num2 = source.Min((int x) => x);
                            bossLocationSpawn2.BossEscortAmount = ((num - num2) / 2).ToString();
                            break;
                        }
                    case EBotAmount.High:
                    case EBotAmount.Horde:
                        bossLocationSpawn2.BossEscortAmount = source.Max((int x) => x).ToString();
                        break;
                }
            }
            return bossLocationSpawn;
        }

        public Dictionary<string, EFT.Player> Bots { get; set; } = new Dictionary<string, EFT.Player>();

        private async Task<LocalPlayer> method_16(Profile profile, Vector3 position)
        {
            if (MatchmakerAcceptPatches.IsClient)
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
                int num = base.method_11();
                profile.SetSpawnedInSession(profile.Info.Side == EPlayerSide.Savage);
                LocalPlayer botPlayer 
                    = await LocalPlayer.Create(num, position, Quaternion.identity, "Player", "", EPointOfView.ThirdPerson, profile, true, base.UpdateQueue, EUpdateMode.Auto, EUpdateMode.Auto, BackendConfigManager.Config.CharacterController.BotPlayerMode
                    , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseSensitivity
                    , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseAimingSensitivity
                    , new StatisticsManager(), FilterCustomizationClass1.Default, null, false);
                botPlayer.Location = base.Location_0.Id;
                if (this.Bots.ContainsKey(botPlayer.ProfileId))
                {
                    return null;
                }
                else
                {
                    this.Bots.Add(botPlayer.ProfileId, botPlayer);
                }
                localPlayer = botPlayer;
            }
            return localPlayer;
        }

        public override void vmethod_0()
        {
            //gclass656_0 = (GClass656)ReflectionHelpers.GetAllMethodsForType(typeof(GClass656))
            //    .FirstOrDefault(x => x.IsConstructor).Invoke(null, new object[] { LoggerMode.None, dictionary_0, Bots });
        }


        public override void Stop(string profileId, ExitStatus exitStatus, string exitName, float delay = 0f)
        {
            if(this.BossWaveManager != null)
                this.BossWaveManager.Stop();

            if (this.nonWavesSpawnScenario_0 != null)
                this.nonWavesSpawnScenario_0.Stop();

            if (this.wavesSpawnScenario_0 != null)
                this.wavesSpawnScenario_0.Stop();

            base.Stop(profileId, exitStatus, exitName, delay);
        }

        public override void CleanUp()
        {
            base.CleanUp();
            BaseLocalGame<GamePlayerOwner>.smethod_3(this.Bots);
        }

        //private IEnumerator method_17(float startDelay, BotControllerSettings controllerSettings, ISpawnSystem spawnSystem, Callback runCallback)
        //{
        //    return base.vmethod_4(startDelay, controllerSettings, spawnSystem, runCallback);
        //}

        private BossWaveManager BossWaveManager;

        private WavesSpawnScenario wavesSpawnScenario_0;

        private NonWavesSpawnScenario nonWavesSpawnScenario_0;

    }
   
}
