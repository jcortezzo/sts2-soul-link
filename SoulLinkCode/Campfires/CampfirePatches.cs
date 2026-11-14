using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Campfires;

/// <summary>Links the campfire: arrives when its options show, gates clicks, and does the next option after each one.</summary>
[HarmonyPatch]
internal static class CampfirePatches
{
    [HarmonyPatch(typeof(NRestSiteRoom), "UpdateRestSiteOptions")]
    [HarmonyPostfix]
    private static void Arrive() => Guard.Run("arrive at a campfire", CampfireLinks.Schedule);

    [HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom.AfterSelectingOption))]
    [HarmonyPostfix]
    private static void AfterOption() => Guard.Run("follow up a campfire option", CampfireLinks.Schedule);

    [HarmonyPatch(typeof(NRestSiteButton), "SelectOption")]
    [HarmonyPrefix]
    private static bool GateOption(NRestSiteButton __instance, RestSiteOption option, ref Task __result)
    {
        if (ClickGate.TryConsumeApproval(__instance)
            || (!ClickGate.SettingsPending && Guard.Gate("link a campfire option", () => CampfireLinks.Click(option))))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }
}
