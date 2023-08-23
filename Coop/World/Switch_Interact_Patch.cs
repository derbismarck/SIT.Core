using EFT.Interactive;
using EFT;
using SIT.Core.Core;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SIT.Core.Coop.World
{
    internal class Switch_Interact_Patch : ModulePatch
    {
        public static Type InstanceType => typeof(Switch);

        public static string MethodName => "Switch_Interact";

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

            Logger.LogDebug($"Switch_Interact_Patch:Replicated");

            if (Enum.TryParse(packet["type"].ToString(), out EInteractionType interactionType))
            {
                Switch @switch;
                @switch = (Switch)CoopGameComponent.GetCoopGameComponent().ListOfInteractiveObjects.FirstOrDefault(x => x.Id == packet["doorId"].ToString());
                if (@switch != null)
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
                    ReflectionHelpers.InvokeMethodForObject(@switch, methodName);
                }
                else
                {
                    Logger.LogDebug("Switch_Interact_Patch:Replicated: Couldn't find Door in at all in world?");
                }


            }
            else
            {
                Logger.LogError("Switch_Interact_Patch:Replicated:EInteractionType did not parse correctly!");
            }
        }

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetAllMethodsForType(InstanceType)
                .FirstOrDefault(x => x.Name == "Interact" && x.GetParameters().Length == 1 && x.GetParameters()[0].Name == "interactionResult");
        }

        [PatchPrefix]
        public static bool Prefix(Switch __instance)
        {
            if (CallLocally.Contains(__instance.Id))
                return true;

            return false;
        }

        [PatchPostfix]
        public static void Postfix(Switch __instance, InteractionResult interactionResult)
        {
            if (CallLocally.Contains(__instance.Id))
            {
                CallLocally.Remove(__instance.Id);
                return;
            }

            var coopGC = CoopGameComponent.GetCoopGameComponent();
            if (coopGC == null)
                return;

            Logger.LogDebug($"Switch_Interact_Patch:Postfix:Door Id:{__instance.Id}");

            Dictionary<string, object> packet = new Dictionary<string, object>
            {
                { "t", DateTime.Now.Ticks },
                { "serverId", CoopGameComponent.GetServerId() },
                { "doorId", __instance.Id },
                { "type", interactionResult.InteractionType.ToString() },
                { "m", MethodName }
            };

            var packetJson = packet.SITToJson();
            Logger.LogDebug($"Switch_Interact_Patch:Postfix:{packetJson}");

            Request.Instance.PostDownWebSocketImmediately(packet);
        }
    }
}