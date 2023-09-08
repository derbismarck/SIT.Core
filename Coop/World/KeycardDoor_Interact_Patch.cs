using EFT;
using EFT.Interactive;
using SIT.Core.Core;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIT.Core.Coop.World
{
    internal class KeycardDoor_Interact_Patch : ModulePatch
    {
        public static Type InstanceType => typeof(KeycardDoor);

        public static string MethodName => "KeycardDoor_Interact";

        public static List<string> CallLocally = new();

        static ConcurrentBag<long> ProcessedCalls = new();

        protected static bool HasProcessed(Dictionary<string, object> dict)
        {
            var timestamp = long.Parse(dict["t"].ToString());

            if (!ProcessedCalls.Contains(timestamp))
            {
                ProcessedCalls.Add(timestamp);
                return false;
            }

            return true;
        }

        public static void Replicated(Dictionary<string, object> packet)
        {
            if (HasProcessed(packet))
                return;

            Logger.LogDebug("KeycardDoor_Interact_Patch:Replicated");
            if (Enum.TryParse(packet["type"].ToString(), out EInteractionType interactionType))
            {
                WorldInteractiveObject keycardDoor;
                keycardDoor = CoopGameComponent.GetCoopGameComponent().ListOfInteractiveObjects.FirstOrDefault(x => x.Id == packet["keycardDoorId"].ToString());
                Logger.LogDebug("KeycardDoor_Interact_Patch:Replicated: Searching for correct keycardDoor...");
                if (keycardDoor != null)
                {
                    string methodName = string.Empty;
                    switch (interactionType)
                    {
                        case EInteractionType.Open:
                            methodName = "Open";
                            break;
                        case EInteractionType.Close:
                            methodName = "Close";
                            break;
                        case EInteractionType.Unlock:
                            methodName = "Unlock";
                            break;
                        case EInteractionType.Breach:
                            methodName = "Breach";
                            break;
                        case EInteractionType.Lock:
                            methodName = "Lock";
                            break;
                    }
                    Logger.LogDebug("KeycardDoor_Interact_Patch:Replicated: Invoking interaction for keycardDoor '" + keycardDoor.Id + "': '" + methodName + "' (" + interactionType + ")");
                    ReflectionHelpers.InvokeMethodForObject(keycardDoor, methodName);
                }
                else
                {
                    Logger.LogDebug("KeycardDoor_Interact_Patch:Replicated: Couldn't find KeycardDoor in at all in world?");
                }


            }
            else
            {
                Logger.LogError("KeycardDoor_Interact_Patch:Replicated:EInteractionType did not parse correctly!");
            }
        }

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetAllMethodsForType(InstanceType)
                .FirstOrDefault(x => x.Name == "Interact" && x.GetParameters().Length == 1 && x.GetParameters()[0].Name == "interactionResult");
        }

        [PatchPrefix]
        public static bool Prefix(KeycardDoor __instance)
        {
            if (CallLocally.Contains(__instance.Id))
                return true;

            return false;
        }

        [PatchPostfix]
        public static void Postfix(KeycardDoor __instance, InteractionResult interactionResult)
        {
            if (CallLocally.Contains(__instance.Id))
            {
                CallLocally.Remove(__instance.Id);
                return;
            }

            var coopGC = CoopGameComponent.GetCoopGameComponent();
            if (coopGC == null)
                return;

            Logger.LogDebug($"KeycardDoor_Interact_Patch:Postfix:KeycardDoor Id:{__instance.Id}");

            Dictionary<string, object> packet = new()
            {
                { "t", DateTime.Now.Ticks.ToString("G") },
                { "serverId", CoopGameComponent.GetServerId() },
                { "keycardDoorId", __instance.Id },
                { "type", interactionResult.InteractionType.ToString() },
                { "m", MethodName }
            };

            var packetJson = packet.SITToJson();
            Logger.LogDebug(packetJson);

            //Request.Instance.PostJsonAndForgetAsync("/coop/server/update", packetJson);
            AkiBackendCommunication.Instance.PostDownWebSocketImmediately(packet);
        }
    }
}
