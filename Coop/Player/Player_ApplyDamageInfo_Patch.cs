using Comfort.Common;
using EFT;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SIT.Core.Coop.Player
{
    public class Player_ApplyDamageInfo_Patch : ModuleReplicationPatch
    {
        //private static ConcurrentDictionary<string, long> ProcessedCalls = new();
        public static List<string> CallLocally = new();
        public override Type InstanceType => typeof(EFT.Player);
        public override string MethodName => "ApplyDamageInfo";

        //public override bool DisablePatch => true;

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetMethodForType(InstanceType, MethodName);
        }

        public static Dictionary<string, EDamageType> LastDamageTypes = new();

        [PatchPrefix]
        public static bool PrePatch(EFT.Player __instance
            , ref DamageInfo damageInfo
            , EBodyPart bodyPartType)
        {

            //var result = false;
            //if (CallLocally.Contains(__instance.ProfileId))
            //    result = true;

            //if (!LastDamageTypes.ContainsKey(__instance.ProfileId))
            //    LastDamageTypes.Add(__instance.ProfileId, EDamageType.Undefined);

            //if (result)
            //{
            //    if (PluginConfigSettings.Instance != null)
            //    {
            //        if (PluginConfigSettings.Instance.CoopSettings.SETTING_HeadshotsAlwaysKill)
            //        {
            //            if (bodyPartType == EBodyPart.Head && damageInfo.DamageType == EFT.EDamageType.Bullet)
            //            {
            //                if (damageInfo.DidArmorDamage == 0)
            //                {
            //                    damageInfo.Damage = 999;
            //                    damageInfo.DidBodyDamage = 999;
            //                }
            //            }
            //        }
            //    }


            //    LastDamageTypes[__instance.ProfileId] = damageInfo.DamageType;
            //}
            //return result;

            var result = true;
            var selfId = __instance.ProfileId;
            var aggressorId = damageInfo.Player.iPlayer.ProfileId;
            // Don't allow self-damage (protects from grenade bullshit)
            if (selfId == aggressorId)
            {
                result = false;
            };
            // Don't allow player damage (protects from frustration)
            if (selfId.StartsWith("pmc") &&
                aggressorId.StartsWith("pmc"))
            {
                result = false;
            };

            return result;
        }

        //[PatchPostfix]
        //public static void PostPatch(
        //   EFT.Player __instance,
        //    ref DamageInfo damageInfo
        //    , EBodyPart bodyPartType, float absorbed, EHeadSegment? headSegment = null
        //    )
        //{
        //    var player = __instance;

        //    if (CallLocally.Contains(player.ProfileId))
        //    {
        //        CallLocally.Remove(player.ProfileId);
        //        return;
        //    }

        //    if (PluginConfigSettings.Instance != null)
        //    {
        //        if (PluginConfigSettings.Instance.CoopSettings.SETTING_HeadshotsAlwaysKill)
        //        {
        //            if (bodyPartType == EBodyPart.Head && damageInfo.DamageType == EFT.EDamageType.Bullet)
        //            {
        //                if (damageInfo.DidArmorDamage == 0)
        //                {
        //                    damageInfo.Damage = 999;
        //                    damageInfo.DidBodyDamage = 999;
        //                }
        //            }
        //        }
        //    }

        //    // Test.
        //    // Lets see if we can only run this from a server only perspective
        //    // 
        //    //if (MatchmakerAcceptPatches.IsClient)
        //    //    return;

        //    //Dictionary<string, object> packet = new();
        //    //var bodyPartColliderType = ((BodyPartCollider)damageInfo.HittedBallisticCollider).BodyPartColliderType;
        //    //damageInfo.HitCollider = null;
        //    //damageInfo.HittedBallisticCollider = null;
        //    //Dictionary<string, string> playerDict = new();
        //    //try
        //    //{
        //    //    if (damageInfo.Player != null)
        //    //    {
        //    //        //playerDict.Add("d.p.aid", damageInfo.Player.Profile.AccountId);
        //    //        playerDict.Add("d.p.aid", damageInfo.Player.iPlayer.Profile.AccountId);
        //    //        playerDict.Add("d.p.id", damageInfo.Player.iPlayer.ProfileId);
        //    //    }
        //    //}
        //    //catch (Exception e)
        //    //{
        //    //    Logger.LogError(e);
        //    //}
        //    //damageInfo.Player = null;
        //    //Dictionary<string, string> weaponDict = new();

        //    ////Dictionary<string, object> packet = new();
        //    ////damageInfo.HitCollider = null;
        //    ////damageInfo.HittedBallisticCollider = null;
        //    ////Dictionary<string, string> playerDict = new();
        //    ////try
        //    ////{
        //    ////    if (damageInfo.Player != null)
        //    ////    {
        //    ////        playerDict.Add("d.p.aid", damageInfo.Player.iPlayer.Profile.AccountId);
        //    ////        playerDict.Add("d.p.id", damageInfo.Player.iPlayer.ProfileId);
        //    ////    }
        //    ////}
        //    ////catch (Exception e)
        //    ////{
        //    ////    Logger.LogError(e);
        //    ////}
        //    ////damageInfo.Player = null;
        //    ////Dictionary<string, string> weaponDict = new();

        //    //packet.Add("d", SerializeObject(damageInfo));
        //    //packet.Add("d.p", playerDict);
        //    //packet.Add("d.w", weaponDict);
        //    //packet.Add("bpt", bodyPartType.ToString());
        //    //packet.Add("bpct", bodyPartColliderType.ToString());
        //    //packet.Add("ab", absorbed.ToString());
        //    //packet.Add("hs", headSegment.ToString());
        //    //packet.Add("m", "ApplyDamageInfo");
        //    //AkiBackendCommunicationCoopHelpers.PostLocalPlayerData(player, packet, true);


        //    // ---------------------------- KILL ------------------------------
        //    //if (MatchmakerAcceptPatches.IsServer)  // should we do this on the server and send out to others only?
        //    //    {
        //    //        var bodyPartHealth = player.ActiveHealthController.GetBodyPartHealth(bodyPartType);
        //    //        if (
        //    //            ((bodyPartType == EBodyPart.Head || bodyPartType == EBodyPart.Common || bodyPartType == EBodyPart.Chest) && bodyPartHealth.AtMinimum)
        //    //            || !player.ActiveHealthController.IsAlive
        //    //            || !player.PlayerHealthController.IsAlive
        //    //            )
        //    //        {
        //    //            packet = new();
        //    //            packet.Add("accountId", player.Profile.AccountId);
        //    //            packet.Add("serverId", CoopGameComponent.GetServerId());
        //    //            packet.Add("t", DateTime.Now.Ticks.ToString("G"));
        //    //            packet.Add("dmt", damageInfo.DamageType.ToString());
        //    //            packet.Add("m", "Kill");
        //    //            AkiBackendCommunication.Instance.SendDataToPool(packet.ToJson());
        //    //        }
        //    //    }
        //}

        public override void Replicated(EFT.Player player, Dictionary<string, object> dict)
        {
            //Logger.LogDebug($"Player_ApplyDamageInfo_Patch:Replicated:{player.GetType().Name}");
            var coopPlayer = player as CoopPlayer;
            if (coopPlayer != null)
            {
                coopPlayer.ReceiveDamageFromServer(dict);
            }
            //if (player == null)
            //    return;

            //if (dict == null)
            //    return;

            //if (HasProcessed(GetType(), player, dict))
            //    return;

            //if (CallLocally.Contains(player.ProfileId))
            //    return;

            //if (player is CoopPlayer coopPlayer)
            //{
            //    Enum.TryParse<EBodyPart>(dict["bpt"].ToString(), out var bodyPartType);
            //    Enum.TryParse<EBodyPartColliderType>(dict["bpct"].ToString(), out var bodyPartColliderType);
            //    Enum.TryParse<EHeadSegment>(dict["hs"].ToString(), out var headSegment);
            //    var absorbed = float.Parse(dict["ab"].ToString());

            //    var damageInfo = Player_ApplyShot_Patch.BuildDamageInfoFromPacket(dict);
            //    damageInfo.HittedBallisticCollider = Player_ApplyShot_Patch.GetBodyPartCollider(player, bodyPartColliderType);
            //    damageInfo.HitCollider = Player_ApplyShot_Patch.GetCollider(player, bodyPartColliderType);

            //    CallLocally.Add(player.ProfileId);
            //    player.ApplyDamageInfo(damageInfo, bodyPartType, absorbed, headSegment);
            //    player.ShotReactions(damageInfo, bodyPartType);
            //}

            //if (player.HealthController == null)
            //    return;

            //if (player.TryGetComponent<PlayerReplicatedComponent>(out var prc))
            //{
            //    if (!player.HealthController.IsAlive && prc.IsClientDrone)
            //    {
            //        if (player.HandsController is EFT.Player.FirearmController firearmCont)
            //        {
            //            if (firearmCont == null)
            //                return;

            //            if (firearmCont.WeaponSoundPlayer == null)
            //                return;

            //            var methodStopFiringLoop = ReflectionHelpers.GetMethodForType(firearmCont.GetType(), "StopFiringLoop");
            //            if (methodStopFiringLoop != null)
            //                methodStopFiringLoop.Invoke(firearmCont, new object[] { });
            //        }
            //    }
            //}
        }
    }
}

