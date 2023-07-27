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
            var result = false;
            if (CallLocally.TryGetValue(__instance.Profile.AccountId, out var expecting) && expecting)
                result = true;

            var selfId = __instance.ProfileId;
            var aggressorId = damageInfo.Player.ProfileId;
            // Don't allow self-damage (protects from grenade bullshit)
            if (selfId == aggressorId) {
                result = false;
            };
            // Don't allow player damage (protects from frustration)
            if (selfId.StartsWith("pmc") &&
                aggressorId.StartsWith("pmc")) {
                result = false;
            };

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
            var bodyPartColliderType = ((BodyPartCollider)damageInfo.HittedBallisticCollider).BodyPartColliderType;
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
            packet.Add("bpct", bodyPartColliderType.ToString());
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
                Enum.TryParse<EBodyPartColliderType>(dict["bpct"].ToString(), out var bodyPartColliderType);

                var damageInfo = BuildDamageInfoFromPacket(dict);
                damageInfo.HittedBallisticCollider = GetBodyPartCollider(player, bodyPartColliderType);
                damageInfo.HitCollider = GetCollider(player, bodyPartColliderType);

                var shotId = new ShotID();
                if (dict.ContainsKey("ammoid") && dict["ammoid"] != null)
                {
                    shotId = new ShotID(dict["ammoid"].ToString(), 1);
                }

                CallLocally.Add(player.Profile.AccountId, true);
                player.ApplyShot(damageInfo, bodyPartType, shotId);
            }
            catch (Exception e)
            {
                Logger.LogDebug(e);
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

            if (dict.ContainsKey("d.w.id"))
            {
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
                    if (createdItem is Weapon grenade) {
                        damageInfo.Weapon = grenade;
                    } else {
                        damageInfo.Weapon = createdItem;
                    }
                }
            }

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
