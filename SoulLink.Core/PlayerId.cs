namespace SoulLink.Core;

/// <summary>Identifies a player in a co-op run. Wraps the game's network id for that player.</summary>
public readonly record struct PlayerId(ulong Value)
{
    public override string ToString() => $"Player {Value}";
}
