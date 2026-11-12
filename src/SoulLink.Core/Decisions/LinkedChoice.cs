using System.Diagnostics.CodeAnalysis;

namespace SoulLink.Core.Decisions;

/// <summary>
/// One linked decision: the first player to commit sets the choice for everyone who can make the same choice.
/// </summary>
/// <remarks>
/// <para>
/// Bindings aren't stored. They're worked out from the ordered commits each time, so a player who joins late or
/// whose options change is bound by the same rules as everyone else:
/// </para>
/// <list type="bullet">
/// <item>A lead (a commit by an unbound player) binds every other undecided player who can follow it and isn't bound yet.</item>
/// <item>An exclusive extra or a separate option binds the other players who have it, even ones already bound to something else.</item>
/// <item>A follow binds no one, so players who can't follow the first choice stay free.</item>
/// </list>
/// <para>
/// Mean mode only changes which choices are allowed: an unbound player may only pick choices that every other
/// unbound, undecided player could follow, so the first commit binds everyone. If any player has no such choice, the
/// decision falls back to nice mode. The first commit fixes the mode for the rest of the decision.
/// </para>
/// <para>
/// A separate option, like Pael's Wing's sacrifice, is linked only among the players who have it: in both modes it can
/// be taken instead of following and is never greyed out because others lack it.
/// </para>
/// </remarks>
/// <typeparam name="TAtom">One pickable option, like an <see cref="OptionKey"/> or a card.</typeparam>
public abstract class LinkedChoice<TAtom>
    where TAtom : notnull
{
    private readonly List<PlayerId> _participants = [];
    private readonly List<Commit<TAtom>> _commits = [];
    private readonly Dictionary<PlayerId, Commit<TAtom>> _decided = new();
    private LinkMode? _lockedMode;

    protected LinkedChoice(LinkMode mode, int pickCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pickCount, 1);
        Mode = mode;
        PickCount = pickCount;
    }

    /// <summary>The mode from the settings.</summary>
    public LinkMode Mode { get; }

    /// <summary>How many options each player picks: 1 for most decisions, more for "choose N" screens.</summary>
    public int PickCount { get; }

    /// <summary>The players facing this decision, in the order they joined.</summary>
    public IReadOnlyList<PlayerId> Participants => _participants;

    /// <summary>All commits in the order the host accepted them.</summary>
    public IReadOnlyList<Commit<TAtom>> Commits => _commits;

    /// <summary>True when every participant has committed.</summary>
    public bool IsComplete => _participants.TrueForAll(_decided.ContainsKey);

    /// <summary>The mode actually in force. Mean falls back to nice for this decision when no choice works for everyone.</summary>
    public LinkMode EffectiveMode
    {
        get
        {
            if (_lockedMode is { } locked)
            {
                return locked;
            }

            if (Mode == LinkMode.Nice)
            {
                return LinkMode.Nice;
            }

            foreach (var player in _participants)
            {
                if (!_decided.ContainsKey(player) && !HasMeanChoice(player))
                {
                    return LinkMode.Nice;
                }
            }

            return LinkMode.Mean;
        }
    }

    public bool IsParticipant(PlayerId player) => _participants.Contains(player);

    public bool HasDecided(PlayerId player) => _decided.ContainsKey(player);

    /// <summary>The player's commit, or null if they haven't committed.</summary>
    public Commit<TAtom>? CommitOf(PlayerId player) => _decided.GetValueOrDefault(player);

    /// <summary>Whether the player is free, bound or decided.</summary>
    public ChoiceStatus<TAtom> StatusOf(PlayerId player)
    {
        RequireParticipant(player);
        if (_decided.TryGetValue(player, out var commit))
        {
            return ChoiceStatus<TAtom>.DecidedAs(commit);
        }

        return FindBinding(player) is { } binding ? ChoiceStatus<TAtom>.BoundBy(binding) : ChoiceStatus<TAtom>.Free;
    }

    /// <summary>The lead commits this player can't follow, which is why they're free. Empty if nobody has chosen yet.</summary>
    public IReadOnlyList<Commit<TAtom>> UnfollowableCommits(PlayerId player)
    {
        RequireParticipant(player);
        return _commits
            .Where(commit => commit.Kind != CommitKind.Follow && commit.Player != player)
            .Where(commit => !TryFollow(commit.Link, commit.Choice.Count, player, out _))
            .ToList();
    }

    /// <summary>Checks whether the player may commit this selection now.</summary>
    public CommitCheck CanCommit(PlayerId player, Selection<TAtom> choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (!IsParticipant(player))
        {
            return CommitCheck.NotParticipant;
        }

        if (_decided.ContainsKey(player))
        {
            return CommitCheck.AlreadyDecided;
        }

        if (choice.Count != PickCount)
        {
            return CommitCheck.WrongPickCount;
        }

        if (!IsAvailable(player, choice))
        {
            return CommitCheck.Unavailable;
        }

        var mode = EffectiveMode;
        if (FindBinding(player) is { } binding)
        {
            if (binding.Required.Equals(choice))
            {
                return CommitCheck.Allowed;
            }

            return CanTakeInstead(choice, mode) ? CommitCheck.Allowed : CommitCheck.MustFollow(binding.Source.Player);
        }

        if (mode == LinkMode.Mean && !IsSeparate(choice) && HasMeanChoice(player) && FirstWhoCannotFollow(player, choice) is { } blocker)
        {
            return CommitCheck.GreyedOut(blocker);
        }

        return CommitCheck.Allowed;
    }

    /// <summary>Records the player's choice after checking it's allowed. Used by the host and in single-peer play.</summary>
    /// <exception cref="InvalidOperationException">The choice isn't allowed; see <see cref="CanCommit"/>.</exception>
    public Commit<TAtom> Commit(PlayerId player, Selection<TAtom> choice)
    {
        var check = CanCommit(player, choice);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't choose {choice}: {check}.");
        }

        var mode = EffectiveMode;
        var binding = FindBinding(player);
        var kind = binding is null
            ? CommitKind.Lead
            : binding.Required.Equals(choice) ? CommitKind.Follow : CommitKind.Escape;
        var commit = new Commit<TAtom>(
            Sequence: _commits.Count + 1,
            Player: player,
            Choice: choice,
            Link: Describe(player, choice),
            Kind: kind,
            Follows: kind == CommitKind.Follow ? binding!.Source.Sequence : null,
            Overrides: kind != CommitKind.Follow && (IsExclusive(choice) || IsSeparate(choice)),
            Mode: mode);
        Record(commit);
        return commit;
    }

    /// <summary>Records a commit the host already accepted, without checking it again. Used by clients.</summary>
    /// <exception cref="InvalidOperationException">The commit is out of order or the player already committed.</exception>
    public void Apply(Commit<TAtom> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        RequireParticipant(commit.Player);
        if (_decided.ContainsKey(commit.Player))
        {
            throw new InvalidOperationException($"{commit.Player} already committed.");
        }

        if (commit.Sequence != _commits.Count + 1)
        {
            throw new InvalidOperationException($"Expected commit {_commits.Count + 1} but got {commit.Sequence}.");
        }

        Record(commit);
    }

    /// <summary>How each of the player's options should look: open, required, locked, greyed out and so on.</summary>
    public IReadOnlyList<OptionView<TAtom>> ViewOptions(PlayerId player)
    {
        RequireParticipant(player);
        var views = new List<OptionView<TAtom>>();
        if (_decided.TryGetValue(player, out var commit))
        {
            foreach (var option in Options(player))
            {
                views.Add(new OptionView<TAtom>(option, commit.Choice.Contains(option) ? OptionState.Chosen : OptionState.NotChosen));
            }

            return views;
        }

        var mode = EffectiveMode;
        var binding = FindBinding(player);
        var meanApplies = binding is null && mode == LinkMode.Mean && HasMeanChoice(player);
        foreach (var option in Options(player))
        {
            views.Add(ViewOption(player, option, mode, binding, meanApplies));
        }

        return views;
    }

    /// <summary>Removes a player who left, for example by disconnecting. Their commits still bind others.</summary>
    public void RemoveParticipant(PlayerId player)
    {
        _participants.Remove(player);
        _decided.Remove(player);
    }

    /// <summary>Every option this player sees, available or not, in display order.</summary>
    protected abstract IEnumerable<TAtom> Options(PlayerId player);

    /// <summary>Whether the player has this option and the game would let them take it now.</summary>
    protected abstract bool IsAvailable(PlayerId player, TAtom option);

    /// <summary>Describes a player's choice in terms every player can resolve.</summary>
    protected abstract LinkKey Describe(PlayerId player, Selection<TAtom> choice);

    /// <summary>Finds the player's options matching a described choice. Availability is checked separately.</summary>
    protected abstract bool TryResolve(LinkKey link, PlayerId player, [MaybeNullWhen(false)] out Selection<TAtom> choice);

    /// <summary>Whether the selection includes an extra option that not every participant has.</summary>
    protected virtual bool IsExclusive(Selection<TAtom> choice) => false;

    /// <summary>Whether the selection includes a separate option, linked only among the players who have it.</summary>
    protected virtual bool IsSeparate(Selection<TAtom> choice) => false;

    protected void AddParticipant(PlayerId player)
    {
        if (_participants.Contains(player))
        {
            throw new ArgumentException($"{player} already joined this decision.", nameof(player));
        }

        _participants.Add(player);
    }

    protected void RequireParticipant(PlayerId player)
    {
        if (!_participants.Contains(player))
        {
            throw new ArgumentException($"{player} isn't facing this decision.", nameof(player));
        }
    }

    /// <summary>
    /// Whether a bound player may take this instead of following: a separate option always, an exclusive extra in
    /// nice mode.
    /// </summary>
    private bool CanTakeInstead(Selection<TAtom> choice, LinkMode mode) =>
        IsSeparate(choice) || (mode == LinkMode.Nice && IsExclusive(choice));

    private void Record(Commit<TAtom> commit)
    {
        _lockedMode ??= commit.Mode;
        _commits.Add(commit);
        _decided[commit.Player] = commit;
    }

    private Binding<TAtom>? FindBinding(PlayerId player)
    {
        Binding<TAtom>? binding = null;
        foreach (var commit in _commits)
        {
            if (commit.Kind == CommitKind.Follow || commit.Player == player)
            {
                continue;
            }

            if (binding is not null && !commit.Overrides)
            {
                continue;
            }

            if (TryFollow(commit.Link, commit.Choice.Count, player, out var required))
            {
                binding = new Binding<TAtom>(required, commit);
            }
        }

        return binding;
    }

    private bool TryFollow(LinkKey link, int count, PlayerId player, [MaybeNullWhen(false)] out Selection<TAtom> required)
    {
        if (TryResolve(link, player, out var resolved) && resolved.Count == count && IsAvailable(player, resolved))
        {
            required = resolved;
            return true;
        }

        required = null;
        return false;
    }

    private bool IsAvailable(PlayerId player, Selection<TAtom> choice)
    {
        foreach (var option in choice.Items)
        {
            if (!IsAvailable(player, option))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The first player a lead with this choice would need to bind but who couldn't follow it: an undecided player
    /// who isn't already bound. Before the first commit, that's everyone else.
    /// </summary>
    private PlayerId? FirstWhoCannotFollow(PlayerId player, Selection<TAtom> choice)
    {
        var link = Describe(player, choice);
        foreach (var other in _participants)
        {
            if (other == player || _decided.ContainsKey(other) || FindBinding(other) is not null)
            {
                continue;
            }

            if (!TryFollow(link, choice.Count, other, out _))
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>Whether the player has enough options that every other unbound, undecided player could follow.</summary>
    private bool HasMeanChoice(PlayerId player)
    {
        var count = 0;
        foreach (var option in Options(player))
        {
            if (IsAvailable(player, option) && FirstWhoCannotFollow(player, Selection.Of(option)) is null && ++count >= PickCount)
            {
                return true;
            }
        }

        return false;
    }

    private OptionView<TAtom> ViewOption(PlayerId player, TAtom option, LinkMode mode, Binding<TAtom>? binding, bool meanApplies)
    {
        if (!IsAvailable(player, option))
        {
            return new OptionView<TAtom>(option, OptionState.Unavailable);
        }

        if (binding is not null)
        {
            if (binding.Required.Contains(option))
            {
                return new OptionView<TAtom>(option, OptionState.Required, binding.Source.Player);
            }

            return CanTakeInstead(Selection.Of(option), mode)
                ? new OptionView<TAtom>(option, OptionState.Escape, binding.Source.Player)
                : new OptionView<TAtom>(option, OptionState.Locked, binding.Source.Player);
        }

        if (meanApplies && !IsSeparate(Selection.Of(option)) && FirstWhoCannotFollow(player, Selection.Of(option)) is { } blocker)
        {
            return new OptionView<TAtom>(option, OptionState.GreyedOut, blocker);
        }

        return new OptionView<TAtom>(option, OptionState.Open);
    }
}
