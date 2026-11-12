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
/// <para>
/// Players act through <see cref="Choose"/>, and <see cref="WhoDecides"/> sets what a click does. With first click, it
/// commits the choice and makes the same choice for every bound player who has nothing they could take instead. With
/// everyone agrees, it's a vote, and a choice is committed once everyone who could take it has voted for it. Both
/// build on the same commits and bindings.
/// </para>
/// <para>
/// On "choose N" screens players also share what they've selected before confirming, through <see cref="Mark"/>, so
/// they can line up. Marks only show; changing them takes back a vote that no longer matches.
/// </para>
/// </remarks>
/// <typeparam name="TAtom">One pickable option, like an <see cref="OptionKey"/> or a card.</typeparam>
public abstract class LinkedChoice<TAtom>
    where TAtom : notnull
{
    private readonly List<PlayerId> _participants = [];
    private readonly List<Commit<TAtom>> _commits = [];
    private readonly Dictionary<PlayerId, Commit<TAtom>> _decided = new();
    private readonly Dictionary<PlayerId, Vote> _votes = new();
    private readonly Dictionary<PlayerId, Marking> _marks = new();
    private LinkMode? _lockedMode;
    private int _voteSequence;
    private int _markSequence;

    protected LinkedChoice(LinkMode mode, int pickCount, WhoDecides whoDecides)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pickCount, 1);
        Mode = mode;
        PickCount = pickCount;
        WhoDecides = whoDecides;
    }

    /// <summary>The mode from the settings.</summary>
    public LinkMode Mode { get; }

    /// <summary>Whether a click is a vote that everyone has to agree on, or a commit that decides at once.</summary>
    public WhoDecides WhoDecides { get; }

    /// <summary>
    /// With first click, whether the same choice is made for bound players automatically. Off for decisions whose
    /// choice isn't a single action, like a campfire category, where everyone still picks an option within it.
    /// </summary>
    public bool FollowsAutomatically { get; init; } = true;

    /// <summary>
    /// Players who can't act yet but will, like a Driftwood holder who owes a reroll. Nothing is chosen for them
    /// automatically, and an agreement they could join waits for them.
    /// </summary>
    public Func<PlayerId, bool>? IsHeld { get; set; }

    /// <summary>
    /// Players who may not take part, like a player who could Smith but hasn't chosen Smith. They still count for
    /// mean mode, but nothing is chosen for them automatically and agreements don't wait for them.
    /// </summary>
    public Func<PlayerId, bool>? IsProspect { get; set; }

    /// <summary>Each undecided player's current vote, with everyone agrees.</summary>
    public IReadOnlyDictionary<PlayerId, Selection<TAtom>> Votes =>
        _votes.ToDictionary(entry => entry.Key, entry => entry.Value.Choice);

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

    /// <summary>The player's current vote, or null.</summary>
    public Selection<TAtom>? VoteOf(PlayerId player) => _votes.TryGetValue(player, out var vote) ? vote.Choice : null;

    /// <summary>
    /// The other players whose votes, matched to this player's options, include this option, in vote order. Shows who
    /// is waiting on whom.
    /// </summary>
    public IReadOnlyList<PlayerId> VotersFor(PlayerId player, TAtom option)
    {
        RequireParticipant(player);
        return _votes
            .Where(entry => entry.Key != player)
            .OrderBy(entry => entry.Value.Sequence)
            .Where(entry => TryResolve(Describe(entry.Key, entry.Value.Choice), player, out var mine) && mine.Contains(option))
            .Select(entry => entry.Key)
            .ToList();
    }

    /// <summary>Takes back a player's vote, for example when their options change.</summary>
    public void ClearVote(PlayerId player) => _votes.Remove(player);

    /// <summary>What the undecided player has selected so far on a "choose N" screen, or empty.</summary>
    public IReadOnlyList<TAtom> MarksOf(PlayerId player) => _marks.TryGetValue(player, out var marking) ? marking.Items : [];

    /// <summary>
    /// Records what the player has selected so far on a "choose N" screen, before they confirm, so the others can see
    /// it and line up with it. Replaces their earlier selection. A vote that doesn't match the new selection is taken
    /// back, so a player who changes their selection after confirming has to confirm again before it can be decided.
    /// Marks never commit anything. Players who aren't on the decision or have decided are ignored.
    /// </summary>
    public void Mark(PlayerId player, IEnumerable<TAtom> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (!IsParticipant(player) || _decided.ContainsKey(player))
        {
            return;
        }

        var items = selected.Distinct().ToList();
        if (_votes.TryGetValue(player, out var vote) && !vote.Choice.Equals(new Selection<TAtom>(items)))
        {
            _votes.Remove(player);
        }

        if (items.Count == 0)
        {
            _marks.Remove(player);
            return;
        }

        // A player keeps their place in the order while they change their selection, so portraits don't jump around.
        var sequence = _marks.TryGetValue(player, out var earlier) ? earlier.Sequence : ++_markSequence;
        _marks[player] = new Marking(items, sequence);
    }

    /// <summary>
    /// The other undecided players whose selection so far, matched to this player's options the way a pick would be,
    /// includes this option, in the order they started selecting. Shows who is choosing what before anyone confirms.
    /// </summary>
    public IReadOnlyList<PlayerId> MarkersFor(PlayerId player, TAtom option)
    {
        RequireParticipant(player);
        var markers = new List<PlayerId>();
        foreach (var (other, marking) in _marks.OrderBy(entry => entry.Value.Sequence))
        {
            if (other == player || !IsParticipant(other))
            {
                continue;
            }

            // A card that left the player's screen since they selected it can't be matched any more.
            var options = Options(other).ToHashSet();
            var shown = marking.Items.Where(options.Contains).ToList();
            if (shown.Count > 0 && TryResolve(Describe(other, new Selection<TAtom>(shown)), player, out var mine) && mine.Contains(option))
            {
                markers.Add(other);
            }
        }

        return markers;
    }

    /// <summary>
    /// A player's click. With first click, it commits the choice and then makes the same choice for everyone who has to
    /// follow. With everyone agrees, it becomes the player's vote, replacing any earlier one, or takes the vote back if
    /// it's the same choice again; the choice is committed once everyone who can take it has voted for it.
    /// </summary>
    /// <returns>The commits made, in order. Empty while a vote waits for the others.</returns>
    /// <exception cref="InvalidOperationException">The choice isn't allowed; see <see cref="CanCommit"/>.</exception>
    public IReadOnlyList<Commit<TAtom>> Choose(PlayerId player, Selection<TAtom> choice)
    {
        var check = CanCommit(player, choice);
        if (!check.IsAllowed)
        {
            throw new InvalidOperationException($"{player} can't choose {choice}: {check}.");
        }

        if (WhoDecides == WhoDecides.FirstClick)
        {
            var commits = new List<Commit<TAtom>> { Commit(player, choice) };
            commits.AddRange(Settle());
            return commits;
        }

        RecordVote(player, choice);
        return Settle();
    }

    /// <summary>
    /// Re-checks the decision after players join, options change or a held player can act: first click makes the same
    /// choice for newly bound players, and everyone agrees commits any agreement that's now complete.
    /// </summary>
    /// <returns>The commits made, in order.</returns>
    public IReadOnlyList<Commit<TAtom>> Settle()
    {
        var commits = new List<Commit<TAtom>>();
        while (true)
        {
            var made = WhoDecides == WhoDecides.FirstClick ? FollowOnce() : AgreeOnce();
            if (made.Count == 0)
            {
                return commits;
            }

            commits.AddRange(made);
        }
    }

    /// <summary>Replays a host click on a client: records the vote with everyone agrees, then applies the host's commits.</summary>
    public void ApplyChoice(PlayerId player, Selection<TAtom> choice, IReadOnlyList<Commit<TAtom>> commits)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(commits);
        if (WhoDecides == WhoDecides.EveryoneAgrees && IsParticipant(player) && !_decided.ContainsKey(player))
        {
            RecordVote(player, choice);
        }

        ApplyAll(commits);
    }

    /// <summary>
    /// Applies commits the host made, in order, then drops votes that stopped being possible, as the host's settle
    /// did. Used by clients after joins, option changes and clicks.
    /// </summary>
    public void ApplyAll(IReadOnlyList<Commit<TAtom>> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);
        foreach (var commit in commits)
        {
            Apply(commit);
        }

        DropImpossibleVotes();
    }

    /// <summary>
    /// With first click, the choice made automatically for the player: what they're bound to, unless they could take an
    /// exclusive extra or a separate option instead, in which case they choose. Null for unbound or held players.
    /// </summary>
    public Selection<TAtom>? AutoFollowChoice(PlayerId player)
    {
        if (!FollowsAutomatically || !IsParticipant(player) || _decided.ContainsKey(player) || Waits(player)
            || FindBinding(player) is not { } binding)
        {
            return null;
        }

        var mode = EffectiveMode;
        foreach (var option in Options(player))
        {
            if (!binding.Required.Contains(option) && IsAvailable(player, option) && CanTakeInstead(Selection.Of(option), mode))
            {
                return null;
            }
        }

        return binding.Required;
    }

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
        _votes.Remove(player);
        _marks.Remove(player);
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
        _votes.Remove(commit.Player);
        _marks.Remove(commit.Player);
    }

    private void RecordVote(PlayerId player, Selection<TAtom> choice)
    {
        if (_votes.TryGetValue(player, out var current) && current.Choice.Equals(choice))
        {
            _votes.Remove(player);
            return;
        }

        _votes[player] = new Vote(choice, ++_voteSequence);
    }

    /// <summary>First click: commits the required choice for every bound player who has to follow automatically.</summary>
    private List<Commit<TAtom>> FollowOnce()
    {
        var commits = new List<Commit<TAtom>>();
        foreach (var player in _participants.ToList())
        {
            if (AutoFollowChoice(player) is { } choice && CanCommit(player, choice).IsAllowed)
            {
                commits.Add(Commit(player, choice));
            }
        }

        return commits;
    }

    /// <summary>Whether nothing may be chosen for the player automatically: they're held or a prospect.</summary>
    private bool Waits(PlayerId player) => IsHeld?.Invoke(player) == true || IsProspect?.Invoke(player) == true;

    /// <summary>Drops votes that stopped being possible, for example because the player's options changed.</summary>
    private void DropImpossibleVotes()
    {
        foreach (var (voter, vote) in _votes.ToList())
        {
            if (!Waits(voter) && !CanCommit(voter, vote.Choice).IsAllowed)
            {
                _votes.Remove(voter);
            }
        }
    }

    /// <summary>Everyone agrees: commits the first complete agreement, in vote order, so the first voter leads it.</summary>
    private List<Commit<TAtom>> AgreeOnce()
    {
        DropImpossibleVotes();
        foreach (var (voter, vote) in _votes.OrderBy(entry => entry.Value.Sequence).ToList())
        {
            if (Waits(voter) || AgreeingGroup(voter, vote.Choice) is not { } group)
            {
                continue;
            }

            var commits = new List<Commit<TAtom>>();
            foreach (var (member, choice) in group)
            {
                if (CanCommit(member, choice).IsAllowed)
                {
                    commits.Add(Commit(member, choice));
                }
            }

            return commits;
        }

        return [];
    }

    /// <summary>
    /// Everyone who has to agree on a choice, with what each of them has to pick, in vote order; or null while anyone
    /// who could take it hasn't voted for it, including held players. Players who can't take it, like players bound
    /// to something else, prospects, and players voting for an exclusive extra or a separate option instead aren't
    /// part of it.
    /// </summary>
    private List<(PlayerId Player, Selection<TAtom> Choice)>? AgreeingGroup(PlayerId voter, Selection<TAtom> choice)
    {
        var link = Describe(voter, choice);
        var takesInstead = IsExclusive(choice) || IsSeparate(choice);
        var group = new List<(PlayerId Player, Selection<TAtom> Choice, int Sequence)>();
        foreach (var player in _participants)
        {
            if (_decided.ContainsKey(player) || IsProspect?.Invoke(player) == true)
            {
                continue;
            }

            // The voter always counts for their own pick, even one nobody can match, like an unlinked card with linked
            // cards matching: it can't be resolved back to them, but leaving them out would leave a group with no one
            // in it, and their vote could never be made.
            Selection<TAtom> required;
            if (player == voter)
            {
                required = choice;
            }
            else if (TryFollow(link, choice.Count, player, out var match) && CanCommit(player, match).IsAllowed)
            {
                required = match;
            }
            else
            {
                continue;
            }

            if (IsHeld?.Invoke(player) == true || !_votes.TryGetValue(player, out var vote))
            {
                return null;
            }

            if (vote.Choice.Equals(required))
            {
                group.Add((player, required, vote.Sequence));
            }
            else if (takesInstead || !(IsExclusive(vote.Choice) || IsSeparate(vote.Choice)))
            {
                return null;
            }
        }

        return group.OrderBy(member => member.Sequence).Select(member => (member.Player, member.Choice)).ToList();
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

    private sealed record Vote(Selection<TAtom> Choice, int Sequence);

    private sealed record Marking(IReadOnlyList<TAtom> Items, int Sequence);
}
