using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SoulLink.Core;
using SoulLink.Core.Campfires;
using SoulLink.Core.Decisions;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Campfires;

/// <summary>
/// Links the campfire's category, Recover (Rest, Mend) or Improve (Smith, Dig, Cook, Lift and the rest), while each
/// player picks their own option within it. A click is a vote for its category or a first-click choice; the mod does
/// the player's option in the game once the host says it's theirs to do.
/// </summary>
internal static class CampfireLinks
{
    private static readonly Color Dimmed = new(1f, 1f, 1f, 0.35f);

    private static readonly MethodInfo SelectOptionMethod =
        AccessTools.Method(typeof(NRestSiteButton), "SelectOption", [typeof(RestSiteOption)]);

    /// <summary>Options done in the game, as "table|option", so each linked option is done once.</summary>
    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);

    private static readonly HashSet<string> Joined = new(StringComparer.Ordinal);
    private static bool _scheduled;

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset()
    {
        Done.Clear();
        Joined.Clear();
    }

    /// <summary>The local campfire, while its options are shown.</summary>
    public static NRestSiteRoom? Room() =>
        NRestSiteRoom.Instance is { } room && GodotObject.IsInstanceValid(room) && room.IsInsideTree() ? room : null;

    public static string? TableId() => OptionLinks.Room is { } room ? $"{room}/campfire" : null;

    /// <summary>The campfire's linked state, once the local player has arrived.</summary>
    public static CampfireSession? Campfire(string table) =>
        OptionLinks.Session is { } session && session.State.Find<CampfireTable>(table)?.Campfire is { } campfire && campfire.IsAt(session.Me)
            ? campfire
            : null;

    /// <summary>Arrives at the campfire once its options are shown, with the local player's options and Miniature Tent.</summary>
    public static void JoinCurrent()
    {
        if (OptionLinks.Session is not { } session || Room() is not { } room || TableId() is not { } table || Joined.Contains(table)
            || room.Options is not { Count: > 0 } options)
        {
            return;
        }

        var hasTent = OptionLinks.Me?.Relics.OfType<MiniatureTent>().Any() == true;
        var args = CampfireTable.JoinArgs(hasTent, options.Select(option => new CampfireOption(option.OptionId, option.IsEnabled)).ToList(), null);
        Joined.Add(table);
        session.Submit(table, SyncActions.CampfireJoin, args);
    }

    /// <summary>
    /// Handles a click on a campfire option. The game acts on it only when it's an option the local player's choice
    /// already decided, for example after backing out of Smith's card screen; otherwise it goes to the host.
    /// </summary>
    /// <returns>True when the game should act on the click as it is.</returns>
    public static bool Click(RestSiteOption option)
    {
        if (OptionLinks.Session is not { } session || TableId() is not { } table)
        {
            return true;
        }

        JoinCurrent();
        if (Campfire(table) is { } campfire && campfire.ChosenBy(session.Me).Contains(option.OptionId, StringComparer.OrdinalIgnoreCase))
        {
            Done.Add($"{table}|{option.OptionId}");
            MarkSource(table, option.OptionId);
            return true;
        }

        ClickGate.Request(
        [
            new GatedOp(table, SyncActions.CampfireChoose, [option.OptionId], state =>
                state.Find<CampfireTable>(table)?.Campfire is { } linked && linked.IsAt(session.Me) ? linked.CanChoose(session.Me, option.OptionId) : null),
        ]);
        return false;
    }

    /// <summary>Checks for options to do after the linked state or the campfire changed. Once per frame at most.</summary>
    public static void Schedule()
    {
        if (_scheduled)
        {
            return;
        }

        _scheduled = true;
        Callable.From(Run).CallDeferred();
    }

    /// <summary>Shows who picked each option, dims options outside the linked category, and updates the status line.</summary>
    public static void Refresh()
    {
        if (Room() is not { } room)
        {
            return;
        }

        if (OptionLinks.Session is not { } session || TableId() is not { } table || Campfire(table) is not { } campfire)
        {
            StatusLine.Set(room, "");
            return;
        }

        var views = campfire.ViewOptions(session.Me).ToDictionary(view => view.Option, StringComparer.OrdinalIgnoreCase);
        foreach (var option in room.Options)
        {
            if (room.GetButtonForOption(option) is not { } button || !GodotObject.IsInstanceValid(button))
            {
                continue;
            }

            var blocked = views.TryGetValue(option.OptionId, out var view)
                && view.State is OptionState.Locked or OptionState.GreyedOut or OptionState.NotChosen;
            button.Modulate = blocked ? Dimmed : Colors.White;
            var backers = campfire.Campers.Where(player => Picked(campfire, player, option.OptionId)).ToList();
            VoteIcons.Show(button, VoteIcons.BoundsOf(button, null), backers);
        }

        StatusLine.Set(
            room,
            campfire.Campers.Count > 1 ? StatusLine.Join(session, campfire.Campers, (player, isMe) => Doing(campfire, player, isMe)) : "");
    }

    /// <summary>Marks the option as the source of a card screen it opens, like Smith's, so the screens of everyone who chose it link.</summary>
    private static void MarkSource(string table, string optionId) =>
        DeckChoices.Acting(table, optionId.ToUpperInvariant(), () =>
            Campfire(table) is { } campfire
                ? campfire.Campers.Where(player => campfire.ChosenBy(player).Contains(optionId, StringComparer.OrdinalIgnoreCase)).ToList()
                : []);

    private static bool Picked(CampfireSession campfire, PlayerId player, string optionId) =>
        string.Equals(campfire.PendingOf(player), optionId, StringComparison.OrdinalIgnoreCase)
        || campfire.ChosenBy(player).Contains(optionId, StringComparer.OrdinalIgnoreCase);

    private static string Doing(CampfireSession campfire, PlayerId player, bool isMe)
    {
        if (campfire.ChosenBy(player) is { Count: > 0 } chosen)
        {
            return $"chose {string.Join(" + ", chosen.Select(Describe))}";
        }

        return campfire.PendingOf(player) is { } pending ? $"{(isMe ? "want" : "wants")} {Describe(pending)}" : "choosing";
    }

    /// <summary>An option as players know it: the game's HEAL is Rest, and the rest are named as they read.</summary>
    private static string Describe(string optionId) =>
        string.Equals(optionId, CampfireOptions.Rest, StringComparison.OrdinalIgnoreCase)
            ? "Rest"
            : optionId.Length > 1 ? optionId[..1].ToUpperInvariant() + optionId[1..].ToLowerInvariant() : optionId;

    private static void Run()
    {
        _scheduled = false;
        try
        {
            JoinCurrent();
            Pump();
            Refresh();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't do a linked campfire option: {ex}");
        }
    }

    /// <summary>Does the next option the host decided for the local player, one at a time.</summary>
    private static void Pump()
    {
        if (OptionLinks.Session is not { } session || Room() is not { } room || TableId() is not { } table || Campfire(table) is not { } campfire)
        {
            return;
        }

        foreach (var optionId in campfire.ChosenBy(session.Me))
        {
            var key = $"{table}|{optionId}";
            if (Done.Contains(key))
            {
                continue;
            }

            var option = room.Options.FirstOrDefault(candidate => string.Equals(candidate.OptionId, optionId, StringComparison.OrdinalIgnoreCase));
            if (option is null || room.GetButtonForOption(option) is not { } button || !GodotObject.IsInstanceValid(button))
            {
                Done.Add(key);
                MainFile.Logger.Warn($"Soul Link couldn't find {optionId} at the campfire, so it's left to you.");
                continue;
            }

            Done.Add(key);
            MainFile.Logger.Info($"Soul Link chose {optionId} at the campfire.");
            MarkSource(table, optionId);
            ClickGate.RunApproved(button, () => SelectOptionMethod.Invoke(button, [option]));
            return;
        }
    }
}
