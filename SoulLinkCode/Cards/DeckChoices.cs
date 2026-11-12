using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using SoulLink.Core;
using SoulLink.Core.Cards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Cards;

/// <summary>
/// Links card choices that follow a linked choice: the card to Smith, remove, transform, enchant or otherwise pick
/// after everyone took the same event option, campfire option or shop slot. Deck cards are matched by the card
/// matching setting; cards offered on the spot, like Room Full of Cheese's commons, are matched by slot.
/// </summary>
/// <remarks>
/// The player picks on the game's own screen as usual. When the screen would finish with their pick, the pick goes to
/// the host as a vote or a first-click choice instead and the screen stays open, so with everyone agrees they can
/// change it. Once the host commits the local player's cards, the screen finishes with those.
/// </remarks>
internal static class DeckChoices
{
    /// <summary>How long after a linked choice a card screen opening still counts as following from it.</summary>
    private const ulong ContextWindowMsec = 15000;

    /// <summary>How long a pick waits for teammates' screens to open before it's sent anyway.</summary>
    private const ulong HoldLimitMsec = 8000;

    private static readonly AccessTools.FieldRef<NCardGridSelectionScreen, IReadOnlyList<CardModel>?> CardsField =
        AccessTools.FieldRefAccess<NCardGridSelectionScreen, IReadOnlyList<CardModel>?>("_cards");

    private static readonly AccessTools.FieldRef<NCardGridSelectionScreen, TaskCompletionSource<IEnumerable<CardModel>>?> CompletionField =
        AccessTools.FieldRefAccess<NCardGridSelectionScreen, TaskCompletionSource<IEnumerable<CardModel>>?>("_completionSource");

    private static readonly AccessTools.FieldRef<NCardGridSelectionScreen, NCardGrid?> GridField =
        AccessTools.FieldRefAccess<NCardGridSelectionScreen, NCardGrid?>("_grid");

    private static readonly Dictionary<string, int> ScreensPerContext = new(StringComparer.Ordinal);

    private static ChoiceContext? _context;
    private static OpenScreen? _open;
    private static bool _scheduled;
    private static bool _checkingHeld;

    /// <summary>The screen whose overlay mustn't close while its pick is intercepted.</summary>
    public static NCardGridSelectionScreen? KeepOpen { get; set; }

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset()
    {
        ScreensPerContext.Clear();
        _context = null;
        _open = null;
        KeepOpen = null;
    }

    /// <summary>
    /// Marks the linked choice the local player just made, so a card screen it opens is linked with the screens of
    /// the players who made the same choice.
    /// </summary>
    /// <param name="table">The linked decision's table.</param>
    /// <param name="option">The option taken.</param>
    /// <param name="sameChoice">The players who made the same choice, who will get the same screen.</param>
    public static void Acting(string table, string option, Func<IReadOnlyList<PlayerId>> sameChoice) =>
        _context = new ChoiceContext($"{table}|{option}", sameChoice, Time.GetTicksMsec());

    /// <summary>Whether the mod links this screen's pick.</summary>
    public static bool IsLinked(NCardGridSelectionScreen screen) =>
        _open is { } open && ReferenceEquals(open.Screen, screen) && GodotObject.IsInstanceValid(screen) && OptionLinks.Session is not null;

    /// <summary>
    /// Called when the game starts waiting for a card screen: links it if it follows a linked choice. Combat's card
    /// screens are never linked.
    /// </summary>
    public static void Opened(NCardGridSelectionScreen screen)
    {
        _open = null;
        if (screen is NCombatPileCardSelectScreen
            || OptionLinks.Session is not { } session
            || _context is not { } context
            || Time.GetTicksMsec() - context.Since > ContextWindowMsec
            || CardsField(screen) is not { Count: > 0 } cards)
        {
            return;
        }

        var ids = cards.Select(CardIds.IdOf).ToList();
        var fromDeck = ids.TrueForAll(id => id is not null);
        if (!fromDeck && ids.Exists(id => id is not null))
        {
            return;
        }

        var number = ScreensPerContext[context.Key] = ScreensPerContext.GetValueOrDefault(context.Key) + 1;
        var table = $"{context.Key}/cards-{number}";
        var pickCount = Math.Max(1, PrefsOf(screen)?.MaxSelect ?? 1);
        _open = new OpenScreen(screen, table, fromDeck, pickCount, cards, context.SameChoice);
        var args = fromDeck
            ? CardTable.JoinArgs(pickCount, ids.Select(id => new ScreenCard(id!.Value)))
            : OptionTable.JoinArgs(pickCount, Enumerable.Range(1, cards.Count).Select(slot => new DecisionOption(OptionKey.Slot(slot))));
        session.Submit(table, fromDeck ? SyncActions.CardsJoin : SyncActions.OptionsJoin, args);
        MainFile.Logger.Info($"Soul Link linked the card screen {table}.");
        Schedule();
    }

    /// <summary>
    /// The player's pick, taken from the screen as it was about to finish: sent to the host, unless it can't be linked,
    /// in which case the screen finishes with it as usual.
    /// </summary>
    public static void Picked(NCardGridSelectionScreen screen, IReadOnlyList<CardModel> picked)
    {
        if (_open is not { } open || !ReferenceEquals(open.Screen, screen))
        {
            Finish(screen, picked);
            return;
        }

        if (picked.Count != open.PickCount || picked.Any(card => IndexOf(open.Cards, card) < 0))
        {
            MainFile.Logger.Info($"Soul Link left a pick of {picked.Count} on {open.Table} unlinked.");
            _open = null;
            Finish(screen, picked);
            return;
        }

        // Confirming the pick that's already the player's vote keeps it, rather than taking it back.
        if (open.Sent is { } sent && sent.Count == picked.Count && sent.All(card => picked.Contains(card)))
        {
            return;
        }

        var held = new HeldPick(picked, Time.GetTicksMsec());
        _open = open with { Held = held };
        Pump();
        Views.Refresh("card choice", Refresh);
    }

    /// <summary>Checks for picks to send or finish after the linked state changed. Once per frame at most.</summary>
    public static void Schedule()
    {
        if (_scheduled)
        {
            return;
        }

        _scheduled = true;
        Callable.From(Run).CallDeferred();
    }

    /// <summary>Shows who picked which card, dims cards the local player can't pick, and updates the status line.</summary>
    public static void Refresh()
    {
        if (_open is not { } open || !GodotObject.IsInstanceValid(open.Screen) || OptionLinks.Session is not { } session)
        {
            return;
        }

        var grid = GridField(open.Screen);
        var decision = Decision(open);
        foreach (var card in open.Cards)
        {
            if (grid?.GetCardHolder(card) is not { } holder || !GodotObject.IsInstanceValid(holder))
            {
                continue;
            }

            var view = decision?.Views.FirstOrDefault(entry => entry.Card == card);
            Dim.Set(holder, view?.State is OptionState.Locked or OptionState.GreyedOut or OptionState.NotChosen);
            var backers = new List<PlayerId>();
            if (open.Held?.Cards.Contains(card) == true)
            {
                backers.Add(session.Me);
            }

            if (view is { } shown)
            {
                backers.AddRange(shown.Backers.Where(player => !backers.Contains(player)));
            }

            VoteIcons.Show(holder, VoteIcons.BoundsOf(holder, holder.Hitbox), backers);
        }

        StatusLine.Set(open.Screen, decision is { Participants.Count: > 1 } ? decision.Status(session) : "");
    }

    private static int IndexOf(IReadOnlyList<CardModel> cards, CardModel card)
    {
        for (var i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], card))
            {
                return i;
            }
        }

        return -1;
    }

    private static CardSelectorPrefs? PrefsOf(NCardGridSelectionScreen screen) =>
        AccessTools.Field(screen.GetType(), "_prefs")?.GetValue(screen) is CardSelectorPrefs prefs ? prefs : null;

    private static void Run()
    {
        _scheduled = false;
        try
        {
            Pump();
            Refresh();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't do a linked card pick: {ex}");
        }
    }

    private static void Pump()
    {
        if (_open is not { } open || OptionLinks.Session is not { } session)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(open.Screen))
        {
            _open = null;
            return;
        }

        if (Decision(open) is { } decision && decision.CommittedCards is { } committed)
        {
            _open = null;
            MainFile.Logger.Info($"Soul Link picked {committed.Count} card(s) on {open.Table}.");
            Finish(open.Screen, committed);
            return;
        }

        if (open.Held is not { } held)
        {
            return;
        }

        var joined = Decision(open)?.Participants ?? [];
        var awaited = open.SameChoice().Where(player => player != session.Me && !joined.Contains(player)).ToList();
        if (awaited.Count > 0 && Time.GetTicksMsec() - held.Since < HoldLimitMsec)
        {
            CheckHeldLater();
            return;
        }

        _open = open with { Held = null, Sent = held.Cards };
        var op = open.FromDeck
            ? new GatedOp(open.Table, SyncActions.CardsChoose, held.Cards.Select(card => SyncArgs.Number(CardIds.IdOf(card)!.Value.Value)).ToList(), state =>
                state.Find<CardTable>(open.Table)?.Decision is { } cards && cards.IsParticipant(session.Me)
                    ? cards.CanCommit(session.Me, new Selection<CardId>(held.Cards.Select(card => CardIds.IdOf(card)!.Value)))
                    : null)
            : OptionLinks.Choose(open.Table, held.Cards.Select(card => OptionKey.Slot(IndexOf(open.Cards, card) + 1)).ToArray());
        ClickGate.Request([op]);
    }

    private static void CheckHeldLater()
    {
        if (_checkingHeld || Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        _checkingHeld = true;
        tree.CreateTimer(1).Timeout += () =>
        {
            _checkingHeld = false;
            Schedule();
        };
    }

    /// <summary>Finishes the screen with these cards, as the game does when a pick is complete.</summary>
    private static void Finish(NCardGridSelectionScreen screen, IReadOnlyList<CardModel> cards)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        CompletionField(screen)?.TrySetResult(cards);
        if (ReferenceEquals(KeepOpen, screen))
        {
            KeepOpen = null;
        }

        if (screen.IsInsideTree())
        {
            NOverlayStack.Instance?.Remove(screen);
        }
    }

    private static LinkedPick? Decision(OpenScreen open)
    {
        if (OptionLinks.Session is not { } session)
        {
            return null;
        }

        if (open.FromDeck)
        {
            return session.State.Find<CardTable>(open.Table)?.Decision is { } cards && cards.IsParticipant(session.Me)
                ? new LinkedPick(cards.Participants, open, cards, null)
                : null;
        }

        return OptionLinks.DecisionOf(open.Table) is { } slots ? new LinkedPick(slots.Participants, open, null, slots) : null;
    }

    /// <summary>The linked choice that opened card screens, with the players who made the same choice.</summary>
    private sealed record ChoiceContext(string Key, Func<IReadOnlyList<PlayerId>> SameChoice, ulong Since);

    /// <summary>A pick waiting for teammates' screens to open.</summary>
    private sealed record HeldPick(IReadOnlyList<CardModel> Cards, ulong Since);

    /// <summary>The local player's linked card screen.</summary>
    private sealed record OpenScreen(
        NCardGridSelectionScreen Screen,
        string Table,
        bool FromDeck,
        int PickCount,
        IReadOnlyList<CardModel> Cards,
        Func<IReadOnlyList<PlayerId>> SameChoice,
        HeldPick? Held = null,
        IReadOnlyList<CardModel>? Sent = null);

    /// <summary>How one card looks on the local player's screen: its state, and who picked it or holds them to it.</summary>
    private sealed record CardView(CardModel Card, OptionState State, IReadOnlyList<PlayerId> Backers);

    /// <summary>The local copy of a linked card screen's decision, by card id for deck cards or by slot for offered cards.</summary>
    private sealed record LinkedPick(IReadOnlyList<PlayerId> Participants, OpenScreen Open, CardChoiceDecision? Cards, OptionDecision? Slots)
    {
        public IReadOnlyList<CardModel>? CommittedCards
        {
            get
            {
                var me = OptionLinks.Session?.Me ?? default;
                if (Cards?.CommitOf(me) is { } cardCommit)
                {
                    return cardCommit.Choice.Items.Select(id => Open.Cards.FirstOrDefault(card => CardIds.IdOf(card) == id)).OfType<CardModel>().ToList();
                }

                if (Slots?.CommitOf(me) is { } slotCommit)
                {
                    return slotCommit.Choice.Items
                        .Select(key => key.SlotNumber is { } slot && slot <= Open.Cards.Count ? Open.Cards[slot - 1] : null)
                        .OfType<CardModel>()
                        .ToList();
                }

                return null;
            }
        }

        public IEnumerable<CardView> Views
        {
            get
            {
                var me = OptionLinks.Session?.Me ?? default;
                foreach (var card in Open.Cards)
                {
                    if (Cards is not null && CardIds.IdOf(card) is { } id)
                    {
                        var view = Cards.ViewOptions(me).FirstOrDefault(entry => entry.Option == id);
                        yield return new CardView(card, view.State, Backers(Cards, me, id, view));
                    }
                    else if (Slots is not null)
                    {
                        var key = OptionKey.Slot(IndexOf(Open.Cards, card) + 1);
                        var view = Slots.ViewOptions(me).FirstOrDefault(entry => entry.Option == key);
                        yield return new CardView(card, view.State, Backers(Slots, me, key, view));
                    }
                }
            }
        }

        /// <summary>What each linked player is doing, like "You: picking     Ben: picked".</summary>
        public string Status(SoulLinkSession session) =>
            StatusLine.Join(session, Participants, (player, isMe) =>
            {
                var decided = Cards?.HasDecided(player) ?? Slots?.HasDecided(player) ?? false;
                var voted = (Cards?.VoteOf(player) is not null) || (Slots?.VoteOf(player) is not null)
                    || (isMe && (Open.Held is not null || Open.Sent is not null));
                return decided ? "picked" : voted ? (isMe ? "want this" : "has a pick") : "picking";
            });

        private static List<PlayerId> Backers<TAtom>(LinkedChoice<TAtom> decision, PlayerId me, TAtom option, OptionView<TAtom> view)
            where TAtom : notnull
        {
            var backers = new List<PlayerId>();
            if (decision.VoteOf(me)?.Contains(option) == true || decision.CommitOf(me)?.Choice.Contains(option) == true)
            {
                backers.Add(me);
            }

            backers.AddRange(decision.VotersFor(me, option));
            if (view.State == OptionState.Required && view.Because is { } leader && !backers.Contains(leader))
            {
                backers.Add(leader);
            }

            return backers;
        }
    }
}
