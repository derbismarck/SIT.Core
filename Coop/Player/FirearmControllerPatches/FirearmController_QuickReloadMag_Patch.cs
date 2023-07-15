using Newtonsoft.Json;
using SIT.Coop.Core.Web;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIT.Core.Coop.Player.FirearmControllerPatches
{
    public class FirearmController_QuickReloadMag_Patch : ModuleReplicationPatch
    {
        public override Type InstanceType => typeof(EFT.Player.FirearmController);
        public override string MethodName => "QuickReloadMag";

        protected override MethodBase GetTargetMethod()
        {
            var method = ReflectionHelpers.GetMethodForType(InstanceType, MethodName);
            return method;
        }

        [PatchPostfix]
        public static void Postfix(
            EFT.Player.FirearmController __instance
            , EFT.Player ____player
            , MagazineClass magazine)
        {
            Dictionary<string, object> magAddressDict = new();
            ItemAddressHelpers.ConvertItemAddressToDescriptor(magazine.CurrentAddress, ref magAddressDict);

            Dictionary<string, object> dictionary = new()
            {
                { "fa.id", __instance.Item.Id },
                { "fa.tpl", __instance.Item.TemplateId },
                { "mg.id", magazine.Id },
                { "mg.tpl", magazine.TemplateId },
                { "ma", magAddressDict },
                { "m", "ReloadMag" }
            };
            ServerCommunication.PostLocalPlayerData(____player, dictionary);

            // Quick Reload is not Round Tripped. 
            HasProcessed(typeof(FirearmController_QuickReloadMag_Patch), ____player, dictionary);
        }

        public override void Replicated(EFT.Player player, Dictionary<string, object> dict)
        {
            if (HasProcessed(GetType(), player, dict))
                return;

            Logger.LogInfo("FirearmController_QuickReloadMag_Patch:Replicated");

            if (player.HandsController is EFT.Player.FirearmController firearmCont)
            {
                try
                {

                    var ma = JsonConvert.DeserializeObject<Dictionary<string, object>>(dict["ma"].ToString());
                    ItemAddressHelpers.ConvertDictionaryToAddress(ma, out var magAddressGrid, out var magAddressSlot);

                    var magazine = player.Profile.Inventory.GetAllItemByTemplate(dict["mg.tpl"].ToString())
                        .FirstOrDefault(x => x.Id == dict["mg.id"].ToString()) as MagazineClass;
                    if (magazine == null)
                    {
                        Logger.LogError("FirearmController_QuickReloadMag_Patch:Replicated:Unable to find Magazine!");
                        return;
                    }

                    firearmCont.QuickReloadMag(magazine, (IResult) =>
                    {



                    });

                }
                catch (Exception e)
                {
                    Logger.LogError(e);
                }
            }
        }
    }
}