using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Rewards;

/// <summary>Tracks the local player's reward screens and joins their linked card reward screens.</summary>
[HarmonyPatch]
internal static class CardRewardTrackingPatches
{
    [HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.ShowScreen))]
    [HarmonyPostfix]
    private static void TrackRewardsSet(RewardsSet set, NRewardsScreen? __result) => Guard.Run("track a rewards screen", () =>
    {
        CardRewardLinks.ShowSet(set);
        if (__result is not null && LocalContext.IsMe(set.Player))
        {
            CardRewardExecutor.TrackRewardsScreen(__result);
        }
    });

    [HarmonyPatch(typeof(CardReward), "OnSelect")]
    [HarmonyPrefix]
    private static void JoinCardReward(CardReward __instance) => Guard.Run("join a card reward", () =>
    {
        CardRewardLinks.Open(__instance);
        if (LocalContext.IsMe(__instance.Player))
        {
            CardRewardExecutor.OnOpened(__instance);
        }
    });

    /// <summary>The executor checks for picks whenever a card reward screen opens or closes.</summary>
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.ShowScreen))]
    [HarmonyPostfix]
    private static void TrackScreen(NCardRewardSelectionScreen? __result)
    {
        if (__result is null)
        {
            return;
        }

        Views.Refresh("card reward", () => LockView.Show(__result));
        __result.TreeExited += CardRewardExecutor.Schedule;
        CardRewardExecutor.Schedule();
    }

    /// <summary>The screen rebuilds its cards when it opens and after a reroll, so the indicator has to be reapplied.</summary>
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
    [HarmonyPostfix]
    private static void RefreshIndicator() => Views.Refresh("card reward", LockView.Refresh);

    /// <summary>
    /// Reroll replaces the reward's cards before it returns, so the new offer can be reported. Other players who can
    /// reroll the same screen then owe a reroll.
    /// </summary>
    [HarmonyPatch(typeof(CardReward), nameof(CardReward.Reroll))]
    [HarmonyPostfix]
    private static void ReportReroll(CardReward __instance) => Guard.Run("report a reroll", () =>
    {
        if (LocalContext.IsMe(__instance.Player))
        {
            CardRewardLinks.ReportReroll(__instance);
        }
    });

    /// <summary>Lasting Candy appends its bonus Power to the reward's cards; mark it as an extra rather than a slot.</summary>
    [HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.TryModifyCardRewardOptions))]
    [HarmonyPrefix]
    private static void CountOptions(List<CardCreationResult> rewardOptions, out int __state) => __state = rewardOptions.Count;

    [HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.TryModifyCardRewardOptions))]
    [HarmonyPostfix]
    private static void MarkBonusCard(List<CardCreationResult> rewardOptions, int __state) => Guard.Run("mark a bonus card", () =>
    {
        for (var i = __state; i < rewardOptions.Count; i++)
        {
            CardRewardLinks.MarkExtra(rewardOptions[i], CardRewardLinks.LastingCandySource);
        }
    });
}

/// <summary>
/// Sends card reward clicks to the host instead of letting the game act on them: a vote with everyone agrees, a pick
/// with first click. <see cref="CardRewardExecutor"/> then does whatever the host commits.
/// </summary>
[HarmonyPatch]
internal static class CardRewardGatePatches
{
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), "SelectCard")]
    [HarmonyPrefix]
    private static bool GateCard(NCardRewardSelectionScreen __instance, NCardHolder cardHolder) =>
        !ClickGate.SettingsPending && Guard.Gate("link a card pick", () => Card(__instance, cardHolder));

    private static bool Card(NCardRewardSelectionScreen __instance, NCardHolder cardHolder)
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

        var key = CardRewardLinks.KeyOfCard(options, index);
        return CardRewardExecutor.TakeCommittedClick(__instance, table, key)
            || ClickGate.Request(CardRewardLinks.JoinOps(reward).Append(CardRewardLinks.Choose(table, key)).ToList());
    }

    [HarmonyPatch(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected")]
    [HarmonyPrefix]
    private static bool GateAlternative(NCardRewardSelectionScreen __instance, int index) =>
        !ClickGate.SettingsPending && Guard.Gate("link a card reward button", () => Alternative(__instance, index));

    private static bool Alternative(NCardRewardSelectionScreen __instance, int index)
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

        return CardRewardExecutor.TakeCommittedClick(__instance, table, key)
            || ClickGate.Request(CardRewardLinks.JoinOps(reward).Append(CardRewardLinks.Choose(table, key)).ToList());
    }

    /// <summary>
    /// Leaving the rewards screen skips every card reward the player didn't take, so it's linked like pressing Skip.
    /// The player leaves once every card reward is done: at once with first click, or once everyone agrees.
    /// </summary>
    [HarmonyPatch(typeof(NRewardsScreen), "OnProceedButtonPressed")]
    [HarmonyPrefix]
    private static bool GateProceed(NRewardsScreen __instance, NButton __0) =>
        !ClickGate.SettingsPending && Guard.Gate("link leaving the rewards", () => Proceed(__instance, __0));

    private static bool Proceed(NRewardsScreen __instance, NButton __0)
    {
        if (ClickGate.TryConsumeApproval(__instance)
            || RewardAccess.RewardsSet(__instance) is not { } set
            || !LocalContext.IsMe(set.Player)
            || !ReferenceEquals(set, CardRewardLinks.CurrentSet)
            || SoulLinkSession.Current is not { Run: not null } session)
        {
            return true;
        }

        var ops = new List<GatedOp>();
        foreach (var reward in set.Rewards.OfType<CardReward>().Where(reward => !reward.SuccessfullySelected))
        {
            if (CardRewardLinks.TableId(reward) is not { } table)
            {
                continue;
            }

            // Skip again on a reward already voted as skipped would take the vote back.
            var decision = CardRewardLinks.ScreenOf(table)?.Decision;
            if (decision is not null && (decision.HasDecided(session.Me) || decision.VoteOf(session.Me)?.Contains(CardRewardKeys.Skip) == true))
            {
                continue;
            }

            ops.AddRange(CardRewardLinks.JoinOps(reward));
            ops.Add(CardRewardLinks.Choose(table, CardRewardKeys.Skip));
        }

        ops.AddRange(RelicRewardLinks.SkipOps(set));

        if (ops.Count == 0 && CardRewardExecutor.IsDone(set))
        {
            return true;
        }

        CardRewardExecutor.ProceedWhenDone(__instance, __0);
        if (ops.Count > 0)
        {
            ClickGate.Request(ops, accepted =>
            {
                if (!accepted)
                {
                    CardRewardExecutor.CancelProceed();
                }
            });
        }

        return false;
    }

    /// <summary>
    /// Claiming a relic reward is linked: the click is a vote to take it, or takes it for everyone with first click.
    /// Other rewards are claimed as usual.
    /// </summary>
    [HarmonyPatch(typeof(NRewardButton), "GetReward")]
    [HarmonyPrefix]
    private static bool GateRelicReward(NRewardButton __instance, ref Task __result)
    {
        if (ClickGate.TryConsumeApproval(__instance)
            || (!ClickGate.SettingsPending && Guard.Gate("link a relic reward", () => RelicReward(__instance))))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }

    private static bool RelicReward(NRewardButton button)
    {
        if (button.Reward is not RelicReward reward
            || !LocalContext.IsMe(reward.Player)
            || SoulLinkSession.Current is not { Run: not null }
            || RelicRewardLinks.TableId(reward) is not { } table
            || RelicRewardLinks.TakeCommittedClick(table))
        {
            return true;
        }

        // Clicking a relic means the player isn't leaving yet.
        CardRewardExecutor.CancelProceed();
        ClickGate.Request([.. RelicRewardLinks.JoinOps(reward), OptionLinks.Choose(table, OptionKey.Take)]);
        return false;
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

}
