namespace SoulLink.Core.Decisions;

/// <summary>Why a player can or can't commit a choice.</summary>
public enum CommitVerdict
{
    Allowed,

    /// <summary>The player isn't facing this decision.</summary>
    NotParticipant,

    /// <summary>The player already committed.</summary>
    AlreadyDecided,

    /// <summary>The selection doesn't have as many items as the decision asks for.</summary>
    WrongPickCount,

    /// <summary>The player doesn't have that option, or the game wouldn't let them take it now.</summary>
    Unavailable,

    /// <summary>Someone else already chose, and the player has to follow them.</summary>
    MustFollow,

    /// <summary>Mean mode: another player in the decision couldn't take the same choice.</summary>
    GreyedOut,

    /// <summary>A Driftwood holder rerolled, so this player has to reroll before picking.</summary>
    MustReroll,
}

/// <summary>The answer to "can this player commit this choice?", with the player responsible when it's blocked.</summary>
/// <param name="Verdict">Allowed, or why not.</param>
/// <param name="Because">For blocks caused by another player: who chose first, who can't follow, or who rerolled.</param>
public readonly record struct CommitCheck(CommitVerdict Verdict, PlayerId? Because = null)
{
    public static CommitCheck Allowed { get; } = new(CommitVerdict.Allowed);

    public static CommitCheck NotParticipant { get; } = new(CommitVerdict.NotParticipant);

    public static CommitCheck AlreadyDecided { get; } = new(CommitVerdict.AlreadyDecided);

    public static CommitCheck WrongPickCount { get; } = new(CommitVerdict.WrongPickCount);

    public static CommitCheck Unavailable { get; } = new(CommitVerdict.Unavailable);

    public bool IsAllowed => Verdict == CommitVerdict.Allowed;

    public static CommitCheck MustFollow(PlayerId leader) => new(CommitVerdict.MustFollow, leader);

    public static CommitCheck GreyedOut(PlayerId blocker) => new(CommitVerdict.GreyedOut, blocker);

    public static CommitCheck MustReroll(PlayerId reroller) => new(CommitVerdict.MustReroll, reroller);

    public override string ToString() => Because is { } player ? $"{Verdict} ({player})" : Verdict.ToString();
}
