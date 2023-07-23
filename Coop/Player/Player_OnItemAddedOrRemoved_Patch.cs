using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using SIT.Coop.Core.Web;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIT.Core.Coop.Player
{
    internal sealed class Player_OnItemAddedOrRemoved_Patch : ModuleReplicationPatch
    {
        public static List<string> CallLocally = new();
        public override Type InstanceType => typeof(EFT.Player);
        public override string MethodName => "OnItemAddedOrRemoved";

        protected override MethodBase GetTargetMethod()
        {
            var method = ReflectionHelpers.GetMethodForType(InstanceType, MethodName);
            return method;
        }

        [PatchPrefix]
        public static bool PrePatch(EFT.Player __instance,
            Item item, ItemAddress location, bool added
            )
        {
            //Logger.LogDebug($"OnItemAddedOrRemoved.PrePatch: {item}/{item.Template}/{item.Template is MagazineTemplate}");
            var result = false;

            if (item.Template is MagazineTemplate)
                result = true;

            return result;
        }

        [PatchPostfix]
        public static void PostPatch(
           EFT.Player __instance,
           Item item, ItemAddress location, bool added
            )
        {
            var player = __instance;

            //Logger.LogDebug($"OnItemAddedOrRemoved.PostPatch:{item.TemplateId}:{location.GetType()}:{location.ContainerName}:{added}");
            if (CallLocally.Contains(player.Profile.AccountId))
            {
                CallLocally.Remove(player.Profile.AccountId);
                return;
            }

            SlotItemAddressDescriptor slotItemAddressDescriptor = new();
            slotItemAddressDescriptor.Container = new();
            slotItemAddressDescriptor.Container.ContainerId = location.Container.ID;
            slotItemAddressDescriptor.Container.ParentId = location.Container.ParentItem != null ? location.Container.ParentItem.Id : null;

            Dictionary<string, object> dictionary = new Dictionary<string, object>
            {
                { "t", DateTime.Now.Ticks }
            };

            if (location is GridItemAddress gridItemAddress)
            {
                GridItemAddressDescriptor gridItemAddressDescriptor = new();
                gridItemAddressDescriptor.Container = new();
                gridItemAddressDescriptor.Container.ContainerId = location.Container.ID;
                gridItemAddressDescriptor.Container.ParentId = location.Container.ParentItem != null ? location.Container.ParentItem.Id : null;
                gridItemAddressDescriptor.LocationInGrid = gridItemAddress.LocationInGrid;
                dictionary.Add("grad", gridItemAddressDescriptor);
            }

            dictionary.Add("id", item.Id);
            dictionary.Add("tpl", item.TemplateId);
            dictionary.Add("sitad", slotItemAddressDescriptor);
            dictionary.Add("added", added);
            dictionary.Add("m", "OnItemAddedOrRemoved");
            ServerCommunication.PostLocalPlayerData(player, dictionary);
        }


        public override void Replicated(EFT.Player player, Dictionary<string, object> dict)
        {
            // if this is us, we've already done this and redoing will break shit
            if (player.IsYourPlayer)
                return;

            if (HasProcessed(GetType(), player, dict))
                return;

            Item item = null;

            // Get the Item and Clone it
            var itemFindResult = Singleton<GameWorld>.Instance.FindItemById(dict["id"].ToString());
            if (itemFindResult.Succeeded)
            {
                item = itemFindResult.Value;

                // Item isn't in a box?
                if(item.CurrentAddress == null || item.CurrentAddress.Container == null)
                {
                    Logger.LogInfo($"Item of Id {item.Id} isn't in a box");
                }
                //item = item.CloneItemWithSameId();
                //item = item.CloneItem();
            }


            if (item != null)
            {
                var added = bool.Parse(dict["added"].ToString());

                //Logger.LogDebug($"OnItemAddedOrRemoved.Replicated:Item[{item}]");
                var itemsInInventory = player.Profile.Inventory
                        .GetAllItemByTemplate(item.TemplateId);
                if (itemsInInventory.Any())
                {
                    if (itemsInInventory.Any(x => x.Id == item.Id))
                    {
                        //Logger.LogError($"Item of Id {item.Id} already exists in the inventory. Stopping Duplication!");
                        return;
                    }
                }

                if (added && item.CurrentAddress != null)
                    item.CurrentAddress.Remove(item, player.ProfileId);

                // Grid Item stuff
                if (dict.ContainsKey("grad"))
                {
                    //Logger.LogDebug($"Has GridItemAddressDescriptor");
                    //Logger.LogDebug($"{dict["grad"]}");
                    GridItemAddressDescriptor gridItemAddressDescriptor = PatchConstants.SITParseJson<GridItemAddressDescriptor>(dict["grad"].ToString());
                    var container1 = player.Equipment.FindContainer(gridItemAddressDescriptor.Container.ContainerId, gridItemAddressDescriptor.Container.ParentId);
                    //var container = player.Equipment.GetContainer(gridItemAddressDescriptor.Container.ContainerId);
                    if (container1 != null)
                    {
                        if (bool.Parse(dict["added"].ToString()))
                        {
                            //Logger.LogDebug($"OnItemAddedOrRemoved.Replicated:Container[{container1.GetType()}][{container1}]");
                            //((GridContainer)container1).AddItemWithoutRestrictions(item, gridItemAddressDescriptor.LocationInGrid);
                            ((GridContainer)container1).AddItem(item, gridItemAddressDescriptor.LocationInGrid, new string[] { player.ProfileId }, false);
                        }
                        else
                        {
                            Logger.LogDebug($"OnItemAddedOrRemoved.Replicated:Container[{container1.GetType()}][{container1}]:RemoveItem");
                            ((GridContainer)container1).RemoveItem(item, player.ProfileId, false);

                        }
                    }
                }
                // Slot Item stuff (Equip weapons, armor, backpack etc)
                else
                {
                    //Logger.LogDebug($"Has SlotItemAddressDescriptor");
                    //Logger.LogDebug($"{dict["sitad"]}");
                    SlotItemAddressDescriptor slotItemAddressDescriptor = PatchConstants.SITParseJson<SlotItemAddressDescriptor>(dict["sitad"].ToString());
                    var container1 = player.Equipment.FindContainer(slotItemAddressDescriptor.Container.ContainerId, slotItemAddressDescriptor.Container.ParentId);
                    if (container1 != null)
                    {
                        if (bool.Parse(dict["added"].ToString()))
                        {
                            //Logger.LogDebug($"OnItemAddedOrRemoved.Replicated:Container[{container1.GetType()}][{container1}]");
                            if (container1 is Slot slot)
                            {
                                //Logger.LogDebug($"OnItemAddedOrRemoved.Replicated:Container[{container1.GetType()}][{container1}]AddWithoutRestrictions");
                                if (slot.CanAccept(item))
                                    slot.Add(item, false);
                                    //slot.AddWithoutRestrictions(item);
                            }
                            
                        }
                        else
                        {
                            if (container1 is Slot slot)
                            {
                                //Logger.LogDebug($"OnItemAddedOrRemoved.Replicated:Container[{container1.GetType()}][{container1}]RemoveItem");
                                slot.RemoveItem();
                            }
                        }
                    }
                }

            }

        }
    }

}