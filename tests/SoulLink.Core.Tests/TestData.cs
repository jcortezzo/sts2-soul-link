using SoulLink.Core.Decisions;

namespace SoulLink.Core.Tests;

/// <summary>The players from the worked examples in docs/rules.md, plus a fourth.</summary>
internal static class TestPlayers
{
    public static readonly PlayerId Ana = new(1);
    public static readonly PlayerId Ben = new(2);
    public static readonly PlayerId Cal = new(3);
    public static readonly PlayerId Dee = new(4);
}

internal static class TestOptions
{
    public static readonly OptionKey Candy = OptionKey.Extra("LASTING_CANDY");

    /// <summary>Numbered slots 1..count, all available.</summary>
    public static DecisionOption[] Slots(int count) =>
        Enumerable.Range(1, count).Select(n => new DecisionOption(OptionKey.Slot(n))).ToArray();

    /// <summary>A card reward screen: the cards, any extra options, then Skip.</summary>
    public static DecisionOption[] CardReward(int cards, params OptionKey[] extras) =>
        Slots(cards)
            .Concat(extras.Select(extra => new DecisionOption(extra)))
            .Append(new DecisionOption(OptionKey.Skip))
            .ToArray();

    public static OptionKey Slot(int number) => OptionKey.Slot(number);
}
