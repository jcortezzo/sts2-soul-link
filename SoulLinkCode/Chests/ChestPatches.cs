using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Chests;

/// <summary>Links the treasure chest's relic pick: joins it when the relics show, gates clicks and shows votes.</summary>
[HarmonyPatch]
internal static class ChestPatches
{
    [HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
    [HarmonyPostfix]
    private static void JoinChest() => ChestLinks.Schedule();

    [HarmonyPatch(typeof(NTreasureRoomRelicCollection), "PickRelic")]
    [HarmonyPrefix]
    private static bool GateRelic(NTreasureRoomRelicHolder holder) => ChestLinks.ClickRelic(holder.Index);

    [HarmonyPatch(typeof(NTreasureRoom), "OnProceedButtonPressed")]
    [HarmonyPrefix]
    private static bool GateSkip(NTreasureRoom __instance) =>
        ClickGate.TryConsumeApproval(__instance) || ChestLinks.ClickSkip(__instance);

    /// <summary>Shows linked votes on the relics with the game's own vote portraits.</summary>
    [HarmonyPatch(typeof(NTreasureRoomRelicHolder), "PlayerVotedForRelic")]
    [HarmonyPostfix]
    private static void ShowRelicVote(NTreasureRoomRelicHolder __instance, Player player, ref bool __result) =>
        __result = __result || ChestLinks.Backs(player.NetId, OptionKey.Slot(__instance.Index + 1));

    [HarmonyPatch(typeof(NTreasureRoom), "IsPlayerVotingForSkip")]
    [HarmonyPostfix]
    private static void ShowSkipVote(Player player, ref bool __result) =>
        __result = __result || ChestLinks.Backs(player.NetId, OptionKey.Skip);
}
