using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;

namespace SoulLink.Core.Campfires;

public enum CampfireCategory
{
    /// <summary>Rest and Mend.</summary>
    Recover,

    /// <summary>Smith, Dig, Cook, Lift and any other option a relic or quest adds.</summary>
    Improve,
}

/// <summary>An option the game offers a player at a campfire, by id (see <see cref="CampfireOptions"/>).</summary>
public readonly record struct CampfireOption(string Id, bool Available = true);

/// <summary>An option a player does at a campfire, by the id the game gave it.</summary>
public readonly record struct CampfireChoice(PlayerId Player, string OptionId);

/// <summary>Campfire option ids, as the game names them, and their categories.</summary>
public static class CampfireOptions
{
    /// <summary>Rest, which the game calls HEAL.</summary>
    public const string Rest = "HEAL";
    public const string Mend = "MEND";
    public const string Smith = "SMITH";
    public const string Dig = "DIG";
    public const string Cook = "COOK";
    public const string Lift = "LIFT";

    /// <summary>Rest and Mend recover; every other option improves, including ones added by relics or quests.</summary>
    public static CampfireCategory CategoryOf(string optionId) =>
        Is(optionId, Rest) || Is(optionId, "REST") || Is(optionId, Mend) ? CampfireCategory.Recover : CampfireCategory.Improve;

    /// <summary>The option key of a category in <see cref="CampfireSession.Category"/>.</summary>
    public static OptionKey KeyOf(CampfireCategory category) => OptionKey.Named(category.ToString().ToLowerInvariant());

    public static bool IsSmith(string optionId) => Is(optionId, Smith);

    private static bool Is(string optionId, string known) => string.Equals(optionId, known, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One campfire visit by the party. The category, Recover or Improve, is linked, and each player picks their own
/// option within it. Miniature Tent holders can choose any number of options and aren't part of the category. If
/// more than one player Smiths, the Smith target is linked too.
/// </summary>
/// <remarks>
/// With first click, the first player to choose sets the category and does their option at once. With everyone
/// agrees, each player's click waits until everyone has clicked an option in the same category, and then everyone's
/// option is done.
/// </remarks>
public sealed class CampfireSession
{
    private readonly Dictionary<PlayerId, Camper> _campers = new();
    private readonly List<PlayerId> _arrivals = [];

    /// <param name="mode">Nice or mean, from the settings.</param>
    /// <param name="matching">How Smith targets are matched.</param>
    /// <param name="whoDecides">Whether everyone has to agree, or the first click decides.</param>
    public CampfireSession(LinkMode mode, ICardMatchingStrategy matching, WhoDecides whoDecides = WhoDecides.EveryoneAgrees)
    {
        // Everyone picks their own option within the category, so the category is never chosen for anyone.
        Category = new OptionDecision(mode, 1, whoDecides) { FollowsAutomatically = false };
        SmithTarget = new CardChoiceDecision(matching, mode, 1, whoDecides)
        {
            IsProspect = player => !HasChosen(player, CampfireOptions.Smith),
        };
    }

    public WhoDecides WhoDecides => Category.WhoDecides;

    /// <summary>Recover or Improve, linked among the players without Miniature Tent.</summary>
    public OptionDecision Category { get; }

    /// <summary>The players at the campfire, in the order they arrived.</summary>
    public IReadOnlyList<PlayerId> Campers => _arrivals;

    /// <summary>Whether the player has arrived at the campfire.</summary>
    public bool IsAt(PlayerId player) => _campers.ContainsKey(player);

    /// <summary>
    /// Which card each player who Smiths upgrades. Everyone who could Smith joins when they arrive, so mean mode only
    /// allows targets every possible smith can follow, but nothing is decided for them until they choose Smith.
    /// Players without Miniature Tent leave it when they choose something else.
    /// </summary>
    public CardChoiceDecision SmithTarget { get; }

    /// <summary>Adds a player at the campfire.</summary>
    /// <param name="player">The player.</param>
    /// <param name="options">The options the game offers them, by id.</param>
    /// <param name="hasMiniatureTent">Whether they hold Miniature Tent.</param>
    /// <param name="upgradeScreen">Their upgrade screen, if they can Smith.</param>
    public void Join(PlayerId player, IEnumerable<CampfireOption> options, bool hasMiniatureTent, SelectionScreen? upgradeScreen = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var list = options.ToList();
        if (list.Select(option => option.Id.ToUpperInvariant()).Distinct().Count() != list.Count)
        {
            throw new ArgumentException("Each campfire option can appear only once.", nameof(options));
        }

        if (upgradeScreen is not null && upgradeScreen.Owner != player)
        {
            throw new ArgumentException("The upgrade screen belongs to another player.", nameof(upgradeScreen));
        }

        if (_campers.ContainsKey(player))
        {
            throw new ArgumentException($"{player} is already at the campfire.", nameof(player));
        }

        _campers[player] = new Camper(list, hasMiniatureTent);
        _arrivals.Add(player);
        if (!hasMiniatureTent)
        {
            Category.Join(player, [CategoryOption(list, CampfireCategory.Recover), CategoryOption(list, CampfireCategory.Improve)]);
        }

        if (upgradeScreen is not null && list.Exists(option => option.Available && CampfireOptions.IsSmith(option.Id)))
        {
            SmithTarget.Join(upgradeScreen);
        }
    }

    /// <summary>The options the player has chosen so far.</summary>
    public IReadOnlyList<string> ChosenBy(PlayerId player) => Get(player).Chosen;

    /// <summary>With everyone agrees, the option the player clicked that waits for the others, or null.</summary>
    public string? PendingOf(PlayerId player) => Get(player).Pending;

    /// <summary>Checks whether the player may choose this option now.</summary>
    public CommitCheck CanChoose(PlayerId player, string optionId)
    {
        if (!_campers.TryGetValue(player, out var camper))
        {
            return CommitCheck.NotParticipant;
        }

        if (camper.Find(optionId) is not { Available: true })
        {
            return CommitCheck.Unavailable;
        }

        if (camper.HasTent)
        {
            return camper.HasChosen(optionId) ? CommitCheck.AlreadyDecided : CommitCheck.Allowed;
        }

        return camper.Chosen.Count > 0
            ? CommitCheck.AlreadyDecided
            : Category.CanCommit(player, CampfireOptions.KeyOf(CampfireOptions.CategoryOf(optionId)));
    }

    /// <summary>
    /// A player's click on an option. Miniature Tent holders do it at once. For everyone else, first click does it at
    /// once and sets the category; everyone agrees waits until everyone has clicked an option in the same category,
    /// and clicking the same option again takes the click back. Smith targets that follow from it are committed in
    /// <see cref="SmithTarget"/>.
    /// </summary>
    /// <returns>The options done, in order: none while a click waits for the others.</returns>
    /// <exception cref="InvalidOperationException">The choice isn't allowed; see <see cref="CanChoose"/>.</exception>
    public IReadOnlyList<CampfireChoice> Choose(PlayerId player, string optionId)
    {
        var check = CanChoose(player, optionId);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't choose {optionId}: {check}.");
        }

        var choices = Click(player, optionId, vote => vote is { } key ? Category.Choose(player, key) : Category.Settle());
        SmithTarget.Settle();
        return choices;
    }

    /// <summary>
    /// Replays a click the host accepted, on a client: records it like <see cref="Choose"/>, then applies the host's
    /// category and Smith target commits instead of working them out.
    /// </summary>
    /// <returns>The options done, in order.</returns>
    public IReadOnlyList<CampfireChoice> ApplyChoose(
        PlayerId player, string optionId, IReadOnlyList<Commit<OptionKey>> categoryCommits, IReadOnlyList<Commit<CardId>> smithCommits)
    {
        ArgumentNullException.ThrowIfNull(categoryCommits);
        ArgumentNullException.ThrowIfNull(smithCommits);
        var choices = Click(player, optionId, vote =>
        {
            if (vote is { } key)
            {
                Category.ApplyChoice(player, Selection.Of(key), categoryCommits);
            }
            else
            {
                Category.ApplyAll(categoryCommits);
            }

            return categoryCommits;
        });
        SmithTarget.ApplyAll(smithCommits);
        return choices;
    }

    /// <summary>
    /// Re-checks the campfire after players arrive or leave: any category agreement that's now complete is done, and
    /// any Smith target that now follows is committed.
    /// </summary>
    /// <returns>The options done, in order.</returns>
    public IReadOnlyList<CampfireChoice> Settle()
    {
        var choices = Execute(Category.Settle());
        SmithTarget.Settle();
        return choices;
    }

    /// <summary>Replays a settle on a client with the host's category and Smith target commits.</summary>
    /// <returns>The options done, in order.</returns>
    public IReadOnlyList<CampfireChoice> ApplySettle(IReadOnlyList<Commit<OptionKey>> categoryCommits, IReadOnlyList<Commit<CardId>> smithCommits)
    {
        ArgumentNullException.ThrowIfNull(categoryCommits);
        ArgumentNullException.ThrowIfNull(smithCommits);
        Category.ApplyAll(categoryCommits);
        var choices = Execute(categoryCommits);
        SmithTarget.ApplyAll(smithCommits);
        return choices;
    }

    /// <summary>Checks whether the player may pick this Smith target now: they have to have chosen Smith.</summary>
    public CommitCheck CanChooseTarget(PlayerId player, CardId card) =>
        _campers.ContainsKey(player) && !HasChosen(player, CampfireOptions.Smith) ? CommitCheck.Unavailable : SmithTarget.CanCommit(player, card);

    /// <summary>A Smith target click, decided like any other linked choice; see <see cref="LinkedChoice{TAtom}.Choose"/>.</summary>
    /// <returns>The Smith target commits made, in order.</returns>
    /// <exception cref="InvalidOperationException">The target isn't allowed; see <see cref="CanChooseTarget"/>.</exception>
    public IReadOnlyList<Commit<CardId>> ChooseTarget(PlayerId player, CardId card)
    {
        var check = CanChooseTarget(player, card);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't Smith {card}: {check}.");
        }

        return SmithTarget.Choose(player, card);
    }

    /// <summary>How each of the player's options should look, based on the linked category.</summary>
    public IReadOnlyList<OptionView<string>> ViewOptions(PlayerId player)
    {
        var camper = Get(player);
        var categories = camper.HasTent
            ? null
            : Category.ViewOptions(player).ToDictionary(view => view.Option);
        var views = new List<OptionView<string>>();
        foreach (var option in camper.Options)
        {
            if (camper.HasChosen(option.Id))
            {
                views.Add(new OptionView<string>(option.Id, OptionState.Chosen));
            }
            else if (!option.Available)
            {
                views.Add(new OptionView<string>(option.Id, OptionState.Unavailable));
            }
            else if (categories is null)
            {
                views.Add(new OptionView<string>(option.Id, OptionState.Open));
            }
            else
            {
                var category = categories[CampfireOptions.KeyOf(CampfireOptions.CategoryOf(option.Id))];
                var state = category.State switch
                {
                    OptionState.Required => OptionState.Open,
                    OptionState.Chosen => OptionState.NotChosen,
                    _ => category.State,
                };
                views.Add(new OptionView<string>(option.Id, state, state == OptionState.Open ? null : category.Because));
            }
        }

        return views;
    }

    /// <summary>
    /// Handles a click the same way on the host and on clients. A Miniature Tent holder's option is done at once.
    /// Anyone else's click becomes their pending option, or takes it back when it's the pending option already, and
    /// then <paramref name="decide"/> votes for the option's category, or just settles when the vote didn't change.
    /// </summary>
    /// <returns>The options done, in order.</returns>
    private List<CampfireChoice> Click(PlayerId player, string optionId, Func<OptionKey?, IReadOnlyList<Commit<OptionKey>>> decide)
    {
        var camper = Get(player);
        var option = camper.Find(optionId)?.Id ?? throw new ArgumentException($"{player} has no {optionId} option.", nameof(optionId));
        var choices = new List<CampfireChoice>();
        OptionKey? vote = null;
        if (camper.HasTent)
        {
            choices.Add(Do(player, option));
        }
        else if (camper.Pending is { } pending && string.Equals(pending, option, StringComparison.OrdinalIgnoreCase))
        {
            camper.Pending = null;
            Category.ClearVote(player);
        }
        else
        {
            camper.Pending = option;
            var key = CampfireOptions.KeyOf(CampfireOptions.CategoryOf(option));
            vote = Selection.Of(key).Equals(Category.VoteOf(player)) ? null : key;
        }

        choices.AddRange(Execute(decide(vote)));
        return choices;
    }

    /// <summary>Does the pending option of every player whose category was just committed.</summary>
    private List<CampfireChoice> Execute(IEnumerable<Commit<OptionKey>> commits)
    {
        var choices = new List<CampfireChoice>();
        foreach (var commit in commits)
        {
            var camper = Get(commit.Player);
            if (camper.Pending is not { } option)
            {
                throw new InvalidOperationException($"{commit.Player} has no option waiting for their category.");
            }

            camper.Pending = null;
            choices.Add(Do(commit.Player, option));
        }

        return choices;
    }

    private CampfireChoice Do(PlayerId player, string option)
    {
        var camper = _campers[player];
        camper.Chosen.Add(option);
        if (!camper.HasTent && !CampfireOptions.IsSmith(option) && SmithTarget.IsParticipant(player) && !SmithTarget.HasDecided(player))
        {
            SmithTarget.RemoveParticipant(player);
        }

        return new CampfireChoice(player, option);
    }

    private bool HasChosen(PlayerId player, string optionId) => _campers.TryGetValue(player, out var camper) && camper.HasChosen(optionId);

    private static DecisionOption CategoryOption(List<CampfireOption> options, CampfireCategory category) =>
        new(
            CampfireOptions.KeyOf(category),
            options.Exists(option => option.Available && CampfireOptions.CategoryOf(option.Id) == category));

    private Camper Get(PlayerId player) =>
        _campers.TryGetValue(player, out var camper)
            ? camper
            : throw new ArgumentException($"{player} isn't at the campfire.", nameof(player));

    private sealed class Camper(List<CampfireOption> options, bool hasTent)
    {
        public List<CampfireOption> Options { get; } = options;

        public bool HasTent { get; } = hasTent;

        public List<string> Chosen { get; } = [];

        /// <summary>With everyone agrees, the option clicked that waits for the category to be agreed.</summary>
        public string? Pending { get; set; }

        public CampfireOption? Find(string optionId)
        {
            foreach (var option in Options)
            {
                if (string.Equals(option.Id, optionId, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }

        public bool HasChosen(string optionId) =>
            Chosen.Exists(chosen => string.Equals(chosen, optionId, StringComparison.OrdinalIgnoreCase));
    }
}
