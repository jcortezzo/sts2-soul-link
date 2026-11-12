namespace SoulLink.Core.Decisions;

/// <summary>How a commit relates to the commits before it.</summary>
public enum CommitKind
{
    /// <summary>The player wasn't bound, so they decided for themselves. Binds everyone else who can follow and isn't bound yet.</summary>
    Lead,

    /// <summary>The player made the choice they were bound to. Binds no one.</summary>
    Follow,

    /// <summary>The player was bound but took an exclusive extra (nice mode) or a separate option instead.</summary>
    Escape,
}

/// <summary>
/// A player's final choice in a decision. The host orders commits, so <see cref="Sequence"/> is the same on every peer.
/// </summary>
/// <param name="Sequence">1-based position of this commit within its decision.</param>
/// <param name="Player">Who committed.</param>
/// <param name="Choice">What they picked, in terms of their own options.</param>
/// <param name="Link">What they picked, in terms every player can resolve against their own options.</param>
/// <param name="Kind">Whether they led, followed or escaped.</param>
/// <param name="Follows">For a follow, the sequence of the commit that bound them.</param>
/// <param name="Overrides">
/// True for an exclusive extra or a separate option: it binds the other players who have the same option, even ones
/// already bound to something else.
/// </param>
/// <param name="Mode">The mode in force when this commit was made. The first commit fixes the mode for the decision.</param>
public sealed record Commit<TAtom>(
    int Sequence,
    PlayerId Player,
    Selection<TAtom> Choice,
    LinkKey Link,
    CommitKind Kind,
    int? Follows,
    bool Overrides,
    LinkMode Mode)
    where TAtom : notnull
{
    /// <summary>The sequence of the commit that set this choice: this one for a lead or escape, the followed one for a follow.</summary>
    public int Root => Follows ?? Sequence;
}

/// <summary>The commit a player has to follow, translated into their own options.</summary>
/// <param name="Required">What the player has to pick.</param>
/// <param name="Source">The lead or escape commit that bound them.</param>
public sealed record Binding<TAtom>(Selection<TAtom> Required, Commit<TAtom> Source)
    where TAtom : notnull;
