using SIT.Coop.Core.Player;
using SIT.Coop.Core.Web;
using SIT.Core.Coop.NetworkPacket;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR;

namespace SIT.Core.Coop.Player.FirearmControllerPatches
{
    public class FirearmController_SetTriggerPressed_Patch : ModuleReplicationPatch
    {
        public override Type InstanceType => typeof(EFT.Player.FirearmController);
        public override string MethodName => "SetTriggerPressed";

        //public override bool DisablePatch => true;

        protected override MethodBase GetTargetMethod()
        {
            var method = ReflectionHelpers.GetMethodForType(InstanceType, MethodName);
            return method;
        }

        public static Dictionary<string, bool> CallLocally = new();

        public static Dictionary<string, bool> LastPress = new();


        [PatchPrefix]
        public static bool PrePatch(
            EFT.Player.FirearmController __instance
            , EFT.Player ____player
            , bool pressed
            )
        {
            if (CoopGameComponent.GetCoopGameComponent() == null)
                return false;

            if (CoopGameComponent.GetCoopGameComponent().HighPingMode && ____player.IsYourPlayer)
            {
                return true;
            }

            var player = ____player;
            if (player == null)
                return false;

            var result = false;
            if (CallLocally.TryGetValue(player.Profile.AccountId, out var expecting) && expecting)
                result = true;

            return result;
        }

        [PatchPostfix]
        public static void PostPatch(
            EFT.Player.FirearmController __instance
            , bool pressed
            , EFT.Player ____player
            )
        {
            var player = ____player;
            if (player == null)
                return;

            if (CallLocally.TryGetValue(player.Profile.AccountId, out var expecting) && expecting)
            {
                CallLocally.Remove(player.Profile.AccountId);
                return;
            }

            //var ticks = DateTime.Now.Ticks;
            //Dictionary<string, object> packet = new Dictionary<string, object>();
            //packet.Add("t", ticks);
            //packet.Add("pr", pressed.ToString());
            //if (pressed)
            //{
            //    packet.Add("rX", player.Rotation.x.ToString());
            //    packet.Add("rY", player.Rotation.y.ToString());
            //}
            //packet.Add("m", "SetTriggerPressed");
            //ServerCommunication.PostLocalPlayerData(player, packet);

            TriggerPressedPacket triggerPressedPacket = new TriggerPressedPacket();
            triggerPressedPacket.AccountId = player.Profile.AccountId;
            triggerPressedPacket.pr = pressed;
            if (pressed)
            {
                triggerPressedPacket.rX = player.Rotation.x;
                triggerPressedPacket.rY = player.Rotation.y;
            }
            var serialized = triggerPressedPacket.Serialize();
            //Logger.LogInfo($"SENDING: Serialized length: {serialized.Length} vs json length: {triggerPressedPacket.ToJson().Length}");
            //Logger.LogInfo($"EXPECTED RECEIVE: Serialized length: {serialized.Split('?')[1].Length} vs json length: {triggerPressedPacket.ToJson().Length}");
            //Logger.LogInfo(serialized);
            Request.Instance.SendDataToPool(serialized);
            //Request.Instance.SendDataToPool(triggerPressedPacket.ToJson());
            //Logger.LogInfo("Pressed:PostPatch");
        }


        public override void Replicated(EFT.Player player, Dictionary<string, object> dict)
        {
            if (CoopGameComponent.GetCoopGameComponent().HighPingMode && player.IsYourPlayer)
            {
                // You would have already run this. Don't bother
                return;
            }

            var taskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            taskScheduler.Do((s) =>
            {
                TriggerPressedPacket tpp = new();

                //Logger.LogInfo("Pressed:Replicated");
                if (dict.ContainsKey("data"))
                {
                    tpp = tpp.DeserializePacketSIT(dict["data"].ToString());
                    //Logger.LogInfo("packet deserialized, really? tidy!");
                    //Logger.LogInfo(tpp.ToJson());
                    //return;
                }

                if (HasProcessed(GetType(), player, tpp))
                    return;

                //if (HasProcessed(GetType(), player, dict))
                //    return;

                if (!player.TryGetComponent<PlayerReplicatedComponent>(out var prc))
                    return;

                if (CallLocally.ContainsKey(player.Profile.AccountId))
                    return;

                CallLocally.Add(player.Profile.AccountId, true);

                bool pressed = tpp.pr; // bool.Parse(dict["pr"].ToString());

                if (player.HandsController is EFT.Player.FirearmController firearmCont)
                {
                    try
                    {
                        firearmCont.SetTriggerPressed(pressed);
                        //if (pressed && dict.ContainsKey("rX"))
                        if (prc.IsClientDrone && pressed && tpp.rX != 0)
                        {
                            var rotat = new Vector2(tpp.rX, tpp.rY);
                            player.Rotation = rotat;
                        }

                        //ReplicatedShotEffects(player, pressed);


                    }
                    catch (Exception e)
                    {
                        Logger.LogInfo(e);
                    }
                }
            });
        }

        void ReplicatedShotEffects(EFT.Player player, bool pressed)
        {
            //if(pressed)
            //{
            //    var weapon = player.TryGetItemInHands<EFT.InventoryLogic.Weapon>();
            //    if (weapon != null)
            //    {
            //        // Thanks Fullstack0verflow
            //        // Check whether a bullet can be fired 
            //        if (weapon.ChamberAmmoCount > 0)
            //        {
            //            if (firearmCont.WeaponSoundPlayer == null)
            //                return;

            //            firearmCont.WeaponSoundPlayer.FireBullet(null, player.Position, UnityEngine.Vector3.zero, 1, false, weapon.FireMode.FireMode == EFT.InventoryLogic.Weapon.EFireMode.fullauto);

            //            var weaponEffectsManager
            //                = ReflectionHelpers.GetFieldFromTypeByFieldType(typeof(EFT.Player.FirearmController), typeof(WeaponEffectsManager))
            //                .GetValue(firearmCont) as WeaponEffectsManager;
            //            if (weaponEffectsManager == null)
            //                return;

            //            weaponEffectsManager.PlayShotEffects(player.IsVisible, player.Distance);
            //        }
            //        else
            //        {
            //            ReflectionHelpers.GetMethodForType(firearmCont.GetType(), "DryShot", findFirst: true)
            //                .Invoke(firearmCont, new object[] { 0, false });
            //        }
            //    }
            //}
        }

        public class TriggerPressedPacket : BasePlayerPacket
        {
            public bool pr { get; set; }
            public float rX { get; set; }
            public float rY { get; set; }

            public TriggerPressedPacket() : base()
            {
                Method = "SetTriggerPressed";
            }

        }

    }
}
