using SoulLink.Core.Decisions;

namespace SoulLink.Core.Rewards;

/// <summary>Option keys on card reward screens.</summary>
public static class CardRewardKeys
{
    public static OptionKey Skip => OptionKey.Skip;

    /// <summary>
    /// Pael's Wing: sacrificing a card reward is linked only among Pael's Wing holders, separately from the card pick.
    /// </summary>
    public static OptionKey Sacrifice { get; } = OptionKey.Separate("PAELS_WING");

    /// <summary>A card, counted from 1 left to right.</summary>
    public static OptionKey Card(int slot) => OptionKey.Slot(slot);

    /// <summary>An extra card only some players get, like Lasting Candy's bonus Power.</summary>
    public static OptionKey Extra(string source) => OptionKey.Extra(source);
}

/// <summary>What one player is offered on one card reward screen.</summary>
/// <param name="Cards">How many regular cards the screen shows.</param>
/// <param name="Extras">Sources of extra cards on this screen, like <c>LASTING_CANDY</c>.</param>
/// <param name="CanReroll">Whether the player can reroll this screen, for example with Driftwood.</param>
/// <param name="CanSacrifice">Whether the player can sacrifice this screen with Pael's Wing.</param>
public sealed record CardRewardOffer(int Cards, IReadOnlyList<string>? Extras = null, bool CanReroll = false, bool CanSacrifice = false)
{
    internal IEnumerable<DecisionOption> ToOptions()
    {
        for (var slot = 1; slot <= Cards; slot++)
        {
            yield return new DecisionOption(CardRewardKeys.Card(slot));
        }

        foreach (var source in Extras ?? [])
        {
            yield return new DecisionOption(CardRewardKeys.Extra(source));
        }

        if (CanSacrifice)
        {
            yield return new DecisionOption(CardRewardKeys.Sacrifice);
        }

        yield return new DecisionOption(CardRewardKeys.Skip);
    }
}

/// <summary>
/// The card rewards after one fight. Screens are matched in order: screen 1 is everyone's first card reward, and
/// screen n is linked among the players who have at least n (Prayer Wheel, Dream Catcher, The Hunt).
/// </summary>
public sealed class CardRewardRound
{
    private readonly List<CardRewardScreen> _screens = [];

    public CardRewardRound(LinkMode mode)
    {
        Mode = mode;
    }

    public LinkMode Mode { get; }

    public IReadOnlyList<CardRewardScreen> Screens => _screens;

    /// <summary>Adds a player's card reward screens in the order the game lists them.</summary>
    public void Join(PlayerId player, IReadOnlyList<CardRewardOffer> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        for (var i = 0; i < screens.Count; i++)
        {
            if (i == _screens.Count)
            {
                _screens.Add(new CardRewardScreen(i + 1, Mode));
            }

            _screens[i].Join(player, screens[i]);
        }
    }

    /// <summary>The n-th card reward screen, counted from 1.</summary>
    public CardRewardScreen Screen(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, _screens.Count);
        return _screens[number - 1];
    }
}

/// <summary>
/// One card reward screen shared by the players who have it: same slot or everyone skips. Adds linked Driftwood
/// rerolls on top of the plain <see cref="OptionDecision"/>.
/// </summary>
public sealed class CardRewardScreen
{
    private readonly Dictionary<PlayerId, RerollState> _rerolls = new();

    /// <param name="number">Which of each player's card rewards this is, counted from 1.</param>
    /// <param name="mode">Nice or mean, from the settings.</param>
    public CardRewardScreen(int number, LinkMode mode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        Number = number;
        Decision = new OptionDecision(mode);
    }

    /// <summary>Which of each player's card rewards this is, counted from 1.</summary>
    public int Number { get; }

    public OptionDecision Decision { get; }

    public IReadOnlyList<PlayerId> Participants => Decision.Participants;

    /// <summary>Checks a pick, including any reroll the player owes first.</summary>
    public CommitCheck CanCommit(PlayerId player, OptionKey option)
    {
        if (_rerolls.TryGetValue(player, out var state) && state.Owed.Count > 0 && !Decision.HasDecided(player))
        {
            return CommitCheck.MustReroll(state.Owed[0]);
        }

        return Decision.CanCommit(player, option);
    }

    /// <exception cref="InvalidOperationException">The pick isn't allowed; see <see cref="CanCommit"/>.</exception>
    public Commit<OptionKey> Commit(PlayerId player, OptionKey option)
    {
        var check = CanCommit(player, option);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't choose {option}: {check}.");
        }

        return Decision.Commit(player, option);
    }

    /// <summary>How many rerolls the player has to make before picking.</summary>
    public int RerollsOwed(PlayerId player) => _rerolls.TryGetValue(player, out var state) ? state.Owed.Count : 0;

    /// <summary>
    /// Records a reroll and the player's new offer. A reroll the player owed settles one debt. Any other reroll makes
    /// every other undecided player who can reroll owe one. A reroll isn't a commit: the first pick still decides
    /// the slot.
    /// </summary>
    /// <returns>The players who now owe a reroll.</returns>
    /// <exception cref="InvalidOperationException">The player already picked or can't reroll.</exception>
    public IReadOnlyList<PlayerId> Reroll(PlayerId player, CardRewardOffer newOffer)
    {
        ArgumentNullException.ThrowIfNull(newOffer);
        if (!_rerolls.TryGetValue(player, out var state))
        {
            throw new ArgumentException($"{player} isn't on this screen.", nameof(player));
        }

        if (Decision.HasDecided(player) || !state.CanReroll)
        {
            throw new InvalidOperationException($"{player} can't reroll this screen.");
        }

        var settlesDebt = state.Owed.Count > 0;
        if (settlesDebt)
        {
            state.Owed.RemoveAt(0);
        }

        state.CanReroll = newOffer.CanReroll;
        if (!state.CanReroll)
        {
            state.Owed.Clear();
        }

        Decision.UpdateOptions(player, newOffer.ToOptions());
        if (settlesDebt)
        {
            return [];
        }

        var owing = new List<PlayerId>();
        foreach (var other in Decision.Participants)
        {
            var otherState = _rerolls[other];
            if (other != player && otherState.CanReroll && !Decision.HasDecided(other))
            {
                otherState.Owed.Add(player);
                owing.Add(other);
            }
        }

        return owing;
    }

    /// <summary>Adds a player who has this screen, with what it offers them.</summary>
    public void Join(PlayerId player, CardRewardOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        Decision.Join(player, offer.ToOptions());
        _rerolls[player] = new RerollState { CanReroll = offer.CanReroll };
    }

    private sealed class RerollState
    {
        public bool CanReroll { get; set; }

        public List<PlayerId> Owed { get; } = [];
    }
}
