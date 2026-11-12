using Godot;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Rewards;

/// <summary>
/// Links relic rewards on the rewards screen: taking a relic reward or leaving it is a linked choice between the
/// players who have the same relic reward, matched in order on each player's rewards screen.
/// </summary>
internal static class RelicRewardLinks
{
    private static readonly DecisionOption[] Options = [new(OptionKey.Take), new(OptionKey.Skip)];
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset() => Done.Clear();

    /// <summary>The linked table for a relic reward, or null when it isn't on the local player's current rewards screen.</summary>
    public static string? TableId(RelicReward reward)
    {
        if (CardRewardLinks.CurrentSet is not { } set || CardRewardLinks.SetPrefix(set) is not { } prefix)
        {
            return null;
        }

        var number = 0;
        foreach (var item in set.Rewards)
        {
            if (item is RelicReward)
            {
                number++;
            }

            if (ReferenceEquals(item, reward))
            {
                return $"{prefix}/relic-{number}";
            }
        }

        return null;
    }

    public static IReadOnlyList<GatedOp> JoinOps(RelicReward reward) =>
        !reward.SuccessfullySelected && TableId(reward) is { } table ? OptionLinks.JoinOps(table, Options) : [];

    /// <summary>Joins every relic reward in the set when the rewards screen opens.</summary>
    public static void JoinAll(RewardsSet set) => OptionLinks.Submit(set.Rewards.OfType<RelicReward>().SelectMany(JoinOps).ToList());

    /// <summary>
    /// Lets a click go straight to the game when taking the relic is the local player's committed choice, so the game
    /// takes it exactly once.
    /// </summary>
    public static bool TakeCommittedClick(string table)
    {
        if (Done.Contains(table) || OptionLinks.CommitOf(table)?.Choice.Contains(OptionKey.Take) != true)
        {
            return false;
        }

        Done.Add(table);
        return true;
    }

    /// <summary>The operations to skip every relic reward the local player hasn't taken or decided, for leaving the screen.</summary>
    public static IEnumerable<GatedOp> SkipOps(RewardsSet set)
    {
        if (OptionLinks.Session is not { } session)
        {
            yield break;
        }

        foreach (var reward in set.Rewards.OfType<RelicReward>().Where(reward => !reward.SuccessfullySelected))
        {
            if (TableId(reward) is not { } table)
            {
                continue;
            }

            // Skip again on a reward already voted as skipped would take the vote back.
            var decision = OptionLinks.DecisionOf(table);
            if (decision is not null && (decision.HasDecided(session.Me) || decision.VoteOf(session.Me)?.Contains(OptionKey.Skip) == true))
            {
                continue;
            }

            foreach (var op in JoinOps(reward))
            {
                yield return op;
            }

            yield return OptionLinks.Choose(table, OptionKey.Skip);
        }
    }

    /// <summary>Whether every relic reward in the set was taken or is decided as left behind.</summary>
    public static bool IsDone(RewardsSet set) =>
        set.Rewards.OfType<RelicReward>().All(reward =>
            reward.SuccessfullySelected
            || TableId(reward) is not { } table
            || OptionLinks.DecisionOf(table) is null
            || OptionLinks.CommitOf(table)?.Choice.Contains(OptionKey.Skip) == true);

    /// <summary>Takes the relic rewards the host committed the local player to take.</summary>
    public static void Pump(NRewardsScreen? screen, RewardsSet set)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        foreach (var reward in set.Rewards.OfType<RelicReward>())
        {
            if (reward.SuccessfullySelected || TableId(reward) is not { } table || Done.Contains(table)
                || OptionLinks.CommitOf(table)?.Choice.Contains(OptionKey.Take) != true
                || RewardAccess.ButtonFor(screen, reward) is not { } button)
            {
                continue;
            }

            Done.Add(table);
            MainFile.Logger.Info($"Soul Link took the relic reward {table}.");
            ClickGate.RunApproved(button, () => RewardAccess.Claim(button));
        }
    }

    /// <summary>Shows who wants each relic reward, and dims relic rewards the local player can't take.</summary>
    public static void Refresh(NRewardsScreen? screen, RewardsSet? set)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen) || set is null || OptionLinks.Session is null)
        {
            return;
        }

        foreach (var reward in set.Rewards.OfType<RelicReward>())
        {
            if (TableId(reward) is not { } table || OptionLinks.DecisionOf(table) is not { } decision
                || RewardAccess.ButtonFor(screen, reward) is not { } button)
            {
                continue;
            }

            Dim.Set(button, OptionLinks.IsBlocked(decision, OptionKey.Take));
            VoteIcons.Show(button, VoteIcons.BoundsOf(button, null), OptionLinks.Backers(decision, OptionKey.Take), VoteIcons.Placement.Right);
        }
    }
}
