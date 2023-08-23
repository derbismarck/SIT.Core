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
using System.Reflection;

namespace SIT.Core.Coop.Player
{
    internal class Player_ApplyShot_Patch : ModuleReplicationPatch
    {
        public static List<string> CallLocally = new();
        public override Type InstanceType => typeof(EFT.Player);
        public override string MethodName => "ApplyShot";

        public override bool DisablePatch => true;

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetMethodForType(InstanceType, MethodName);
        }

        [PatchPrefix]
        public static bool PrePatch(EFT.Player __instance, DamageInfo damageInfo)
        {
            var result = false;
            if (CallLocally.Contains(__instance.ProfileId))
                result = true;

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

        [PatchPostfix]
        public static void PostPatch(
           EFT.Player __instance,
            DamageInfo damageInfo, EBodyPart bodyPartType, ShotId shotId
            )
        {
            var player = __instance;

            //Logger.LogDebug("Player_ApplyShot_Patch:PostPatch");


            if (CallLocally.Contains(player.ProfileId))
            {
                CallLocally.Remove(player.ProfileId);
                return;
            }

            Dictionary<string, object> packet = new();
            var bodyPartColliderType = ((BodyPartCollider)damageInfo.HittedBallisticCollider).BodyPartColliderType;
            damageInfo.HitCollider = null;
            damageInfo.HittedBallisticCollider = null;
            Dictionary<string, string> playerDict = new();
            try
            {
                if (damageInfo.Player != null)
                {
                    playerDict.Add("d.p.aid", damageInfo.Player.iPlayer.Profile.AccountId);
                    playerDict.Add("d.p.id", damageInfo.Player.iPlayer.ProfileId);
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

            var shotammoid_field = ReflectionHelpers.GetFieldFromType(typeof(ShotId), "string_0");
            string shotammoid = null;
            if (shotammoid_field != null)
            {
                shotammoid = shotammoid_field.GetValue(shotId).ToString();
                //Logger.LogDebug(shotammoid);
            }

            packet.Add("d", SerializeObject(damageInfo));
            packet.Add("d.p", playerDict);
            packet.Add("d.w", weaponDict);
            packet.Add("bpt", bodyPartType.ToString());
            packet.Add("bpct", bodyPartColliderType.ToString());
            packet.Add("ammoid", shotammoid);
            packet.Add("m", "ApplyShot");
            AkiBackendCommunicationCoop.PostLocalPlayerData(player, packet);
        }

        public override void Replicated(EFT.Player player, Dictionary<string, object> dict)
        {
            if (player == null)
                return;

            if (dict == null)
                return;

            if (HasProcessed(GetType(), player, dict))
                return;

            if (CallLocally.Contains(player.ProfileId))
                return;

            try
            {
                Enum.TryParse<EBodyPart>(dict["bpt"].ToString(), out var bodyPartType);
                Enum.TryParse<EBodyPartColliderType>(dict["bpct"].ToString(), out var bodyPartColliderType);

                var damageInfo = BuildDamageInfoFromPacket(dict);
                damageInfo.HittedBallisticCollider = GetBodyPartCollider(player, bodyPartColliderType);
                damageInfo.HitCollider = GetCollider(player, bodyPartColliderType);

                var shotId = new ShotId();
                if (dict.ContainsKey("ammoid") && dict["ammoid"] != null)
                {
                    shotId = new ShotId(dict["ammoid"].ToString(), 1);
                }

                CallLocally.Add(player.ProfileId);
                player.ApplyShot(damageInfo, bodyPartType, shotId);
            }
            catch (Exception e)
            {
                Logger.LogDebug(e);
            }
        }

        public static DamageInfo BuildDamageInfoFromPacket(Dictionary<string, object> dict)
        {
            //Stopwatch sw = Stopwatch.StartNew();
            var damageInfo = JObject.Parse(dict["d"].ToString()).ToObject<DamageInfo>();
            if (dict.ContainsKey("bpct"))
            {
                if (Enum.TryParse<EBodyPartColliderType>(dict["bpct"].ToString(), out var bodyPartColliderType))
                {
                    damageInfo.BodyPartColliderType = bodyPartColliderType;
                }
            }

            //EFT.Player aggressorPlayer = null;
            if (dict.ContainsKey("d.p") && dict["d.p"] != null && damageInfo.Player == null)
            {
                Dictionary<string, string> playerDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(dict["d.p"].ToString());
                if (playerDict != null && playerDict.ContainsKey("d.p.id"))
                {
                    var coopGC = CoopGameComponent.GetCoopGameComponent();
                    if (coopGC != null)
                    {
                        var accountId = playerDict["d.p.aid"];
                        var profileId = playerDict["d.p.id"];
                        //if (coopGC.Players.ContainsKey(accountId))
                        //{
                        //    aggressorPlayer = coopGC.Players[accountId];
                        //    damageInfo.Player = aggressorPlayer;
                        //}
                        //else
                        //{
                        //    aggressorPlayer = (EFT.Player)Singleton<GameWorld>.Instance.RegisteredPlayers.FirstOrDefault(x => x.Profile.AccountId == accountId);
                        //    if (aggressorPlayer != null)
                        //        damageInfo.Player = Singleton<GameWorld>.Instance.GetAlivePlayerBridgeByProfileID(profileId);
                        //}
                        damageInfo.Player = Singleton<GameWorld>.Instance.GetAlivePlayerBridgeByProfileID(profileId);

                    }
                }
            }

            //if (dict.ContainsKey("d.w.tpl") || dict.ContainsKey("d.w.id"))
            if (dict.ContainsKey("d.w.id"))
            {
                //if (aggressorPlayer != null)
                //{
                //Item item = null;
                //if (!ItemFinder.TryFindItemOnPlayer(aggressorPlayer, dict["d.w.tpl"].ToString(), dict["d.w.id"].ToString(), out item))
                //    ItemFinder.TryFindItemInWorld(dict["d.w.id"].ToString(), out item);

                if (ItemFinder.TryFindItem(dict["d.w.id"].ToString(), out Item item))
                {
                    if (item is Weapon w)
                    {
                        damageInfo.Weapon = w;
                    }
                }
                else
                {
                    var createdItem = Tarkov.Core.Spawners.ItemFactory.CreateItem(dict["d.w.id"].ToString(), dict["d.w.tpl"].ToString());
                    if (createdItem is Weapon wep)
                    {
                        damageInfo.Weapon = wep;
                    }
                }
                //}
            }


            //Logger.LogDebug($"BuildDamageInfoFromPacket::Took::{sw.Elapsed}");

            return damageInfo;
        }

        public static BodyPartCollider GetBodyPartCollider(EFT.Player player, EBodyPartColliderType bodyPartColliderType)
        {
            // Access the _hitColliders field via Reflection
            var fieldInfo = typeof(EFT.Player).GetField("_hitColliders", BindingFlags.NonPublic | BindingFlags.Instance);
            BodyPartCollider[] hitColliders = fieldInfo.GetValue(player) as BodyPartCollider[];

            foreach (BodyPartCollider bodyPartCollider in hitColliders)
            {
                if (bodyPartCollider.BodyPartColliderType == bodyPartColliderType)
                {
                    return bodyPartCollider;
                }
            }

            // If no matching BodyPartCollider is found, return null
            return null;
        }

        public static UnityEngine.Collider GetCollider(EFT.Player player, EBodyPartColliderType bodyPartColliderType)
        {
            return GetBodyPartCollider(player, bodyPartColliderType).Collider;
        }
    }

}
