using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core.Rewards;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Rewards;

/// <summary>Tracks the local player's reward screens and joins their linked card reward screens.</summary>
[HarmonyPatch]
internal static class CardRewardTrackingPatches
{
    [HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.ShowScreen))]
    [HarmonyPostfix]
    private static void TrackRewardsSet(RewardsSet set) => CardRewardLinks.ShowSet(set);

    [HarmonyPatch(typeof(CardReward), "OnSelect")]
    [HarmonyPrefix]
    private static void JoinCardReward(CardReward __instance) => CardRewardLinks.Open(__instance);

    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.ShowScreen))]
    [HarmonyPostfix]
    private static void TrackScreen(NCardRewardSelectionScreen __result) => LockView.Show(__result);

    /// <summary>The screen rebuilds its cards when it opens and after a reroll, so the indicator has to be reapplied.</summary>
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
    [HarmonyPostfix]
    private static void RefreshIndicator() => LockView.Refresh();

    /// <summary>
    /// Reroll replaces the reward's cards before it returns, so the new offer can be reported. Other players who can
    /// reroll the same screen then owe a reroll.
    /// </summary>
    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Reroll))]
    [HarmonyPostfix]
    private static void ReportReroll(CardReward __instance)
    {
        if (LocalContext.IsMe(__instance.Player))
        {
            CardRewardLinks.ReportReroll(__instance);
        }
    }

    /// <summary>Lasting Candy appends its bonus Power to the reward's cards; mark it as an extra rather than a slot.</summary>
    [HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.TryModifyCardRewardOptions))]
    [HarmonyPrefix]
    private static void CountOptions(List<CardCreationResult> rewardOptions, out int __state) => __state = rewardOptions.Count;

    [HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.TryModifyCardRewardOptions))]
    [HarmonyPostfix]
    private static void MarkBonusCard(List<CardCreationResult> rewardOptions, int __state)
    {
        for (var i = __state; i < rewardOptions.Count; i++)
        {
            CardRewardLinks.MarkExtra(rewardOptions[i], CardRewardLinks.LastingCandySource);
        }
    }
}

/// <summary>Holds card reward picks until the host accepts them, so the first click decides.</summary>
[HarmonyPatch]
internal static class CardRewardGatePatches
{
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), "SelectCard")]
    [HarmonyPrefix]
    private static bool GateCard(NCardRewardSelectionScreen __instance, NCardHolder cardHolder)
    {
        if (ClickGate.TryConsumeApproval(__instance)
            || CardRewardLinks.ActiveReward is not { } reward
            || CardRewardLinks.TableId(reward) is not { } table)
        {
            return true;
        }

        var options = RewardAccess.Options(__instance);
        var index = IndexOf(options, cardHolder);
        if (index < 0)
        {
            return true;
        }

        var ops = CardRewardLinks.JoinOps(reward).Append(CardRewardLinks.Commit(table, CardRewardLinks.KeyOfCard(options, index))).ToList();
        return ClickGate.Request(__instance, ops, () => RewardAccess.SelectCard(__instance, cardHolder));
    }

    [HarmonyPatch(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected")]
    [HarmonyPrefix]
    private static bool GateAlternative(NCardRewardSelectionScreen __instance, int index)
    {
        if (ClickGate.TryConsumeApproval(__instance)
            || CardRewardLinks.ActiveReward is not { } reward
            || CardRewardLinks.TableId(reward) is not { } table)
        {
            return true;
        }

        var alternatives = RewardAccess.Alternatives(__instance);
        if (index < 0 || index >= alternatives.Count || CardRewardLinks.KeyOfAlternative(alternatives[index]) is not { } key)
        {
            return true;
        }

        var ops = CardRewardLinks.JoinOps(reward).Append(CardRewardLinks.Commit(table, key)).ToList();
        return ClickGate.Request(__instance, ops, () => RewardAccess.SelectAlternative(__instance, index));
    }

    /// <summary>
    /// Leaving the rewards screen skips every card reward the player didn't take, so it's linked like pressing Skip.
    /// </summary>
    [HarmonyPatch(typeof(NRewardsScreen), "OnProceedButtonPressed")]
    [HarmonyPrefix]
    private static bool GateProceed(NRewardsScreen __instance, NButton __0)
    {
        if (ClickGate.TryConsumeApproval(__instance)
            || RewardAccess.RewardsSet(__instance) is not { } set
            || !LocalContext.IsMe(set.Player)
            || !ReferenceEquals(set, CardRewardLinks.CurrentSet))
        {
            return true;
        }

        var ops = new List<GatedOp>();
        foreach (var reward in set.Rewards.OfType<CardReward>().Where(reward => !reward.SuccessfullySelected))
        {
            if (CardRewardLinks.TableId(reward) is not { } table || IsDecided(table))
            {
                continue;
            }

            ops.AddRange(CardRewardLinks.JoinOps(reward));
            ops.Add(CardRewardLinks.Commit(table, CardRewardKeys.Skip));
        }

        return ClickGate.Request(__instance, ops, () => RewardAccess.Proceed(__instance, __0));
    }

    private static int IndexOf(IReadOnlyList<CardCreationResult> options, NCardHolder holder)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (ReferenceEquals(options[i].Card, holder.CardModel))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsDecided(string table) =>
        SoulLinkSession.Current is { } session
        && session.State.Find<SoulLink.Core.Sync.RewardTable>(table)?.Screen.Decision is { } decision
        && decision.IsParticipant(session.Me)
        && decision.HasDecided(session.Me);
}
