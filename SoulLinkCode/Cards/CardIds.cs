using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core;
using SoulLink.Core.Cards;
using SoulLink.SoulLinkCode.Linking;

namespace SoulLink.SoulLinkCode.Cards;

/// <summary>
/// Gives every card in every player's deck an id that stays the same for the whole run and is the same on every peer,
/// so linked choices and link groups can name cards over the network.
/// </summary>
/// <remarks>
/// Every peer keeps every player's deck in step, adding the same cards in the same order. Ids count up per player in
/// that order, starting from the decks as they are when the run starts or loads, so they match on every peer.
/// </remarks>
internal static class CardIds
{
    /// <summary>Room for this many cards per player before ids of different players could meet.</summary>
    private const long PerPlayer = 1_000_000;

    private static readonly ConditionalWeakTable<CardModel, StrongBox<long>> Ids = new();
    private static readonly Dictionary<ulong, long> Counts = new();

    /// <summary>Raised when a card joins a deck and gets its id, with its owner.</summary>
    public static event Action<Player, CardModel, CardId>? Added;

    /// <summary>Raised when a card with an id leaves a deck, with its owner.</summary>
    public static event Action<Player, CardModel, CardId>? Removed;

    /// <summary>Numbers every deck from scratch, at the start or load of a run.</summary>
    public static void Reset(RunState? state)
    {
        Ids.Clear();
        Counts.Clear();
        if (state is null)
        {
            return;
        }

        foreach (var player in state.Players)
        {
            foreach (var card in player.Deck.Cards)
            {
                Assign(state, player, card);
            }
        }
    }

    /// <summary>The card's id, or null for a card that isn't in a deck.</summary>
    public static CardId? IdOf(CardModel card) => Ids.TryGetValue(card, out var id) ? new CardId(id.Value) : null;

    /// <summary>The player's deck card with this id, or null.</summary>
    public static CardModel? CardOf(Player player, CardId id) => player.Deck.Cards.FirstOrDefault(card => IdOf(card) == id);

    public static CardRef? RefOf(CardModel card) =>
        card.Owner is { } owner && IdOf(card) is { } id ? new CardRef(OptionLinks.IdOf(owner), id) : null;

    /// <summary>Called when a card joins any pile: deck cards get the next id of their owner.</summary>
    public static void OnAdded(CardPile pile, CardModel card)
    {
        if (pile.Type != PileType.Deck || card.Owner is not { } owner || !ReferenceEquals(owner.Deck, pile)
            || OptionLinks.RunState is not { } state || IdOf(card) is not null)
        {
            return;
        }

        var id = Assign(state, owner, card);
        Added?.Invoke(owner, card, id);
    }

    /// <summary>Called when a card leaves any pile.</summary>
    public static void OnRemoved(CardPile pile, CardModel card)
    {
        if (pile.Type == PileType.Deck && card.Owner is { } owner && ReferenceEquals(owner.Deck, pile) && IdOf(card) is { } id)
        {
            Removed?.Invoke(owner, card, id);
        }
    }

    private static CardId Assign(RunState state, Player player, CardModel card)
    {
        var count = Counts.GetValueOrDefault(player.NetId) + 1;
        Counts[player.NetId] = count;
        var id = (state.GetPlayerSlotIndex(player) + 1) * PerPlayer + count;
        Ids.AddOrUpdate(card, new StrongBox<long>(id));
        return new CardId(id);
    }
}
