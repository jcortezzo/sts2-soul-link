namespace SoulLink.Core;

/// <summary>What happens when a linked choice isn't possible for everyone.</summary>
public enum LinkMode
{
    /// <summary>Whoever can follow a choice must; players who can't decide for themselves.</summary>
    Nice,

    /// <summary>Everyone or no one: an option is greyed out unless every player in the decision can take it.</summary>
    Mean,
}

/// <summary>Which of your cards counts as "the same card" for upgrades, removals and other deck choices.</summary>
public enum CardMatching
{
    /// <summary>The card linked to theirs: gained from the same linked choice, or paired at the start of the run.</summary>
    LinkedCards,

    /// <summary>The card at the same position on your selection screen; your last card if your screen is shorter.</summary>
    DeckIndex,
}

/// <summary>How a linked decision gets made.</summary>
public enum WhoDecides
{
    /// <summary>Every player who can take an option has to click it before it's taken.</summary>
    EveryoneAgrees,

    /// <summary>The first player to pick decides, and the same pick is made for everyone who can follow.</summary>
    FirstClick,
}

/// <summary>The rules the host picks before the run. Everyone plays under the same settings.</summary>
/// <param name="CardMatching">How deck card choices are matched between players.</param>
/// <param name="Mode">Nice or mean handling of choices that aren't possible for everyone.</param>
/// <param name="SharedDeath">Whether any player's death ends the run for everyone.</param>
/// <param name="WhoDecides">Whether everyone has to agree, or the first click decides.</param>
public sealed record SoulLinkSettings(
    CardMatching CardMatching = CardMatching.LinkedCards,
    LinkMode Mode = LinkMode.Nice,
    bool SharedDeath = false,
    WhoDecides WhoDecides = WhoDecides.EveryoneAgrees)
{
    public static SoulLinkSettings Default { get; } = new();
}
