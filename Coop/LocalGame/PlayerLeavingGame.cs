using SIT.Coop.Core.Matchmaker;
using SIT.Coop.Core.Player;
using SIT.Core.SP.PlayerPatches;
using SIT.Tarkov.Core;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SIT.Core.Coop.LocalGame
{
    internal class Player_LeavingGame_Patch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return OfflineSaveProfile.GetMethod();
        }

        [PatchPostfix]
        public static void Postfix(string profileId)
        {
            //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}: PlayerLeavingGame Postfix starting");

            

            if (CoopGameComponent.TryGetCoopGameComponent(out var component))
            {
                //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     PLGPF found coopGC {component}");

                // Notify that I have left the Server
                var request = new System.Collections.Generic.Dictionary<string, object>() {
                    { "m", "PlayerLeft" },
                    { "accountId", component.Players.FirstOrDefault(x=>x.Value.ProfileId == profileId).Value.Profile.AccountId },
                    { "serverId", CoopGameComponent.GetServerId() }
                };
                //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     PLGPF notifying we left server: {request}");
                Request.Instance.PostDownWebSocketImmediately(request);

                // If I am the Host/Server, then ensure all the bots have left too
                if (MatchmakerAcceptPatches.IsServer)
                {
                    //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     PLGPF we are the server, dropping all players");
                    foreach (var p in component.Players)
                    {
                        Request.Instance.PostDownWebSocketImmediately(new System.Collections.Generic.Dictionary<string, object>() {
                            { "m", "PlayerLeft" },
                            { "accountId", p.Value.Profile.AccountId },
                            { "serverId", CoopGameComponent.GetServerId() }
                        });
                    }
                    //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     PLGPF done dropping players");
                }

                //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     PLGPF dismantling coopGC now");
                foreach (var p in component.Players)
                {
                    if (p.Value == null)
                        continue;

                    if(p.Value.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                    {
                        GameObject.Destroy(prc);
                    }
                }

                if (component != null)
                {
                    foreach(var prc in GameObject.FindObjectsOfType<PlayerReplicatedComponent>())
                    {
                        GameObject.DestroyImmediate(prc);
                    }
                    //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     PLGPF final destruction");
                    GameObject.DestroyImmediate(component);
                }
            }
            else
            {
                //Logger.LogError($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     PLGPF could not find coopGC!");
            }

            //Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}: PlayerLeavingGame Postfix exiting");
        }
    }
}
