using System.Globalization;

namespace SoulLink.Core.Decisions;

/// <summary>
/// Identifies an option across players. Options with the same key are "the same choice" even when they hold
/// different things, like slot 2 of two different card rewards.
/// </summary>
/// <param name="Value">The key as text, for example <c>slot:2</c>, <c>skip</c> or <c>extra:LASTING_CANDY</c>.</param>
public readonly record struct OptionKey(string Value)
{
    private const string SlotPrefix = "slot:";
    private const string ExtraPrefix = "extra:";

    /// <summary>Not taking anything: skipping a card reward, a relic reward and so on.</summary>
    public static OptionKey Skip { get; } = new("skip");

    /// <summary>Taking a single offered thing, like a relic reward.</summary>
    public static OptionKey Take { get; } = new("take");

    /// <summary>
    /// True for an extra option that only some players get from a relic or effect, like Lasting Candy's bonus card.
    /// When not everyone in a decision has it, it's exclusive: in nice mode it can be taken instead of following,
    /// and in mean mode it's greyed out.
    /// </summary>
    public bool IsExtra => Value.StartsWith(ExtraPrefix, StringComparison.Ordinal);

    /// <summary>The slot number for slot keys, otherwise null.</summary>
    public int? SlotNumber =>
        Value.StartsWith(SlotPrefix, StringComparison.Ordinal)
        && int.TryParse(Value.AsSpan(SlotPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>The option in a numbered slot, counted from 1, left to right as each player sees their own options.</summary>
    public static OptionKey Slot(int number)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return new OptionKey(SlotPrefix + number.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>An extra option from a relic or effect. Holders of the same source share the key.</summary>
    public static OptionKey Extra(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new OptionKey(ExtraPrefix + source);
    }

    /// <summary>A named option, like a campfire category.</summary>
    public static OptionKey Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.StartsWith(ExtraPrefix, StringComparison.Ordinal) || name.StartsWith(SlotPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Use {nameof(Slot)} or {nameof(Extra)} for '{name}'.", nameof(name));
        }

        return new OptionKey(name);
    }

    public override string ToString() => Value;
}

/// <summary>One option as a particular player sees it.</summary>
/// <param name="Key">The key that matches it with other players' options.</param>
/// <param name="Available">Whether the game would let this player take it right now.</param>
public readonly record struct DecisionOption(OptionKey Key, bool Available = true);
