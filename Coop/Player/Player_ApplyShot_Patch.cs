using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIT.Coop.Core.Web;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIT.Core.Coop.Player
{
    internal class Player_ApplyShot_Patch : ModuleReplicationPatch
    {
        public static Dictionary<string, bool> CallLocally = new();
        public override Type InstanceType => typeof(EFT.Player);
        public override string MethodName => "ApplyShot";

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetMethodForType(InstanceType, MethodName);
        }

        [PatchPrefix]
        public static bool PrePatch(EFT.Player __instance)
        {
            var result = true;
            if (CallLocally.TryGetValue(__instance.Profile.AccountId, out var expecting) && !expecting)
                result = false;

            return result;
        }

        [PatchPostfix]
        public static void PostPatch(
           EFT.Player __instance,
            DamageInfo damageInfo, EBodyPart bodyPartType, ShotID shotId
            )
        {
            var player = __instance;

            //Logger.LogDebug("Player_ApplyShot_Patch:PostPatch");


            if (CallLocally.TryGetValue(player.Profile.AccountId, out var expecting) && expecting)
            {
                CallLocally.Remove(player.Profile.AccountId);
                return;
            }

            Dictionary<string, object> packet = new();
            damageInfo.HitCollider = null;
            damageInfo.HittedBallisticCollider = null;
            Dictionary<string, string> playerDict = new();
            try
            {
                if (damageInfo.Player != null)
                {
                    playerDict.Add("d.p.aid", damageInfo.Player.Profile.AccountId);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
            damageInfo.Player = null;
            Dictionary<string, string> weaponDict = new();

            if (damageInfo.Weapon != null)
            {
                packet.Add("d.w.tpl", damageInfo.Weapon.TemplateId);
                packet.Add("d.w.id", damageInfo.Weapon.Id);
            }
            damageInfo.Weapon = null;

            var shotammoid_field = ReflectionHelpers.GetFieldFromType(typeof(ShotID), "string_0");
            string shotammoid = null;
            if (shotammoid_field != null)
            {
                shotammoid = shotammoid_field.GetValue(shotId).ToString();
                //Logger.LogDebug(shotammoid);
            }

            packet.Add("t", DateTime.Now.Ticks);
            packet.Add("d", SerializeObject(damageInfo));
            packet.Add("d.p", playerDict);
            packet.Add("d.w", weaponDict);
            packet.Add("bpt", bodyPartType.ToString());
            packet.Add("ammoid", shotammoid);
            packet.Add("m", "ApplyShot");
            ServerCommunication.PostLocalPlayerData(player, packet);
        }

        public override void Replicated(EFT.Player player, Dictionary<string, object> dict)
        {
            if (player == null)
                return;

            if (dict == null)
                return;

            if (HasProcessed(GetType(), player, dict))
                return;

            if (CallLocally.ContainsKey(player.Profile.AccountId))
                return;

            try
            {
                Enum.TryParse<EBodyPart>(dict["bpt"].ToString(), out var bodyPartType);

                var damageInfo = BuildDamageInfoFromPacket(dict);

                var shotId = new ShotID();
                if (dict.ContainsKey("ammoid") && dict["ammoid"] != null)
                {
                    shotId = new ShotID(dict["ammoid"].ToString(), 1);
                }

                CallLocally.Add(player.Profile.AccountId, true);
                player.ApplyShot(damageInfo, bodyPartType, shotId);
            }
            catch
            {
                //Logger.LogInfo(e);
            }
        }

        public static DamageInfo BuildDamageInfoFromPacket(Dictionary<string, object> dict)
        {
            var damageInfo = JObject.Parse(dict["d"].ToString()).ToObject<DamageInfo>();

            EFT.Player aggressorPlayer = null;
            if (dict.ContainsKey("d.p") && dict["d.p"] != null && damageInfo.Player == null)
            {
                Dictionary<string, string> playerDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(dict["d.p"].ToString());
                if (playerDict != null && playerDict.ContainsKey("d.p.aid"))
                {
                    var coopGC = CoopGameComponent.GetCoopGameComponent();
                    if (coopGC != null)
                    {
                        var accountId = playerDict["d.p.aid"];
                        if (coopGC.Players.ContainsKey(accountId))
                        {
                            aggressorPlayer = coopGC.Players[accountId];
                            damageInfo.Player = aggressorPlayer;
                        }
                        else
                        {
                            aggressorPlayer = Singleton<GameWorld>.Instance.RegisteredPlayers.FirstOrDefault(x => x.Profile.AccountId == accountId);
                            if (aggressorPlayer != null)
                                damageInfo.Player = aggressorPlayer;
                        }
                    }
                }
            }

            if (dict.ContainsKey("d.w.tpl") || dict.ContainsKey("d.w.id"))
            {
                if (aggressorPlayer != null)
                {
                    Item item = null;
                    if (!ItemFinder.TryFindItemOnPlayer(aggressorPlayer, dict["d.w.tpl"].ToString(), dict["d.w.id"].ToString(), out item))
                        ItemFinder.TryFindItemInWorld(dict["d.w.id"].ToString(), out item);

                    if (item is Weapon w)
                    {
                        damageInfo.Weapon = w;
                    }
                }
            }

            return damageInfo;
        }
    }

}
