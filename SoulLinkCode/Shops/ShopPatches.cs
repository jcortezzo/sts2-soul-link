using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Shops;

/// <summary>Links the shop: enters it when the room loads, gates slot clicks and keeps the indicators current.</summary>
[HarmonyPatch]
internal static class ShopPatches
{
    [HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
    [HarmonyPostfix]
    private static void Enter() => ShopLinks.Schedule();

    [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Open))]
    [HarmonyPostfix]
    private static void Opened() => ShopLinks.Schedule();

    [HarmonyPatch(typeof(NMerchantInventory), "OnPurchaseCompleted")]
    [HarmonyPostfix]
    private static void Purchased() => ShopLinks.Schedule();

    [HarmonyPatch(typeof(NMerchantSlot), "OnSelected")]
    [HarmonyPrefix]
    private static bool GateSlot(NMerchantSlot __instance, ref Task __result)
    {
        if (ClickGate.TryConsumeApproval(__instance) || ShopLinks.Click(__instance))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }
}
