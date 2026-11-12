using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Events;

/// <summary>
/// Maps event and Ancient pages onto linked option decisions. Each page a player is shown is its own decision, named
/// by the room, the page and how many times the player has chosen on that page before, so players on the same page
/// share it. Options are matched by slot, counted in the order each player sees them.
/// </summary>
/// <remarks>
/// Events the game puts to a group vote are left to the game's vote, except with first click, where the first click
/// casts the same vote for everyone. A page with a single option isn't a decision, so it isn't linked.
/// </remarks>
internal static class EventLinks
{
    private const string PagesMarker = ".pages.";
    private const string OptionsMarker = ".options.";

    private static readonly AccessTools.FieldRef<EventSynchronizer, uint> GroupPageField =
        AccessTools.FieldRefAccess<EventSynchronizer, uint>("_pageIndex");

    /// <summary>How many options each player has chosen per page in each room, keyed by room, player and page.</summary>
    private static readonly Dictionary<string, int> Choices = new(StringComparer.Ordinal);

    /// <summary>How many options each player has chosen in each room, keyed by room and player.</summary>
    private static readonly Dictionary<string, int> RoomChoices = new(StringComparer.Ordinal);

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset()
    {
        Choices.Clear();
        RoomChoices.Clear();
    }

    public static EventSynchronizer? Synchronizer => RunManager.Instance?.EventSynchronizer;

    /// <summary>The local player's event, or null outside events.</summary>
    public static EventModel? LocalEvent()
    {
        try
        {
            return Synchronizer?.GetLocalEvent();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            // Outside an event room the synchronizer has no events to look in.
            return null;
        }
    }

    /// <summary>A player's event, or null when they aren't in one. Every player's event runs on every machine.</summary>
    public static EventModel? EventOf(Player player)
    {
        try
        {
            return Synchronizer?.GetEventForPlayer(player);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the event's current page is a linked decision: not over, a real choice, and not a group vote unless the
    /// first click decides.
    /// </summary>
    public static bool IsLinked(EventModel ev) =>
        ev.Owner is not null && !ev.IsFinished && ev.CurrentOptions.Count >= 2 && (!ev.IsShared || IsFirstClick);

    /// <summary>The linked table for the event's current page.</summary>
    public static string? TableId(EventModel ev)
    {
        if (ev.Owner is not { } owner)
        {
            return null;
        }

        var page = PageOf(ev);
        if (ev.IsShared)
        {
            // Everyone is on the same page of a group vote, and the game numbers its pages.
            return Synchronizer is { } synchronizer ? $"{RoomOf(owner)}/group-event/{page}/{GroupPageField(synchronizer) + 1}" : null;
        }

        return $"{RoomOf(owner)}/event/{page}/{Choices.GetValueOrDefault(ChoiceKey(owner, page)) + 1}";
    }

    /// <summary>
    /// Counts a player's choice on their current page, before the game applies it, so the next page they see is a new
    /// decision even when it's the same page again.
    /// </summary>
    public static void CountChoice(EventSynchronizer synchronizer, Player player)
    {
        var ev = synchronizer.GetEventForPlayer(player);
        if (ev is null || ev.IsShared || ev.IsFinished)
        {
            return;
        }

        var key = ChoiceKey(player, PageOf(ev));
        Choices[key] = Choices.GetValueOrDefault(key) + 1;
        var room = RoomKey(player);
        RoomChoices[room] = RoomChoices.GetValueOrDefault(room) + 1;
    }

    /// <summary>The slot keys and availability of the event's current options, in display order.</summary>
    public static IReadOnlyList<DecisionOption> OptionsOf(EventModel ev)
    {
        var options = new List<DecisionOption>();
        for (var i = 0; i < ev.CurrentOptions.Count; i++)
        {
            options.Add(new DecisionOption(OptionKey.Slot(i + 1), IsAvailable(ev, ev.CurrentOptions[i])));
        }

        return options;
    }

    /// <summary>
    /// Whether the event's owner can take the option: it isn't locked, and it wouldn't kill them, which the game
    /// doesn't allow in co-op.
    /// </summary>
    public static bool IsAvailable(EventModel ev, EventOption option) =>
        !option.IsLocked && !(ev.Owner is { } owner && option.WillKillPlayer?.Invoke(owner) == true);

    /// <summary>Joins the local player's current page once, with what it offers.</summary>
    public static IReadOnlyList<GatedOp> JoinOps(EventModel ev) =>
        IsLinked(ev) && TableId(ev) is { } table ? OptionLinks.JoinOps(table, OptionsOf(ev)) : [];

    /// <summary>Joins the local player's current page, if it's linked, so mean mode knows their options before anyone picks.</summary>
    public static void JoinCurrent()
    {
        if (OptionLinks.Session is not null && LocalEvent() is { } ev)
        {
            OptionLinks.Submit(JoinOps(ev));
        }
    }

    /// <summary>The operation for a click on an option, checked against the local linked state.</summary>
    public static GatedOp Choose(string table, OptionKey key) => OptionLinks.Choose(table, key);

    /// <summary>The page's linked decision, once the local player has joined it.</summary>
    public static OptionDecision? DecisionOf(string table) => OptionLinks.DecisionOf(table);

    /// <summary>
    /// The other players a first click on the local player's page waits for: players on the same page who haven't
    /// joined it yet, and players still on an earlier choice of this event, who may be about to reach it. That way a
    /// fast click doesn't decide for players whose page hasn't opened yet.
    /// </summary>
    public static IReadOnlyList<PlayerId> Awaited(EventModel mine)
    {
        // A group vote's page is the same for everyone, and players who join late follow the first click.
        if (mine.IsShared || Synchronizer is not { } synchronizer || mine.Owner is not { } me || TableId(mine) is not { } table)
        {
            return [];
        }

        var decision = DecisionOf(table);
        var myChoices = RoomChoices.GetValueOrDefault(RoomKey(me));
        var awaited = new List<PlayerId>();
        foreach (var ev in synchronizer.Events)
        {
            if (ev.Owner is not { } owner || LocalContext.IsMe(owner) || ev.IsFinished || ev.IsShared)
            {
                continue;
            }

            var player = new PlayerId(owner.NetId);
            var onSamePage = TableId(ev) == table;
            if ((onSamePage && IsLinked(ev) && decision?.IsParticipant(player) != true)
                || (!onSamePage && RoomChoices.GetValueOrDefault(RoomKey(owner)) < myChoices))
            {
                awaited.Add(player);
            }
        }

        return awaited;
    }

    /// <summary>
    /// The page an event is on, from its options' text keys, which look like <c>EVENT.pages.PAGE.options.OPTION</c>.
    /// </summary>
    public static string PageOf(EventModel ev)
    {
        foreach (var option in ev.CurrentOptions)
        {
            var key = option.TextKey;
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var start = key.IndexOf(PagesMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += PagesMarker.Length;
            var end = key.IndexOf(OptionsMarker, start, StringComparison.Ordinal);
            if (end > start)
            {
                return key[start..end];
            }
        }

        return "PAGE";
    }

    private static bool IsFirstClick => OptionLinks.Session?.Run?.Settings.WhoDecides == WhoDecides.FirstClick;

    private static string RoomOf(Player player) => player.RunState.RunLocation.ToString();

    private static string RoomKey(Player player) => $"{RoomOf(player)}|{player.NetId}";

    private static string ChoiceKey(Player player, string page) => $"{RoomKey(player)}|{page}";
}
