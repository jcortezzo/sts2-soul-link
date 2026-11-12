using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core.Cards;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Linking;

namespace SoulLink.SoulLinkCode.Cards;

/// <summary>
/// Keeps the linked cards setting's link groups the same on every peer: the host pairs the starting decks, each
/// player reports the cards they gain from a linked choice, and every peer applies transforms and removals itself,
/// since every peer sees every deck change.
/// </summary>
internal static class CardLinksSync
{
    /// <summary>How long after a linked choice a card joining the deck still counts as gained from it.</summary>
    private const ulong GainWindowMsec = 15000;

    /// <summary>Transformed cards by the card they replace, until the replacement joins the deck.</summary>
    private static readonly ConditionalWeakTable<CardModel, CardModel> Replacing = new();

    private static GainSource? _source;

    /// <summary>The linked choice the local player just made, which the cards they gain next came from.</summary>
    private sealed record GainSource(string Key, ulong Since);

    public static void Initialize()
    {
        CardIds.Added += OnAdded;
        CardIds.Removed += OnRemoved;
    }

    /// <summary>Numbers the decks and, on the host, pairs the starting decks once the run's session has started.</summary>
    public static void StartRun(RunState? state)
    {
        _source = null;
        CardIds.Reset(state);
        if (state is null || SoulLinkSession.Current is not { IsHost: true } session)
        {
            return;
        }

        var decks = state.Players.Select(player => new StarterDeck(
                OptionLinks.IdOf(player),
                player.Deck.Cards
                    .Where(card => card.Rarity == CardRarity.Basic && CardIds.IdOf(card) is not null)
                    .Select(card => new StarterCard(CardIds.IdOf(card)!.Value, PairingName(card)))
                    .ToList()))
            .ToList();
        session.Submit("links", SyncActions.LinksStarters, SyncState.StarterArgs(decks));
    }

    /// <summary>
    /// Marks the choice the local player just made, so a card they gain from it links with the cards the other
    /// players gained from the same choice.
    /// </summary>
    /// <param name="table">The linked decision's table.</param>
    /// <param name="option">The option taken.</param>
    /// <param name="root">The sequence of the lead commit or purchase that decided it.</param>
    public static void GainingFrom(string table, string option, int root) =>
        _source = new GainSource(GainLinks.SourceKey(table, option, root), Time.GetTicksMsec());

    /// <summary>Records that a card is about to be replaced by a transform, so the new card keeps its link.</summary>
    public static void Transforming(CardModel original, CardModel? replacement)
    {
        if (replacement is not null && !ReferenceEquals(original, replacement))
        {
            Replacing.AddOrUpdate(replacement, original);
        }
    }

    /// <summary>Strike and Defend pair across characters; every other starting card pairs by what it is.</summary>
    private static string PairingName(CardModel card) =>
        card.Tags.Contains(CardTag.Strike) ? "Strike"
        : card.Tags.Contains(CardTag.Defend) ? "Defend"
        : card.Id.Entry;

    private static void OnAdded(Player owner, CardModel card, CardId id)
    {
        if (SoulLinkSession.Current?.Run is not { } run)
        {
            return;
        }

        if (Replacing.TryGetValue(card, out var original))
        {
            Replacing.Remove(card);
            if (CardIds.RefOf(original) is { } old)
            {
                run.Links.Replace(old, new CardRef(old.Owner, id));
            }

            return;
        }

        if (LocalContext.IsMe(owner) && _source is { } source && Time.GetTicksMsec() - source.Since < GainWindowMsec
            && OptionLinks.Session is { } session)
        {
            session.Submit("links", SyncActions.LinksGained, SyncState.GainedArgs(source.Key, [id]));
        }
    }

    private static void OnRemoved(Player owner, CardModel card, CardId id)
    {
        // A transformed card keeps its place in its group, handed to its replacement when that joins the deck.
        if (SoulLinkSession.Current?.Run is { } run && !IsBeingReplaced(card))
        {
            run.Links.Remove(new CardRef(OptionLinks.IdOf(owner), id));
        }
    }

    private static bool IsBeingReplaced(CardModel card)
    {
        foreach (var (_, original) in Replacing)
        {
            if (ReferenceEquals(original, card))
            {
                return true;
            }
        }

        return false;
    }
}
