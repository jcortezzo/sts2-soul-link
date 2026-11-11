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

/// <summary>Campfire option ids and their categories.</summary>
public static class CampfireOptions
{
    public const string Rest = "REST";
    public const string Mend = "MEND";
    public const string Smith = "SMITH";
    public const string Dig = "DIG";
    public const string Cook = "COOK";
    public const string Lift = "LIFT";

    /// <summary>Rest and Mend recover; every other option improves, including ones added by relics or quests.</summary>
    public static CampfireCategory CategoryOf(string optionId) =>
        Is(optionId, Rest) || Is(optionId, Mend) ? CampfireCategory.Recover : CampfireCategory.Improve;

    /// <summary>The option key of a category in <see cref="CampfireSession.Category"/>.</summary>
    public static OptionKey KeyOf(CampfireCategory category) => OptionKey.Named(category.ToString().ToLowerInvariant());

    public static bool IsSmith(string optionId) => Is(optionId, Smith);

    private static bool Is(string optionId, string known) => string.Equals(optionId, known, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One campfire visit by the party. The first player to choose sets the category, Recover or Improve, and everyone
/// else picks an option within it. Miniature Tent holders can choose any number of options and don't set the
/// category. If more than one player Smiths, the first Smith target decides.
/// </summary>
public sealed class CampfireSession
{
    private readonly Dictionary<PlayerId, Camper> _campers = new();

    public CampfireSession(LinkMode mode, ICardMatchingStrategy matching)
    {
        Category = new OptionDecision(mode);
        SmithTarget = new CardChoiceDecision(matching, mode);
    }

    /// <summary>Recover or Improve, linked among the players without Miniature Tent.</summary>
    public OptionDecision Category { get; }

    /// <summary>
    /// Which card each player who Smiths upgrades. Everyone who could Smith joins when they arrive, so mean mode only
    /// allows targets every possible smith can follow. Players without Miniature Tent leave it when they choose
    /// something else.
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

    /// <summary>Records the player's choice. For players without Miniature Tent, this commits their category.</summary>
    /// <exception cref="InvalidOperationException">The choice isn't allowed; see <see cref="CanChoose"/>.</exception>
    public void Choose(PlayerId player, string optionId)
    {
        var check = CanChoose(player, optionId);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't choose {optionId}: {check}.");
        }

        var camper = _campers[player];
        camper.Chosen.Add(camper.Find(optionId)!.Value.Id);
        if (camper.HasTent)
        {
            return;
        }

        Category.Commit(player, CampfireOptions.KeyOf(CampfireOptions.CategoryOf(optionId)));
        if (!CampfireOptions.IsSmith(optionId) && SmithTarget.IsParticipant(player) && !SmithTarget.HasDecided(player))
        {
            SmithTarget.RemoveParticipant(player);
        }
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
