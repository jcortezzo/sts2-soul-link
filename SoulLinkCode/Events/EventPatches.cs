using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Events;

/// <summary>Tracks event pages: joins each linked page when it's shown and counts every player's choices.</summary>
[HarmonyPatch]
internal static class EventTrackingPatches
{
    /// <summary>The event room shows a page's options here, when the event starts and after every choice.</summary>
    [HarmonyPatch(typeof(NEventRoom), "SetOptions")]
    [HarmonyPostfix]
    private static void JoinPage()
    {
        EventLinks.JoinCurrent();
        EventExecutor.Schedule();
    }

    /// <summary>Every player's choice in an event they don't vote on goes through here, on every peer.</summary>
    [HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent")]
    [HarmonyPrefix]
    private static void CountChoice(EventSynchronizer __instance, Player player)
    {
        EventLinks.CountChoice(__instance, player);
        EventExecutor.Schedule();
    }

    [HarmonyPatch(typeof(NEventOptionButton), nameof(NEventOptionButton.AnimateIn))]
    [HarmonyPostfix]
    private static void RefreshAfterAnimation(NEventOptionButton __instance) => EventView.AfterAnimateIn(__instance);

    /// <summary>An Ancient's options fade in once its dialogue ends.</summary>
    [HarmonyPatch(typeof(NAncientEventLayout), "AnimateButtonsIn")]
    [HarmonyPostfix]
    private static void RefreshAfterDialogue() => EventView.Refresh();

    /// <summary>Shows teammates' linked votes and choices with the game's own vote portraits.</summary>
    [HarmonyPatch(typeof(NEventOptionButton), "ShouldDisplayPlayerVote")]
    [HarmonyPostfix]
    private static void ShowLinkedVote(NEventOptionButton __instance, Player player, ref bool __result)
    {
        if (!__result)
        {
            __result = EventView.ShowsVote(__instance, player.NetId);
        }
    }
}

/// <summary>
/// Sends clicks on linked event options to the host instead of letting the game act on them: a vote with everyone
/// agrees, a choice with first click. <see cref="EventExecutor"/> then takes whatever the host commits.
/// </summary>
[HarmonyPatch]
internal static class EventGatePatches
{
    [HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked))]
    [HarmonyPrefix]
    private static bool GateOption(NEventRoom __instance, EventOption option, int index)
    {
        if (ClickGate.TryConsumeApproval(__instance)
            || option.IsLocked
            || SoulLinkSession.Current is not { Run: not null }
            || !ReferenceEquals(EventExecutor.Room(), __instance)
            || EventLinks.LocalEvent() is not { } ev
            || !EventLinks.IsLinked(ev)
            || index < 0
            || index >= ev.CurrentOptions.Count
            || !ReferenceEquals(ev.CurrentOptions[index], option)
            || EventLinks.TableId(ev) is not { } table)
        {
            return true;
        }

        var key = OptionKey.Slot(index + 1);
        if (EventExecutor.IsGivenBack(table) || EventExecutor.TakeCommittedClick(table, key))
        {
            return true;
        }

        EventExecutor.Click(ev, table, key);
        return false;
    }
}
