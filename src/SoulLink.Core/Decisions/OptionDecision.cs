using System.Diagnostics.CodeAnalysis;

namespace SoulLink.Core.Decisions;

/// <summary>
/// A linked decision where players match by option key: the same card reward slot, event option, Ancient choice,
/// chest relic or campfire category.
/// </summary>
public sealed class OptionDecision : LinkedChoice<OptionKey>
{
    private readonly Dictionary<PlayerId, IReadOnlyList<DecisionOption>> _options = new();

    /// <param name="mode">Nice or mean, from the settings.</param>
    /// <param name="pickCount">How many options each player picks, like 2 for Room Full of Cheese.</param>
    public OptionDecision(LinkMode mode, int pickCount = 1)
        : base(mode, pickCount)
    {
    }

    /// <summary>Adds a player facing this decision, with their options in display order.</summary>
    public void Join(PlayerId player, IEnumerable<DecisionOption> options)
    {
        var list = Validate(options);
        AddParticipant(player);
        _options[player] = list;
    }

    /// <summary>Replaces a player's options, for example after a reroll or when availability changes.</summary>
    public void UpdateOptions(PlayerId player, IEnumerable<DecisionOption> options)
    {
        RequireParticipant(player);
        _options[player] = Validate(options);
    }

    /// <summary>The player's options in display order.</summary>
    public IReadOnlyList<DecisionOption> OptionsOf(PlayerId player)
    {
        RequireParticipant(player);
        return _options[player];
    }

    /// <summary>Every participant's option with this key, or null for players who don't have it. Powers slot peek.</summary>
    public IReadOnlyList<KeyValuePair<PlayerId, DecisionOption?>> Peek(OptionKey key) =>
        Participants
            .Select(player => new KeyValuePair<PlayerId, DecisionOption?>(player, Find(player, key)))
            .ToList();

    public CommitCheck CanCommit(PlayerId player, OptionKey option) => CanCommit(player, Selection.Of(option));

    public Commit<OptionKey> Commit(PlayerId player, OptionKey option) => Commit(player, Selection.Of(option));

    protected override IEnumerable<OptionKey> Options(PlayerId player) => _options[player].Select(option => option.Key);

    protected override bool IsAvailable(PlayerId player, OptionKey option) => Find(player, option) is { Available: true };

    protected override LinkKey Describe(PlayerId player, Selection<OptionKey> choice) =>
        new(choice.Items.Select(key => key.Value).OrderBy(value => value, StringComparer.Ordinal));

    protected override bool TryResolve(LinkKey link, PlayerId player, [MaybeNullWhen(false)] out Selection<OptionKey> choice)
    {
        choice = new Selection<OptionKey>(link.Parts.Select(part => new OptionKey(part)));
        return true;
    }

    /// <summary>An extra option is exclusive when not every participant can take it.</summary>
    protected override bool IsExclusive(Selection<OptionKey> choice) =>
        choice.Items.Any(key => key.IsExtra && !Participants.All(player => IsAvailable(player, key)));

    protected override bool IsSeparate(Selection<OptionKey> choice) => choice.Items.Any(key => key.IsSeparate);

    private static List<DecisionOption> Validate(IEnumerable<DecisionOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var list = options.ToList();
        if (list.Select(option => option.Key).Distinct().Count() != list.Count)
        {
            throw new ArgumentException("Each option key can appear only once per player.", nameof(options));
        }

        return list;
    }

    private DecisionOption? Find(PlayerId player, OptionKey key)
    {
        if (_options.TryGetValue(player, out var options))
        {
            foreach (var option in options)
            {
                if (option.Key == key)
                {
                    return option;
                }
            }
        }

        return null;
    }
}
