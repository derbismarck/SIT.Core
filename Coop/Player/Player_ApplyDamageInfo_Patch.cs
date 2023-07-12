using EFT.InventoryLogic;
using SIT.Coop.Core.Matchmaker;
using SIT.Coop.Core.Player;
using SIT.Coop.Core.Web;
using SIT.Core.Configuration;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SIT.Core.Coop.Player
{
    public class Player_ApplyDamageInfo_Patch : ModuleReplicationPatch
    {
        //private static ConcurrentDictionary<string, long> ProcessedCalls = new();
        public static Dictionary<string, bool> CallLocally = new();
        public override Type InstanceType => typeof(EFT.Player);
        public override string MethodName => "ApplyDamageInfo";
        //public override bool DisablePatch => true;

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetMethodForType(InstanceType, MethodName);
        }

        [PatchPrefix]
        public static bool PrePatch(EFT.Player __instance
            , ref DamageInfo damageInfo
            , EBodyPart bodyPartType)
        {
            var result = true;
            if (CallLocally.TryGetValue(__instance.Profile.AccountId, out var expecting) && !expecting)
                result = false;
            if(result)
            {
                if (PluginConfigSettings.Instance != null)
                {
                    if (PluginConfigSettings.Instance.CoopSettings.SETTING_HeadshotsAlwaysKill)
                    {
                        if (bodyPartType == EBodyPart.Head && damageInfo.DamageType == EFT.EDamageType.Bullet)
                        {
                            if (damageInfo.DidArmorDamage == 0)
                            {
                                damageInfo.Damage = 999;
                                damageInfo.DidBodyDamage = 999;
                            }
                        }
                    }
                }
            }
            return result;
        }

        [PatchPostfix]
        public static void PostPatch(
           EFT.Player __instance,
            ref DamageInfo damageInfo
            , EBodyPart bodyPartType, float absorbed, EHeadSegment? headSegment = null
            )
        {
            var player = __instance;

            if (CallLocally.TryGetValue(player.Profile.AccountId, out var expecting) && expecting)
            {
                CallLocally.Remove(player.Profile.AccountId);
                return;
            }

            if (PluginConfigSettings.Instance != null)
            {
                if (PluginConfigSettings.Instance.CoopSettings.SETTING_HeadshotsAlwaysKill)
                {
                    if (bodyPartType == EBodyPart.Head && damageInfo.DamageType == EFT.EDamageType.Bullet)
                    {
                        if(damageInfo.DidArmorDamage == 0)
                        {
                            damageInfo.Damage = 999;
                            damageInfo.DidBodyDamage = 999;
                        }
                    }
                }
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

            packet.Add("t", DateTime.Now.Ticks.ToString("G"));
            packet.Add("d", SerializeObject(damageInfo));
            packet.Add("d.p", playerDict);
            packet.Add("d.w", weaponDict);
            packet.Add("bpt", bodyPartType.ToString());
            packet.Add("ab", absorbed.ToString());
            packet.Add("hs", headSegment.ToString());
            packet.Add("m", "ApplyDamageInfo");
            ServerCommunication.PostLocalPlayerData(player, packet, true);


            // ---------------------------- KILL ------------------------------
            //if (MatchmakerAcceptPatches.IsServer)  // should we do this on the server and send out to others only?
            {
                var bodyPartHealth = player.ActiveHealthController.GetBodyPartHealth(bodyPartType);
                if(
                    ((bodyPartType == EBodyPart.Head || bodyPartType == EBodyPart.Common || bodyPartType == EBodyPart.Chest) && bodyPartHealth.AtMinimum)
                    || !player.ActiveHealthController.IsAlive
                    || !player.PlayerHealthController.IsAlive
                    )
                {
                    packet = new();
                    packet.Add("accountId", player.Profile.AccountId);
                    packet.Add("serverId", CoopGameComponent.GetServerId());
                    packet.Add("t", DateTime.Now.Ticks.ToString("G"));
                    packet.Add("dmt", damageInfo.DamageType.ToString());
                    packet.Add("m", "Kill");
                    Request.Instance.SendDataToPool(packet.ToJson());
                }
            }
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
                Enum.TryParse<EHeadSegment>(dict["hs"].ToString(), out var headSegment);
                var absorbed = float.Parse(dict["ab"].ToString());

                var damageInfo = Player_ApplyShot_Patch.BuildDamageInfoFromPacket(dict);

                CallLocally.Add(player.Profile.AccountId, true);
                player.ApplyDamageInfo(damageInfo, bodyPartType, absorbed, headSegment);
                player.ShotReactions(damageInfo, bodyPartType);
            }
            catch (Exception e)
            {
                Logger.LogInfo(e);
            }

            if (player.HealthController == null)
                return;

            if (player.TryGetComponent<PlayerReplicatedComponent>(out var prc))
            {
                if (!player.HealthController.IsAlive && prc.IsClientDrone)
                {
                    if (player.HandsController is EFT.Player.FirearmController firearmCont)
                    {
                        if (firearmCont == null)
                            return;

                        if (firearmCont.WeaponSoundPlayer == null)
                            return;

                        var methodStopFiringLoop = ReflectionHelpers.GetMethodForType(firearmCont.GetType(), "StopFiringLoop");
                        if(methodStopFiringLoop != null)
                            methodStopFiringLoop.Invoke(firearmCont, new object[] { });
                    }
                }
            }
        }
    }
}

