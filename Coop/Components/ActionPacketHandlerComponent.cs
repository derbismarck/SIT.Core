using BepInEx.Logging;
using Comfort.Common;
using EFT;
using SIT.Coop.Core.Matchmaker;
using SIT.Coop.Core.Player;
using SIT.Core.Coop.World;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SIT.Core.Coop.Components
{
    public class ActionPacketHandlerComponent : MonoBehaviour
    {
        public BlockingCollection<Dictionary<string, object>> ActionPackets { get; } = new();
        public BlockingCollection<Dictionary<string, object>> ActionPacketsMovement { get; } = new();
        public ConcurrentDictionary<string, EFT.Player> Players => CoopGameComponent.Players;
        public ManualLogSource Logger { get; private set; }

        private List<string> RemovedFromAIPlayers = new();
        
        private CoopGame CoopGame { get; } = (CoopGame)Singleton<AbstractGame>.Instance;

        private CoopGameComponent CoopGameComponent { get; set; }

        void Awake()
        {
            // ----------------------------------------------------
            // Create a BepInEx Logger for ActionPacketHandlerComponent
            Logger = BepInEx.Logging.Logger.CreateLogSource("ActionPacketHandlerComponent");
            Logger.LogDebug("Awake");

            CoopGameComponent = CoopPatches.CoopGameComponentParent.GetComponent<CoopGameComponent>();

        }

        void Start()
        {
            CoopGameComponent = CoopPatches.CoopGameComponentParent.GetComponent<CoopGameComponent>();
        }

        void Update()
        {
            ProcessActionPackets();
        }

        private void ProcessActionPackets()
        {
            if (CoopGameComponent == null)
            {
                if (CoopPatches.CoopGameComponentParent != null)
                {
                    CoopGameComponent = CoopPatches.CoopGameComponentParent.GetComponent<CoopGameComponent>();
                    if (CoopGameComponent == null)
                        return;
                }
            }

            if (Singleton<GameWorld>.Instance == null)
                return;

            if (ActionPackets == null)
                return;

            if (Players == null)
                return;

            if (ActionPackets.Count > 0)
            {
                while (ActionPackets.TryTake(out var result))
                {
                    ProcessLastActionDataPacket(result);
                }
            }

            if (ActionPacketsMovement.Count > 0)
            {
                while (ActionPacketsMovement.TryTake(out var result))
                {
                    ProcessLastActionDataPacket(result);
                }
            }

            return;
        }

        void ProcessLastActionDataPacket(Dictionary<string, object> packet)
        {
            if (Singleton<GameWorld>.Instance == null)
                return;

            if (packet == null || packet.Count == 0)
            {
                Logger.LogInfo("No Data Returned from Last Actions!");
                return;
            }

            ProcessPlayerPacket(packet);
            ProcessWorldPacket(ref packet);

        }

        void ProcessWorldPacket(ref Dictionary<string, object> packet)
        {
            if (packet.ContainsKey("profileId"))
                return;

            if (!packet.ContainsKey("m"))
                return;

            foreach (var coopPatch in CoopPatches.NoMRPPatches)
            {
                var imrwp = coopPatch as IModuleReplicationWorldPatch;
                if (imrwp != null)
                {
                    if (imrwp.MethodName == packet["m"].ToString())
                    {
                        imrwp.Replicated(ref packet);
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
                case "KeycardDoor_Interact":
                    KeycardDoor_Interact_Patch.Replicated(packet);
                    break;
                case "Switch_Interact":
                    Switch_Interact_Patch.Replicated(packet);
                    break;

            }
        }


        void ProcessPlayerPacket(Dictionary<string, object> packet)
        {

            if (packet == null)
                return;

            if (!packet.ContainsKey("profileId"))
                return;

            var profileId = packet["profileId"].ToString();

            if (Players == null)
            {
                Logger.LogDebug("Players is Null");
                return;
            }

            if (Players.Count == 0)
            {
                Logger.LogDebug("Players is Empty");
                return;
            }

            //var registeredPlayers = Singleton<GameWorld>.Instance.RegisteredPlayers;

            //if (!Players.Any(x => x.Key == profileId) && !registeredPlayers.Any(x => x.ProfileId == profileId))
            //{
            //    // Start a new thread that waits for the localplayer to exist to send death events about them
            //    if (packet["m"].ToString() == "Kill")
            //    {
            //        Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}: Received kill packet for null player with ID {profileId}, enqueuing death");
            //        Task.Run(async () => await WaitForPlayerAndProcessPacket(profileId, packet));
            //    }
            //    return;
            //}

            foreach (var plyr in
                Players
                .Where(x => x.Key == profileId)
                .Where(x => x.Value != null)
                )
            {
                if (plyr.Value.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                {
                    prc.ProcessPacket(packet);
                }
                else
                {
                    Logger.LogError($"Player {profileId} doesn't have a PlayerReplicatedComponent!");
                }

                if (packet.ContainsKey("Extracted"))
                {
                    if (CoopGame != null)
                    {
                        //Logger.LogInfo($"Received Extracted ProfileId {packet["profileId"]}");
                        if (!CoopGame.ExtractedPlayers.Contains(packet["profileId"].ToString()))
                            CoopGame.ExtractedPlayers.Add(packet["profileId"].ToString());

                        if (!MatchmakerAcceptPatches.IsClient)
                        {
                            var botController = (BotControllerClass)ReflectionHelpers.GetFieldFromTypeByFieldType(typeof(BaseLocalGame<GamePlayerOwner>), typeof(BotControllerClass)).GetValue(Singleton<AbstractGame>.Instance);
                            if (botController != null)
                            {
                                if (!RemovedFromAIPlayers.Contains(plyr.Key))
                                {
                                    RemovedFromAIPlayers.Add(plyr.Key);
                                    Logger.LogDebug("Removing Client Player to Enemy list");
                                    var botSpawner = (BotSpawner)ReflectionHelpers.GetFieldFromTypeByFieldType(typeof(BotControllerClass), typeof(BotSpawner)).GetValue(botController);
                                    botSpawner.DeletePlayer(plyr.Value);
                                }
                            }
                        }
                    }
                }
            }

        }

        async Task WaitForPlayerAndProcessPacket(string profileId, Dictionary<string, object> packet)
        {
            // Start the timer.
            var startTime = DateTime.Now;
            var maxWaitTime = TimeSpan.FromMinutes(2);

            while (true)
            {
                // Check if maximum wait time has been reached.
                if (DateTime.Now - startTime > maxWaitTime)
                {
                    Logger.LogError($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}: WaitForPlayerAndProcessPacket waited for {maxWaitTime.TotalMinutes} minutes, but player {profileId} still did not exist after timeout period.");
                    return;
                }

                if (Players == null)
                    continue;

                var registeredPlayers = Singleton<GameWorld>.Instance.RegisteredPlayers;

                // If the player now exists, process the packet and end the thread.
                if (Players.Any(x => x.Key == profileId) || registeredPlayers.Any(x => x.Profile.ProfileId == profileId))
                {
                    // Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}: WaitForPlayerAndProcessPacket waited for {(DateTime.Now - startTime).TotalSeconds}s");
                    ProcessPlayerPacket(packet);
                    return;
                }

                // Wait for a short period before checking again.
                await Task.Delay(1000);
            }
        }
    }
}
