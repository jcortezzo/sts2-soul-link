namespace SoulLink.Core.Cards;

/// <summary>Identifies one card instance for the whole run. The game layer assigns it and keeps it stable.</summary>
public readonly record struct CardId(long Value)
{
    public override string ToString() => $"Card {Value}";
}

/// <summary>A card in a particular player's deck.</summary>
public readonly record struct CardRef(PlayerId Owner, CardId Card)
{
    public override string ToString() => $"{Card} of {Owner}";
}

/// <summary>Identifies a group of linked cards, at most one per player.</summary>
public readonly record struct LinkGroupId(int Value)
{
    public override string ToString() => $"Link {Value}";
}
