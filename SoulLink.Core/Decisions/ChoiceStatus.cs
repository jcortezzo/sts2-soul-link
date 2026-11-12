namespace SoulLink.Core.Decisions;

/// <summary>Where a player stands in a decision.</summary>
public enum ChoiceState
{
    /// <summary>Not bound: either nobody has chosen yet, or the player can't follow what was chosen.</summary>
    Free,

    /// <summary>Someone chose first and the player has to follow.</summary>
    Bound,

    /// <summary>The player has committed.</summary>
    Decided,
}

/// <summary>A player's state in a decision, with the binding or commit behind it.</summary>
public sealed record ChoiceStatus<TAtom>(ChoiceState State, Binding<TAtom>? Binding, Commit<TAtom>? Commit)
    where TAtom : notnull
{
    public static ChoiceStatus<TAtom> Free { get; } = new(ChoiceState.Free, null, null);

    public static ChoiceStatus<TAtom> BoundBy(Binding<TAtom> binding) => new(ChoiceState.Bound, binding, null);

    public static ChoiceStatus<TAtom> DecidedAs(Commit<TAtom> commit) => new(ChoiceState.Decided, null, commit);
}

/// <summary>How one option should look to a player, for lock and greyed-out indicators.</summary>
public enum OptionState
{
    /// <summary>The player may take it.</summary>
    Open,

    /// <summary>The player has to take it: someone chose this first.</summary>
    Required,

    /// <summary>An option the player may take instead of following: an exclusive extra in nice mode, or a separate option.</summary>
    Escape,

    /// <summary>Blocked because the player is bound to another option.</summary>
    Locked,

    /// <summary>Mean mode: blocked because another player couldn't take the same choice.</summary>
    GreyedOut,

    /// <summary>The game wouldn't let the player take it.</summary>
    Unavailable,

    /// <summary>The player committed to it.</summary>
    Chosen,

    /// <summary>The player committed to something else.</summary>
    NotChosen,
}

/// <summary>One option's state for a player, and the player responsible when it's blocked or required.</summary>
public readonly record struct OptionView<TAtom>(TAtom Option, OptionState State, PlayerId? Because = null)
    where TAtom : notnull;
