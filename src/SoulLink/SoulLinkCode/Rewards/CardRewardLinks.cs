using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Rewards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Rewards;

/// <summary>
/// Maps the game's card rewards onto linked card reward screens: which table a reward belongs to, what it offers and
/// which option key each card or button is.
/// </summary>
internal static class CardRewardLinks
{
    public const string RerollId = "REROLL";

    /// <summary>Lasting Candy's bonus card, the one extra card a relic adds to reward screens.</summary>
    public const string LastingCandySource = "LASTING_CANDY";

    private static readonly ConditionalWeakTable<CardCreationResult, string> ExtraSources = new();
    private static readonly ConditionalWeakTable<RewardsSet, StrongBox<int>> SetNumbers = new();
    private static readonly Dictionary<string, int> SetsPerLocation = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Joined = new(StringComparer.Ordinal);

    /// <summary>The local player's reward set on the rewards screen.</summary>
    public static RewardsSet? CurrentSet { get; private set; }

    /// <summary>The local player's open card reward.</summary>
    public static CardReward? ActiveReward { get; private set; }

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset()
    {
        SetsPerLocation.Clear();
        Joined.Clear();
        CurrentSet = null;
        ActiveReward = null;
    }

    /// <summary>Marks a card a relic added to a reward screen, so it's linked as an extra rather than a slot.</summary>
    public static void MarkExtra(CardCreationResult card, string source) => ExtraSources.AddOrUpdate(card, source);

    /// <summary>
    /// Numbers a reward set within its room, the same way on every peer, so the room's combat rewards are set 1 for
    /// everyone.
    /// </summary>
    public static void ShowSet(RewardsSet set)
    {
        if (!LocalContext.IsMe(set.Player))
        {
            return;
        }

        CurrentSet = set;
        if (!SetNumbers.TryGetValue(set, out _))
        {
            var location = LocationOf(set);
            SetsPerLocation[location] = SetsPerLocation.GetValueOrDefault(location) + 1;
            SetNumbers.Add(set, new StrongBox<int>(SetsPerLocation[location]));
        }

        // Join every card reward screen right away, so mean mode can check this player's cards before anyone picks.
        foreach (var reward in set.Rewards.OfType<CardReward>().Where(reward => reward.IsPopulated))
        {
            Join(reward);
        }
    }

    /// <summary>Records the local player's open card reward and joins its linked screen.</summary>
    public static void Open(CardReward reward)
    {
        if (!LocalContext.IsMe(reward.Player))
        {
            return;
        }

        ActiveReward = reward;
        Join(reward);
    }

    /// <summary>The linked table for a card reward, or null when it isn't part of a known reward set.</summary>
    public static string? TableId(CardReward reward)
    {
        var set = CurrentSet;
        if (set is null || !SetNumbers.TryGetValue(set, out var setNumber) || !set.Rewards.Contains(reward))
        {
            return null;
        }

        return $"{LocationOf(set)}/rewards-{setNumber.Value}/card-{ScreenNumber(set, reward)}";
    }

    /// <summary>Joins the reward's linked screen once, with what it offers.</summary>
    public static IReadOnlyList<GatedOp> JoinOps(CardReward reward)
    {
        if (TableId(reward) is not { } table || Joined.Contains(table) || CurrentSet is not { } set)
        {
            return [];
        }

        var args = RewardTable.JoinArgs(ScreenNumber(set, reward), OfferOf(reward));
        return [new GatedOp(table, SyncActions.RewardJoin, args, Sent: () => Joined.Add(table))];
    }

    /// <summary>The option key of the card at an index of the reward's options: a slot, or an extra from a relic.</summary>
    public static OptionKey KeyOfCard(IReadOnlyList<CardCreationResult> options, int index)
    {
        if (ExtraSources.TryGetValue(options[index], out var source))
        {
            return CardRewardKeys.Extra(source);
        }

        var slot = 1;
        for (var i = 0; i < index; i++)
        {
            slot += ExtraSources.TryGetValue(options[i], out _) ? 0 : 1;
        }

        return CardRewardKeys.Card(slot);
    }

    /// <summary>
    /// The option key of a button below the cards, or null for a reroll, which isn't a commit. Skip and Pael's Wing's
    /// sacrifice both count as skipping, and so does any button another mod adds.
    /// </summary>
    public static OptionKey? KeyOfAlternative(CardRewardAlternative alternative) =>
        alternative.OptionId == RerollId ? null : CardRewardKeys.Skip;

    /// <summary>The operation that commits a key on the reward's screen, checked against the local linked state.</summary>
    public static GatedOp Commit(string table, OptionKey key) =>
        new(table, SyncActions.RewardCommit, [key.Value], state =>
            state.Find<RewardTable>(table)?.Screen.CanCommit(Me(), key));

    /// <summary>Reports a reroll with the new offer.</summary>
    public static void ReportReroll(CardReward reward)
    {
        if (SoulLinkSession.Current is { Run: not null } session && TableId(reward) is { } table)
        {
            session.Submit(table, SyncActions.RewardReroll, RewardTable.OfferArgs(OfferOf(reward)));
        }
    }

    public static CardRewardOffer OfferOf(CardReward reward)
    {
        var cards = 0;
        var extras = new List<string>();
        foreach (var option in OptionsOf(reward))
        {
            if (ExtraSources.TryGetValue(option, out var source))
            {
                extras.Add(source);
            }
            else
            {
                cards++;
            }
        }

        return new CardRewardOffer(cards, extras, reward.CanReroll);
    }

    public static IReadOnlyList<CardCreationResult> OptionsOf(CardReward reward) => RewardAccess.Cards(reward);

    private static int ScreenNumber(RewardsSet set, CardReward reward)
    {
        var number = 0;
        foreach (var item in set.Rewards)
        {
            if (item is CardReward)
            {
                number++;
            }

            if (ReferenceEquals(item, reward))
            {
                break;
            }
        }

        return number;
    }

    private static string LocationOf(RewardsSet set) => set.Player.RunState.RunLocation.ToString();

    private static SoulLink.Core.PlayerId Me() => SoulLinkSession.Current?.Me ?? default;

    private static void Join(CardReward reward)
    {
        if (SoulLinkSession.Current is not { Run: not null } session)
        {
            return;
        }

        foreach (var op in JoinOps(reward))
        {
            op.Sent?.Invoke();
            session.Submit(op.Table, op.Action, op.Args);
        }
    }
}
