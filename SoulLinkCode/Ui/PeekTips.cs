using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Linking;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// Builds the hover tips for slot peek, which show what teammates have in the slot the local player hovers: their
/// card, relic, potion or option, headed with their name and portrait so it can't be mistaken for the player's own.
/// </summary>
internal static class PeekTips
{
    private const string IdPrefix = "SoulLink.Peek.";

    private static readonly PropertyInfo TitleProperty = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title));
    private static readonly PropertyInfo DescriptionProperty = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description));
    private static readonly PropertyInfo IconProperty = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Icon));
    private static readonly PropertyInfo IdProperty = AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Id));
    private static readonly Regex Tags = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    private static int _made;

    /// <summary>A text tip about a teammate, headed with their name and portrait.</summary>
    /// <param name="player">The teammate.</param>
    /// <param name="description">The text, which may use the game's text tags.</param>
    /// <param name="subject">What the tip is about, shown after the teammate's name in the heading.</param>
    /// <param name="icon">The picture; the teammate's character portrait by default.</param>
    public static IHoverTip About(PlayerId player, string description, string? subject = null, Texture2D? icon = null)
    {
        // The game only builds text tips from its own localization tables, so the tip's text is set directly.
        object tip = default(HoverTip);
        TitleProperty.SetValue(tip, string.IsNullOrEmpty(subject) ? NameOf(player) : $"{NameOf(player)}: {Plain(subject)}");
        DescriptionProperty.SetValue(tip, description);
        IconProperty.SetValue(tip, icon ?? OptionLinks.PlayerOf(player)?.Character?.IconTexture);
        IdProperty.SetValue(tip, $"{IdPrefix}{player.Value}.{++_made}");
        return (IHoverTip)tip;
    }

    /// <summary>
    /// One of the game's text tips, like a relic's, retitled with the teammate's name, with an optional line added.
    /// Card tips are returned as they are.
    /// </summary>
    public static IHoverTip Retitled(PlayerId player, IHoverTip tip, string? note = null)
    {
        if (tip is not HoverTip text)
        {
            return tip;
        }

        object copy = text;
        TitleProperty.SetValue(copy, string.IsNullOrEmpty(text.Title) ? NameOf(player) : $"{NameOf(player)}: {text.Title}");
        if (note is not null)
        {
            DescriptionProperty.SetValue(copy, string.IsNullOrEmpty(text.Description) ? note : $"{text.Description}\n{note}");
        }

        IdProperty.SetValue(copy, $"{IdPrefix}{player.Value}.{++_made}");
        return (IHoverTip)copy;
    }

    /// <summary>
    /// A teammate's card: a line saying what they have, and the card itself unless it's the same as the local
    /// player's.
    /// </summary>
    /// <param name="player">The teammate.</param>
    /// <param name="theirs">Their card.</param>
    /// <param name="mine">The local player's card in the same slot, if any.</param>
    /// <param name="has">What they do with the card, like "Has" or "Would pick".</param>
    /// <param name="where">Where the card is, like "in this slot".</param>
    /// <param name="note">An extra sentence, like the card's price.</param>
    public static IEnumerable<IHoverTip> Card(PlayerId player, CardModel theirs, CardModel? mine, string has, string where, string? note = null)
    {
        var suffix = note is null ? "" : " " + note;
        if (mine is not null && CardValue(mine) == CardValue(theirs))
        {
            return [About(player, $"{has} the same card {where}.{suffix}")];
        }

        return [About(player, $"{has} {Gold(theirs.Title)} {where}.{suffix}"), HoverTipFactory.FromCard(theirs)];
    }

    /// <summary>
    /// The cards teammates shared for a slot on a linked screen, with what the local player has there.
    /// </summary>
    public static IReadOnlyList<IHoverTip> SharedCards(SoulLinkSession session, string table, OptionKey key, CardModel? mine)
    {
        var tips = new List<IHoverTip>();
        foreach (var (player, slots) in session.State.PeeksOf(table).OrderBy(entry => entry.Key.Value))
        {
            if (player == session.Me || !slots.TryGetValue(key.Value, out var value))
            {
                continue;
            }

            if (CardOf(value) is { } theirs)
            {
                tips.AddRange(Card(player, theirs, mine, "Has", "in this slot"));
            }
        }

        return tips;
    }

    /// <summary>A card as text other players can rebuild: its id and how many times it's upgraded.</summary>
    public static string CardValue(CardModel card) =>
        $"{card.Id}|{card.CurrentUpgradeLevel.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The card a <see cref="CardValue"/> describes, or null for one this game doesn't know.</summary>
    public static CardModel? CardOf(string value)
    {
        var parts = value.Split('|');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var level))
        {
            return null;
        }

        ModelId id;
        try
        {
            id = ModelId.Deserialize(parts[0]);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (ModelDb.GetByIdOrNull<CardModel>(id) is not { } canonical)
        {
            return null;
        }

        var card = canonical.ToMutable();
        while (card.CurrentUpgradeLevel < Math.Min(level, card.MaxUpgradeLevel))
        {
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
        }

        return card;
    }

    /// <summary>Formatted text from the game, or null when it can't be formatted.</summary>
    public static string? Text(LocString? text)
    {
        if (text is null || text.IsEmpty)
        {
            return null;
        }

        try
        {
            return text.GetFormattedText();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Text without the game's text tags, for headings, which show tags as they are.</summary>
    public static string Plain(string text) => Tags.Replace(text, "");

    /// <summary>A name in the game's gold highlight, escaped so it can't be read as text tags.</summary>
    public static string Gold(string name) => $"[gold]{name.Replace("[", "[lb]", StringComparison.Ordinal)}[/gold]";

    private static string NameOf(PlayerId player) => OptionLinks.Session?.NameOf(player) ?? "Teammate";
}
