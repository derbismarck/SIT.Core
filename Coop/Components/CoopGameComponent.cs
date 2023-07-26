using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SIT.Coop.Core.Matchmaker;
using SIT.Coop.Core.Player;
using SIT.Core.Configuration;
using SIT.Core.Coop.World;
using SIT.Core.Misc;
using SIT.Core.SP.Raid;
using SIT.Tarkov.Core;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace SIT.Core.Coop
{
    /// <summary>
    /// Coop Game Component is the User 1-2-1 communication to the Server
    /// </summary>
    public class CoopGameComponent : MonoBehaviour, IFrameIndexer
    {
        #region Fields/Properties        
        public WorldInteractiveObject[] ListOfInteractiveObjects { get; set; }
        private Request RequestingObj { get; set; }
        public string ServerId { get; set; } = null;
        public Dictionary<string, EFT.Player> Players { get; private set; } = new();
        BepInEx.Logging.ManualLogSource Logger { get; set; }
        public ConcurrentDictionary<string, ESpawnState> PlayersToSpawn { get; private set; } = new();
        public ConcurrentDictionary<string, Dictionary<string, object>> PlayersToSpawnPacket { get; private set; } = new();
        public Dictionary<string, Profile> PlayersToSpawnProfiles { get; private set; } = new();
        public ConcurrentDictionary<string, Vector3> PlayersToSpawnPositions { get; private set; } = new();

        public List<EFT.LocalPlayer> SpawnedPlayersToFinalize { get; private set; } = new();


        /**
         * https://stackoverflow.com/questions/48919414/poor-performance-with-concurrent-queue
         */
        public BlockingCollection<Dictionary<string, object>> ActionPackets { get; } = new();

        int PacketQueueSize_Receive { get; set; } = 0;
        int PacketQueueSize_Send { get; set; } = 0;

        private Dictionary<string, object>[] m_CharactersJson;

        private bool RunAsyncTasks = true;

        #endregion

        #region Public Voids

        public static CoopGameComponent GetCoopGameComponent()
        {
            if (CoopPatches.CoopGameComponentParent == null)
                return null;
          
            CoopPatches.CoopGameComponentParent.TryGetComponent<CoopGameComponent>(out var coopGameComponent);
            return coopGameComponent;
        }

        public static bool TryGetCoopGameComponent(out CoopGameComponent coopGameComponent)
        {
            coopGameComponent = GetCoopGameComponent();
            return coopGameComponent != null;
        }

        public static string GetServerId()
        {
            var coopGC = GetCoopGameComponent();
            if (coopGC == null)
                return null;

            return coopGC.ServerId;
        }
        #endregion

        #region Unity Component Methods

        void Awake()
        {

            // ----------------------------------------------------
            // Create a BepInEx Logger for CoopGameComponent
            Logger = BepInEx.Logging.Logger.CreateLogSource("CoopGameComponent");
            Logger.LogDebug("CoopGameComponent:Awake");

        }

        void Start()
        {
            Logger.LogDebug("CoopGameComponent:Start");

            // ----------------------------------------------------
            // Always clear "Players" when creating a new CoopGameComponent
            Players = new Dictionary<string, EFT.Player>();
            var ownPlayer = (LocalPlayer)Singleton<GameWorld>.Instance.RegisteredPlayers.First(x => x.IsYourPlayer);
            Players.Add(ownPlayer.Profile.AccountId, ownPlayer);

            RequestingObj = Request.GetRequestInstance(true, Logger);

            // Run an immediate call to get characters in the server
            _ = ReadFromServerCharacters();


            Task.Run(() => ReadFromServerCharactersLoop());
            StartCoroutine(ProcessServerCharacters());
            //Task.Run(() => ReadFromServerLastActions());
            //Task.Run(() => ProcessFromServerLastActions());
            StartCoroutine(SendWeatherToClients());

            ListOfInteractiveObjects = FindObjectsOfType<WorldInteractiveObject>();
            //PatchConstants.Logger.LogDebug($"Found {ListOfInteractiveObjects.Length} interactive objects");

            CoopPatches.EnableDisablePatches();
            //GCHelpers.EnableGC();

            HighPingMode = PluginConfigSettings.Instance.CoopSettings.ForceHighPingMode;

            Player_Init_Coop_Patch.SendPlayerDataToServer((LocalPlayer)Singleton<GameWorld>.Instance.RegisteredPlayers.First(x => x.IsYourPlayer));

        }

      

        void OnDestroy()
        {
            PatchConstants.Logger.LogDebug($"CoopGameComponent:OnDestroy");

            if (Players != null)
            {
                foreach (var pl in Players)
                {
                    if (pl.Value == null)
                        continue;

                    if (pl.Value.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                    {
                        GameObject.DestroyImmediate(prc);
                    }
                }
            }
            Players.Clear();
            PlayersToSpawnProfiles.Clear();
            PlayersToSpawnPositions.Clear();
            PlayersToSpawnPacket.Clear();
            StopCoroutine(ProcessServerCharacters());
            RunAsyncTasks = false;

            CoopPatches.EnableDisablePatches();
        }

        TimeSpan LateUpdateSpan = TimeSpan.Zero;
        Stopwatch swActionPackets { get; } = new Stopwatch();
        bool PerformanceCheck_ActionPackets { get; set; } = false; 

        void LateUpdate()
        {
            if(ServerHasStopped && !ServerHasStoppedActioned)
            {
                ServerHasStoppedActioned = true;
                try
                {
                    LocalGameInstance.Stop(Singleton<GameWorld>.Instance.MainPlayer.ProfileId, ExitStatus.Survived, "", 0);
                }
                catch { }
                return;
            }

            var DateTimeStart = DateTime.Now;

            if(!PluginConfigSettings.Instance.CoopSettings.ForceHighPingMode)
                HighPingMode = ServerPing > PING_LIMIT_HIGH;


            if (ActionPackets == null)
                return;

            if (Players == null)
                return;

            if (ActionPackets == null)
                return;

            if (Singleton<GameWorld>.Instance == null)
                return;

            if (ActionPackets.Count > 0)
            {
                Dictionary<string, object> result = null;
                swActionPackets.Restart();
                //var indexOfPacketsHandled = 0;
                //while (ActionPackets.TryTake(out result) && indexOfPacketsHandled++ < 20)
                while (ActionPackets.TryTake(out result))
                {
                    ProcessLastActionDataPacket(result);
                }
                PerformanceCheck_ActionPackets = (swActionPackets.ElapsedMilliseconds > 14);

            }

            List<Dictionary<string, object>> playerStates = new List<Dictionary<string, object>>();
            if (LastPlayerStateSent < DateTime.Now.AddMilliseconds(PluginConfigSettings.Instance.CoopSettings.SETTING_PlayerStateTickRateInMS))
            {
                //Logger.LogDebug("Creating PRC");

                foreach (var player in Players.Values)
                {
                    if (player == null)
                        continue;

                    if (!player.TryGetComponent<PlayerReplicatedComponent>(out PlayerReplicatedComponent prc))
                        continue;

                    if (prc.IsClientDrone)
                        continue;

                    if (!player.enabled)
                        continue;

                    if (!player.isActiveAndEnabled)
                        continue;


                    CreatePlayerStatePacketFromPRC(ref playerStates, player, prc);
                }

                foreach (var player in Singleton<GameWorld>.Instance.RegisteredPlayers)
                {
                    if (player == null)
                        continue;

                    if (!player.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                        continue;

                    if (prc.IsClientDrone)
                        continue;

                    if (!player.enabled)
                        continue;

                    if (!player.isActiveAndEnabled)
                        continue;

                    if (playerStates.Any(x => x.ContainsKey("accountId") && x["accountId"].ToString() == player.Profile.AccountId))
                        continue;

                    CreatePlayerStatePacketFromPRC(ref playerStates, player, prc);
                }

                if (RequestingObj == null)
                    return;

                //Logger.LogDebug(playerStates.SITToJson());
                RequestingObj.SendListDataToPool(string.Empty, playerStates);

                LastPlayerStateSent = DateTime.Now;
            }

            if (SpawnedPlayersToFinalize == null)
                return;

            List<EFT.LocalPlayer> SpawnedPlayersToRemoveFromFinalizer = new List<LocalPlayer>();
            foreach (var p in SpawnedPlayersToFinalize)
            {
                SetWeaponInHandsOfNewPlayer(p, () => {

                    SpawnedPlayersToRemoveFromFinalizer.Add(p);
                });
            }
            foreach (var p in SpawnedPlayersToRemoveFromFinalizer)
            {
                SpawnedPlayersToFinalize.Remove(p);
            }

            // In game ping system.
            if (Singleton<FrameMeasurer>.Instantiated)
            {
                FrameMeasurer instance = Singleton<FrameMeasurer>.Instance;
                instance.PlayerRTT = ServerPing;
                instance.ServerFixedUpdateTime = ServerPing;
                instance.ServerTime = ServerPing;
                return;
            }

            LateUpdateSpan = DateTime.Now - DateTimeStart;
        }

        #endregion

        private async Task ReadFromServerCharactersLoop()
        {
            if (GetServerId() == null)
                return;

            
            while (RunAsyncTasks)
            {
                await Task.Delay(10000);

                if (Players == null)
                    continue;

                await ReadFromServerCharacters();

            }
        }

        private async Task ReadFromServerCharacters()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d.Add("serverId", GetServerId());
            d.Add("pL", new List<string>());

            // -----------------------------------------------------------------------------------------------------------
            // We must filter out characters that already exist on this match!
            //
            var playerList = new List<string>();
            if (!PluginConfigSettings.Instance.CoopSettings.SETTING_DEBUGSpawnDronesOnServer)
            {
                if (PlayersToSpawn.Count > 0)
                    playerList.AddRange(PlayersToSpawn.Keys.ToArray());
                if (Players.Keys.Any())
                    playerList.AddRange(Players.Keys.ToArray());
                if (Singleton<GameWorld>.Instance.RegisteredPlayers.Any())
                    playerList.AddRange(Singleton<GameWorld>.Instance.RegisteredPlayers.Select(x => x.Profile.AccountId));
            }
            //
            // -----------------------------------------------------------------------------------------------------------
            d["pL"] = playerList.Distinct();
            var jsonDataToSend = d.ToJson();

            try
            {
                m_CharactersJson = await RequestingObj.PostJsonAsync<Dictionary<string, object>[]>("/coop/server/read/players", jsonDataToSend, 30000);
                if (m_CharactersJson == null)
                    return;

                if (!m_CharactersJson.Any())
                    return;

                if(m_CharactersJson[0].ContainsKey("notFound"))
                {
                    // Game is broken and doesn't exist!
                    if(LocalGameInstance != null)
                    {
                        this.ServerHasStopped = true;
                    }
                    return;
                }

                //Logger.LogDebug($"CoopGameComponent.ReadFromServerCharacters:{actionsToValues.Length}");

                var packets = m_CharactersJson
                     .Where(x => x != null);
                if (packets == null)
                    return;

                foreach (var queuedPacket in packets)
                {
                    if (queuedPacket != null && queuedPacket.Count > 0)
                    {
                        if (queuedPacket != null)
                        {
                            if (queuedPacket.ContainsKey("m"))
                            {
                                var method = queuedPacket["m"].ToString();
                                if (method != "PlayerSpawn")
                                    continue;

                                string accountId = queuedPacket["accountId"].ToString();
                                if (!PluginConfigSettings.Instance.CoopSettings.SETTING_DEBUGSpawnDronesOnServer)
                                {
                                    if (Players == null
                                        || Players.ContainsKey(accountId)
                                        || Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == accountId))
                                    {
                                        Logger.LogDebug($"Ignoring call to Spawn player {accountId}. The player already exists in the game.");
                                        continue;
                                    }
                                }

                                if (PlayersToSpawn.ContainsKey(accountId))
                                    continue;

                                if (!PlayersToSpawnPacket.ContainsKey(accountId))
                                    PlayersToSpawnPacket.TryAdd(accountId, queuedPacket);

                                if (!PlayersToSpawn.ContainsKey(accountId))
                                    PlayersToSpawn.TryAdd(accountId, ESpawnState.None);

                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                Logger.LogError(ex.ToString());

            }
            finally
            {

            }
        }

        private IEnumerator ProcessServerCharacters()
        {
            var waitEndOfFrame = new WaitForEndOfFrame();

            if (GetServerId() == null)
                yield return waitEndOfFrame;

            var waitSeconds = new WaitForSeconds(0.5f);

            while (RunAsyncTasks)
            {
                yield return waitSeconds;
                foreach (var p in PlayersToSpawn)
                {
                    // If not showing drones. Check whether the "Player" has been registered, if they have, then ignore the drone
                    if (!PluginConfigSettings.Instance.CoopSettings.SETTING_DEBUGSpawnDronesOnServer)
                    {
                        if (Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == p.Key))
                        {
                            if (PlayersToSpawn.ContainsKey(p.Key))
                                PlayersToSpawn[p.Key] = ESpawnState.Ignore;

                            continue;
                        }

                        if (Players.Any(x => x.Key == p.Key))
                        {
                            if (PlayersToSpawn.ContainsKey(p.Key))
                                PlayersToSpawn[p.Key] = ESpawnState.Ignore;

                            continue;
                        }
                    }


                    if (PlayersToSpawn[p.Key] == ESpawnState.Ignore)
                        continue;

                    if (PlayersToSpawn[p.Key] == ESpawnState.Spawned)
                        continue;

                    Vector3 newPosition = Vector3.zero;
                    if (PlayersToSpawnPacket[p.Key].ContainsKey("sPx")
                        && PlayersToSpawnPacket[p.Key].ContainsKey("sPy")
                        && PlayersToSpawnPacket[p.Key].ContainsKey("sPz"))
                    {
                        string npxString = PlayersToSpawnPacket[p.Key]["sPx"].ToString();
                        newPosition.x = float.Parse(npxString);
                        string npyString = PlayersToSpawnPacket[p.Key]["sPy"].ToString();
                        newPosition.y = float.Parse(npyString);
                        string npzString = PlayersToSpawnPacket[p.Key]["sPz"].ToString();
                        newPosition.z = float.Parse(npzString) + 0.5f;
                        ProcessPlayerBotSpawn(PlayersToSpawnPacket[p.Key], p.Key, newPosition, false);
                    }
                    else
                    {
                        Logger.LogError($"ReadFromServerCharacters::PlayersToSpawnPacket does not have positional data for {p.Key}");
                    }
                }

                
                yield return waitEndOfFrame;
            }
        }

        private void ProcessPlayerBotSpawn(Dictionary<string, object> packet, string accountId, Vector3 newPosition, bool isBot)
        {
            // If not showing drones. Check whether the "Player" has been registered, if they have, then ignore the drone
            if (!PluginConfigSettings.Instance.CoopSettings.SETTING_DEBUGSpawnDronesOnServer)
            {
                if (Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == accountId))
                {
                    if (PlayersToSpawn.ContainsKey(accountId))
                        PlayersToSpawn[accountId] = ESpawnState.Ignore;

                    return;
                }

                if (Players.Keys.Any(x => x == accountId))
                {
                    if (PlayersToSpawn.ContainsKey(accountId))
                        PlayersToSpawn[accountId] = ESpawnState.Ignore;

                    return;
                }
            }


            // If CreatePhysicalOtherPlayerOrBot has been done before. Then ignore the Deserialization section and continue.
            if (PlayersToSpawn.ContainsKey(accountId)
                && PlayersToSpawnProfiles.ContainsKey(accountId)
                && PlayersToSpawnProfiles[accountId] != null
                )
            {
                CreatePhysicalOtherPlayerOrBot(PlayersToSpawnProfiles[accountId], newPosition);
                return;
            }

            if (PlayersToSpawnProfiles.ContainsKey(accountId))
                return;

            PlayersToSpawnProfiles.Add(accountId, null);

            Logger.LogDebug($"ProcessPlayerBotSpawn:{accountId}");

            Profile profile = new Profile();
            if (packet.ContainsKey("profileJson"))
            {
                if (packet["profileJson"].ToString().TrySITParseJson(out profile))
                {
                    Logger.LogInfo("Obtained Profile");
                    // Send to be loaded
                    PlayersToSpawnProfiles[accountId] = profile;
                }
                else
                {
                    Logger.LogError("Unable to Parse Profile");
                    PlayersToSpawn[accountId] = ESpawnState.Error;
                    return;
                }
            }
            //profile.AccountId = accountId;
            //profile.BackendCounters.Clear();
            //profile.CheckedChambers.Clear();
            //profile.CheckedMagazines.Clear();
            //profile.ConditionCounters.Counters.Clear();
            //profile.Encyclopedia.Clear();
            //profile.Info.Side = isBot ? EPlayerSide.Savage : EPlayerSide.Usec;
            //if (packet.ContainsKey("side"))
            //{
            //    if (Enum.TryParse<EPlayerSide>(packet["side"].ToString(), out var side))
            //    {
            //        profile.Info.Side = side;
            //    }
            //}
            //profile.Skills.StartClientMode();
            ////profile.QuestItems = new QuestItems[0];
            //profile.QuestsData.Clear();

            //try
            //{
            //    //Logger.LogDebug("PlayerBotSpawn:: Adding " + accountId + " to spawner list");
            //    profile.Id = accountId;
            //    profile.Info.Nickname = "BSG Employee " + Players.Count;
            //    if (packet.ContainsKey("p.info"))
            //    {
            //        //Logger.LogDebug("PlayerBotSpawn:: Converting Profile data");
            //        profile.Info = packet["p.info"].ToString().SITParseJson<ProfileInfo>();
            //        //Logger.LogDebug("PlayerBotSpawn:: Converted Profile data:: Hello " + profile.Info.Nickname);
            //    }
            //    if (packet.ContainsKey("p.cust"))
            //    {
            //        var parsedCust = packet["p.cust"].ToString().ParseJsonTo<Dictionary<EBodyModelPart, string>>(Array.Empty<JsonConverter>());
            //        if (parsedCust != null && parsedCust.Any())
            //        {
            //            profile.Customization = new Customization(parsedCust);
            //            //Logger.LogDebug("PlayerBotSpawn:: Set Profile Customization for " + profile.Info.Nickname);
            //        }
            //        else
            //        {
            //            Logger.LogError("ProcessPlayerBotSpawn:: Profile Customization for " + profile.Info.Nickname + " failed!");
            //            return;
            //        }
            //    }
            //    if (packet.ContainsKey("p.equip"))
            //    {
            //        var pEquip = packet["p.equip"].ToString();
            //        if(pEquip.TrySITParseJson<Equipment>(out Equipment equipment))
            //        {
            //            if (profile.Inventory.GetAllEquipmentItems().Any()
            //                )
            //                profile.Inventory.Equipment = equipment;
            //            else
            //            {
            //                Logger.LogError($"{accountId} Equipment could not be loaded!");
            //                PlayersToSpawn[accountId] = ESpawnState.Error;
            //            }
            //        }
            //        //var equipment = packet["p.equip"].ToString().SITParseJson<Equipment>();
            //        //profile.Inventory.Equipment = equipment;

            //        //Logger.LogDebug("PlayerBotSpawn:: Set Equipment for " + profile.Info.Nickname);

            //    }
            //    if (packet.ContainsKey("isHost"))
            //    {
            //    }

            //    if (packet.ContainsKey("profileId"))
            //    {
            //        profile.Id = packet["profileId"].ToString();
            //        //Logger.LogDebug($"profile id {profile.Id}");
            //    }

                

            //    // Send to be loaded
            //    PlayersToSpawnProfiles[accountId] = profile;
            //}
            //catch (Exception ex)
            //{
            //    Logger.LogError($"PlayerBotSpawn::ERROR::" + ex.Message);
            //}

        }

        private void CreatePhysicalOtherPlayerOrBot(Profile profile, Vector3 position)
        {
            try
            {
                // A final check to stop duplicate clones spawning on Server
                if (!PluginConfigSettings.Instance.CoopSettings.SETTING_DEBUGSpawnDronesOnServer)
                {
                    if (Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == profile.AccountId))
                        return;


                    if (Singleton<GameWorld>.Instance.AllPlayers.Any(x => x.Profile.AccountId == profile.AccountId))
                        return;

                    if (Players.Keys.Any(x => x == profile.AccountId))
                        return;
                }

                if (Players == null)
                {
                    Logger.LogError("Players is NULL!");
                    return;
                }

                int playerId = Players.Count + Singleton<GameWorld>.Instance.RegisteredPlayers.Count + 1;
                if (profile == null)
                {
                    Logger.LogError("CreatePhysicalOtherPlayerOrBot Profile is NULL!");
                    return;
                }

                PlayersToSpawn.TryAdd(profile.AccountId, ESpawnState.None);
                if (PlayersToSpawn[profile.AccountId] == ESpawnState.None)
                {
                    PlayersToSpawn[profile.AccountId] = ESpawnState.Loading;
                    IEnumerable<ResourceKey> allPrefabPaths = profile.GetAllPrefabPaths();
                    if (allPrefabPaths.Count() == 0)
                    {
                        Logger.LogError($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::PrefabPaths are empty!");
                        PlayersToSpawn[profile.AccountId] = ESpawnState.Error;
                        return;
                    }

                    Singleton<PoolManager>.Instance.LoadBundlesAndCreatePools(PoolManager.PoolsCategory.Raid, PoolManager.AssemblyType.Local, allPrefabPaths.ToArray(), JobPriority.General)
                        .ContinueWith(x =>
                        {
                            if (x.IsCompleted)
                            {
                                PlayersToSpawn[profile.AccountId] = ESpawnState.Spawning;
                                Logger.LogDebug($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::Load Complete.");
                            }
                            else if (x.IsFaulted)
                            {
                                Logger.LogError($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::Load Failed.");
                            }
                            else if (x.IsCanceled)
                            {
                                Logger.LogError($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::Load Cancelled?.");
                            }
                        })
                        ;

                    return;
                }

                // ------------------------------------------------------------------
                // Its loading on the previous pass, ignore this one until its finished
                if (PlayersToSpawn[profile.AccountId] == ESpawnState.Loading)
                {
                    return;
                }

                // ------------------------------------------------------------------
                // It has already spawned, we should never reach this point if Players check is working in previous step
                if (PlayersToSpawn[profile.AccountId] == ESpawnState.Spawned)
                {
                    Logger.LogDebug($"CreatePhysicalOtherPlayerOrBot::{profile.Info.Nickname}::Is already spawned");
                    return;
                }

                // Move this here. Ensure that this is run before it attempts again on slow PCs
                PlayersToSpawn[profile.AccountId] = ESpawnState.Spawned;

                // ------------------------------------------------------------------
                // Create Local Player drone
                CreateLocalPlayer(profile, position, playerId);
                // TODO: I would like to use the following, but it causes the drones to spawn without a weapon.
                //CreateLocalPlayerAsync(profile, position, playerId);

            }
            catch (Exception ex)
            {
                Logger.LogError(ex.ToString());
            }

        }

        private void CreateLocalPlayer(Profile profile, Vector3 position, int playerId)
        {
            // If this is an actual PLAYER player that we're creating a drone for, when we set
            // aiControl to true then they'll automatically run voice lines (eg when throwing
            // a grenade) so we need to make sure it's set to FALSE for the drone version of them.
            var useAiControl = !profile.Id.StartsWith("pmc");

            // For actual bots, we can gain SIGNIFICANT clientside performance on the
            // non-host client by ENABLING aiControl for the bot. This has zero consequences
            // in terms of synchronization. No idea why having aiControl OFF is so expensive,
            // perhaps it's more accurate to think of it as an inverse bool of
            // "player controlled", where the engine has to enable a bunch of additional
            // logic when aiControl is turned off (in other words, for players)?

            var otherPlayer = LocalPlayer.Create(playerId // playerId: The unique identifier for the player
               , position // position: The starting position for the player in the game world
               , Quaternion.identity // rotation: The starting rotation for the player. Here it's set to "no rotation" with Quaternion.identity
               , "Player" // layerName: The layer this player belongs to. Used for things like collision detection
               , "" // prefix: A string that might be used for naming or categorizing the player
               , EPointOfView.ThirdPerson // pointOfView: The camera view for this player. Here it's set to third person
               , profile // profile: The Profile object containing data about this player
               , aiControl: useAiControl // aiControl: Boolean indicating whether this player is controlled by AI or not. Here it's set to false, so it's not AI controlled
               , EUpdateQueue.Update // updateQueue: Which update queue this player should use. Here it's set to the standard Update queue
               , EFT.Player.EUpdateMode.Auto // armsUpdateMode: The update mode for the player's arms. Here it's set to automatically update
               , EFT.Player.EUpdateMode.Auto // bodyUpdateMode: The update mode for the player's body. Here it's set to automatically update
               , BackendConfigManager.Config.CharacterController.ClientPlayerMode // characterControllerMode: The mode for the player's character controller, set from the backend config
               , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseSensitivity // getSensitivity: A function that returns the mouse sensitivity for this player
               , () => Singleton<SettingsManager>.Instance.Control.Settings.MouseAimingSensitivity // getAimingSensitivity: A function that returns the mouse aiming sensitivity for this player
               , new CoopStatisticsManager() // statisticsManager: An instance of the class for managing player statistics
               , FilterCustomizationClass.Default // filter: A filter used for customization, set to the default filter
               , null // questController: A controller for managing quests. Here it's set to null, indicating no quests for this player
               , isYourPlayer: false // isYourPlayer: Boolean indicating whether this player is the user's player. Here it's set to false, so this is not the user's player
            ).Result;


            if (otherPlayer == null)
                return;

            // ----------------------------------------------------------------------------------------------------
            // Add the player to the custom Players list
            if (!Players.ContainsKey(profile.AccountId))
                Players.Add(profile.AccountId, otherPlayer);

            if (!Singleton<GameWorld>.Instance.RegisteredPlayers.Any(x => x.Profile.AccountId == profile.AccountId))
                Singleton<GameWorld>.Instance.RegisteredPlayers.Add(otherPlayer);

            // Create/Add PlayerReplicatedComponent to the LocalPlayer
            var prc = otherPlayer.GetOrAddComponent<PlayerReplicatedComponent>();
            prc.IsClientDrone = true;

            //if (MatchmakerAcceptPatches.IsServer)
            {
                if (otherPlayer.ProfileId.StartsWith("pmc"))
                {
                    if (LocalGameInstance != null)
                    {
                        var botController = (BotControllerClass)ReflectionHelpers.GetFieldFromTypeByFieldType(typeof(BaseLocalGame<GamePlayerOwner>), typeof(BotControllerClass)).GetValue(this.LocalGameInstance);
                        if (botController != null)
                        {
                            Logger.LogDebug("Adding Client Player to Enemy list");
                            botController.AddActivePLayer(otherPlayer);
                        }
                    }
                }
            }

            if (!SpawnedPlayersToFinalize.Any(x => otherPlayer))
                SpawnedPlayersToFinalize.Add(otherPlayer);

            Logger.LogDebug($"CreateLocalPlayer::{profile.Info.Nickname}::Spawned.");

            SetWeaponInHandsOfNewPlayer(otherPlayer, () => { });

            // Setup Dogtags for players
            Item containedItem = otherPlayer.Equipment.GetSlot(EquipmentSlot.Dogtag).ContainedItem;
            DogtagComponent dogtagComponent = (containedItem != null) ? (DogtagComponent)UpdateDogtagPatch.GetItemComponent(containedItem) : null;
            if (dogtagComponent != null)
            {
                dogtagComponent.GroupId = otherPlayer.Profile.Info.GroupId;
            }
        }

        /// <summary>
        /// Attempts to set up the New Player with the current weapon after spawning
        /// </summary>
        /// <param name="person"></param>
        public void SetWeaponInHandsOfNewPlayer(EFT.Player person, Action successCallback)
        {
            //Logger.LogDebug($"SetWeaponInHandsOfNewPlayer: {person.Profile.AccountId}");

            var equipment = person.Profile.Inventory.Equipment;
            if (equipment == null)
            {
                Logger.LogError($"SetWeaponInHandsOfNewPlayer: {person.Profile.AccountId} has no Equipment!");
            }
            Item item = null;

            if (equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;

            if (item == null && equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;

            if (item == null && equipment.GetSlot(EquipmentSlot.Holster).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;

            if (item == null && equipment.GetSlot(EquipmentSlot.Scabbard).ContainedItem != null)
                item = equipment.GetSlot(EquipmentSlot.Scabbard).ContainedItem;

            if (item == null)
            {
                Logger.LogError($"SetWeaponInHandsOfNewPlayer:Unable to find any weapon for {person.Profile.AccountId}");
            }

            //Logger.LogDebug($"SetWeaponInHandsOfNewPlayer: {person.Profile.AccountId} {item.TemplateId}");

            person.SetItemInHands(item, (IResult) =>
            {

                if (IResult.Failed == true)
                {
                    Logger.LogError($"SetWeaponInHandsOfNewPlayer:Unable to set item {item} in hands for {person.Profile.AccountId}");
                }

                if (IResult.Succeed == true)
                {
                    if (successCallback != null)
                            successCallback();
                }

                if(person.TryGetItemInHands<Item>() != null)
                {
                    if (successCallback != null)
                        successCallback();
                }

            });
        }

        public void ProcessLastActionDataPacket(Dictionary<string, object> packet)
        {
            if (Singleton<GameWorld>.Instance == null)
                return;

            if (packet == null || packet.Count == 0)
            {
                PatchConstants.Logger.LogInfo("CoopGameComponent:No Data Returned from Last Actions!");
                return;
            }

            ProcessPlayerPacket(packet);
            ProcessWorldPacket(packet);

        }

        private void ProcessWorldPacket(Dictionary<string, object> packet)
        {
            if (packet.ContainsKey("accountId"))
                return;

            if (!packet.ContainsKey("m"))
                return;

            foreach(var coopPatch in CoopPatches.NoMRPPatches)
            {
                var imrwp = coopPatch as IModuleReplicationWorldPatch;
                if(imrwp != null)
                {
                    if(imrwp.MethodName == packet["m"].ToString())
                    {
                        imrwp.Replicated(packet);
                    }
                }
            }

            switch (packet["m"].ToString())
            {
                case "WIO_Interact":
                    WorldInteractiveObject_Interact_Patch.Replicated(packet);
                    break;
                case "Door_Interact":
                    Door_Interact_Patch.Replicated(packet);
                    break;
                    
            }
        }

        private void ProcessPlayerPacket(Dictionary<string, object> packet)
        {
            if (packet == null)
                return;

            if (!packet.ContainsKey("accountId"))
                return;

            if (packet["accountId"] == null || packet["accountId"].ToString() == "null")
            {
                Logger.LogError("Account Id is null for Packet");
                return;
            }

            var accountId = packet["accountId"].ToString();

            if (Players == null)
                return;

            if (!Players.Any())
                return;

            var registeredPlayers = Singleton<GameWorld>.Instance.RegisteredPlayers;

            if (!Players.Any(x => x.Key == accountId) && !registeredPlayers.Any(x => x.Profile.AccountId == accountId))
                return;

            int index = 0;

            HashSet<int> processedPlayers = new HashSet<int>();

            foreach (var plyr in
                Players.ToArray()
                .Where(x => x.Key == accountId)
                .Where(x => x.Value != null)
                )
            {
                if (processedPlayers.Contains(plyr.Value.PlayerId))
                    continue;

                if (plyr.Value.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                {
                    //Logger.LogDebug($"[{Time.frameCount}]     Processing packet for [{accountId}, {plyr.Value.PlayerId}, {plyr.Value.Id}] [{index}]");
                    prc.ProcessPacket(packet);
                }
                else
                {
                    Logger.LogError($"Player {accountId} doesn't have a PlayerReplicatedComponent!");
                }

                processedPlayers.Add(plyr.Value.PlayerId);
                index++;
            }

            try
            {
                index = 0;
                // Deal to all versions of this guy
                foreach (var plyr in Singleton<GameWorld>.Instance.RegisteredPlayers
                    .Where(x => x.Profile != null && x.Profile.AccountId == accountId))
                {
                    if (processedPlayers.Contains(plyr.PlayerId))
                        continue;

                    if (plyr.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                    {
                        //Logger.LogDebug($"[{Time.frameCount}]     Processing packet for [{accountId}, {plyr.PlayerId}, {plyr.Id}] [{index}]");
                        prc.ProcessPacket(packet);
                    }
                    else
                    {
                        Logger.LogError($"Player {accountId} doesn't have a PlayerReplicatedComponent!");
                    }

                    processedPlayers.Add(plyr.PlayerId);
                    index++;
                }
            }
            catch (Exception) { }
        }

        private static void CreatePlayerStatePacketFromPRC(ref List<Dictionary<string, object>> playerStates, EFT.Player player, PlayerReplicatedComponent prc)
        {
            Dictionary<string, object> dictPlayerState = new Dictionary<string, object>();

            if (prc.ReplicatedDirection.HasValue && !player.IsYourPlayer)
            {
                dictPlayerState.Add("dX", prc.ReplicatedDirection.Value.x);
                dictPlayerState.Add("dY", prc.ReplicatedDirection.Value.y);
            }
            dictPlayerState.Add("pX", player.Position.x);
            dictPlayerState.Add("pY", player.Position.y);
            dictPlayerState.Add("pZ", player.Position.z);
            dictPlayerState.Add("rX", player.Rotation.x);
            dictPlayerState.Add("rY", player.Rotation.y);
            dictPlayerState.Add("pose", player.MovementContext.PoseLevel);
            dictPlayerState.Add("spd", player.MovementContext.CharacterMovementSpeed);
            dictPlayerState.Add("spr", player.MovementContext.IsSprintEnabled);
            dictPlayerState.Add("tp", prc.TriggerPressed);
            dictPlayerState.Add("alive", player.HealthController.IsAlive);
            dictPlayerState.Add("tilt", player.MovementContext.Tilt);
            dictPlayerState.Add("prn", player.MovementContext.IsInPronePose);
            dictPlayerState.Add("accountId", player.Profile.AccountId);
            dictPlayerState.Add("serverId", GetServerId());
            dictPlayerState.Add("t", DateTime.Now.Ticks);
            dictPlayerState.Add("phys.hs.current", player.Physical.HandsStamina.Current);
            dictPlayerState.Add("phys.hs.total", player.Physical.HandsStamina.TotalCapacity.Value);
            dictPlayerState.Add("m", "PlayerState");

            playerStates.Add(dictPlayerState);
        }

        private IEnumerator SendWeatherToClients()
        {
            var waitSeconds = new WaitForSeconds(60f);

            while (RunAsyncTasks)
            {
                yield return waitSeconds;

            }
        }

        private DateTime LastPlayerStateSent { get; set; } = DateTime.Now;
        public ulong LocalIndex { get; set; }

        public double LocalTime => 0;

        public BaseLocalGame<GamePlayerOwner> LocalGameInstance { get; internal set; }

        int GuiX = 10;
        int GuiWidth = 400;

        public const int PING_LIMIT_HIGH = 125;
        public const int PING_LIMIT_MID = 100;

        public int ServerPing { get; set; } = 1;
        public ConcurrentQueue<int> ServerPingSmooth { get; } = new();
        public DateTimeOffset LastServerPing { get; set; } = DateTimeOffset.Now;
        public string LastPacketHash { get; set; } = "";

        public bool HighPingMode { get; set; } = false;
        public bool ServerHasStopped { get; set; }
        private bool ServerHasStoppedActioned { get; set; }

        void OnGUI()
        {
            var rect = new Rect(GuiX, 5, GuiWidth, 100);

            rect.y = 5;
            GUI.Label(rect, $"SIT Coop: " + (MatchmakerAcceptPatches.IsClient ? "CLIENT" : "SERVER"));
            rect.y += 15;

            // PING ------
            GUI.contentColor = Color.white;
            GUI.contentColor = ServerPing >= PING_LIMIT_HIGH ? Color.red : ServerPing >= PING_LIMIT_MID ? Color.yellow : Color.green;
            GUI.Label(rect, $"Ping:{(ServerPing)}");
            rect.y += 15;
            GUI.Label(rect, $"Ping RTT:{(ServerPing + Request.Instance.PostPing)}");
            rect.y += 15;
            GUI.contentColor = Color.white;

            if (PerformanceCheck_ActionPackets)
            {
                GUI.contentColor = Color.red;
                GUI.Label(rect, $"BAD PERFORMANCE!");
                GUI.contentColor = Color.white;
                rect.y += 15;
            }

            if (HighPingMode)
            {
                GUI.contentColor = Color.red;
                GUI.Label(rect, $"!HIGH PING MODE!");
                GUI.contentColor = Color.white;
                rect.y += 15;
            }

            var numberOfPlayers = Players.Count(x => x.Value.ProfileId.StartsWith("pmc"));
            GUI.Label(rect, $"Players: {numberOfPlayers}");

            var numberOfLiveAI = Players.Count(x => !x.Value.ProfileId.StartsWith("pmc") && x.Value.HealthController.IsAlive);
            var numberOfDeadAI = Players.Count(x => !x.Value.ProfileId.StartsWith("pmc") && !x.Value.HealthController.IsAlive);
            rect.y += 15;
            GUI.Label(rect, $"Live AI: {numberOfLiveAI}");
            rect.y += 15;
            GUI.Label(rect, $"Dead AI: {numberOfDeadAI}");

            OnGUI_DrawPlayerList(rect);
        }

        private void OnGUI_DrawPlayerList(Rect rect)
        {
            if (!PluginConfigSettings.Instance.CoopSettings.SETTING_DEBUGShowPlayerList)
                return;

            rect.y += 15;

            if (PlayersToSpawn.Any(p => p.Value != ESpawnState.Spawned))
            {
                GUI.Label(rect, $"Spawning Players:");
                rect.y += 15;
                foreach (var p in PlayersToSpawn.Where(p => p.Value != ESpawnState.Spawned))
                {
                    GUI.Label(rect, $"{p.Key}:{p.Value}");
                    rect.y += 15;
                }
            }

            if (Singleton<GameWorld>.Instance != null)
            {
                var players = Singleton<GameWorld>.Instance.RegisteredPlayers.ToList();
                players.AddRange(Players.Values);

                rect.y += 15;
                GUI.Label(rect, $"Players [{players.Count}]:");
                rect.y += 15;
                foreach (var p in players)
                {
                    GUI.Label(rect, $"{p.Profile.Nickname}:{(p.IsAI ? "AI" : "Player")}:{(p.HealthController.IsAlive ? "Alive" : "Dead")}");
                    rect.y += 15;
                }

                players.Clear();
                players = null;
            }
        }
    }

    public enum ESpawnState
    {
        None = 0,
        Loading = 1,
        Spawning = 2,
        Spawned = 3,
        Ignore = 98,
        Error = 99,
    }

    
}
