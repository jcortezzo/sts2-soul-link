using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Events;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Events;

/// <summary>
/// The lock and vote indicators on a linked event page. Teammates' portraits sit on the options they clicked, as the
/// game shows votes on group events; options the local player can't take are dimmed; and a line at the top says
/// what each linked player has picked so far.
/// </summary>
internal static class EventView
{
    private static readonly AccessTools.FieldRef<NEventOptionButton, Tween?> AnimInTweenField =
        AccessTools.FieldRefAccess<NEventOptionButton, Tween?>("_animInTween");

    private static readonly Func<NEventOptionButton, int> IndexGetter =
        AccessTools.MethodDelegate<Func<NEventOptionButton, int>>(AccessTools.PropertyGetter(typeof(NEventOptionButton), "Index"));

    /// <summary>Re-applies the indicators after the linked state or the page changed.</summary>
    public static void Refresh()
    {
        if (EventExecutor.Room() is not { } room || room.Layout is not { } layout)
        {
            return;
        }

        var page = Current();
        foreach (var button in layout.OptionButtons)
        {
            if (!GodotObject.IsInstanceValid(button))
            {
                continue;
            }

            Dim.Set(button, page is { } linked && IsBlocked(linked, button));
            button.RefreshVotes();
        }

        StatusLine.Set(room, page is { } shown ? Status(shown) : "");
    }

    /// <summary>Re-applies the indicators once a button's entrance animation ends, since it fades the button in.</summary>
    public static void AfterAnimateIn(NEventOptionButton button)
    {
        if (AnimInTweenField(button) is { } tween && tween.IsValid())
        {
            tween.Finished += () => Views.Refresh("event", Refresh);
        }
        else
        {
            Refresh();
        }
    }

    /// <summary>Whether to show a player's portrait on an option: they voted for it, are waiting to, or chose it.</summary>
    public static bool ShowsVote(NEventOptionButton button, ulong playerNetId)
    {
        if (Current() is not { } page)
        {
            return false;
        }

        var player = new PlayerId(playerNetId);
        var key = OptionKey.Slot(IndexGetter(button) + 1);
        if (player == page.Me && EventExecutor.Held is { } held && held.Table == page.Table)
        {
            return held.Key == key;
        }

        return page.Decision.IsParticipant(player)
            && (page.Decision.VoteOf(player)?.Contains(key) == true || page.Decision.CommitOf(player)?.Choice.Contains(key) == true);
    }

    private static LinkedPage? Current()
    {
        if (SoulLinkSession.Current is not { Run: not null } session
            || EventLinks.LocalEvent() is not { } ev
            || !EventLinks.IsLinked(ev)
            || EventLinks.TableId(ev) is not { } table
            || EventLinks.DecisionOf(table) is not { } decision)
        {
            return null;
        }

        return new LinkedPage(session, table, decision);
    }

    private static bool IsBlocked(LinkedPage page, NEventOptionButton button)
    {
        var key = OptionKey.Slot(IndexGetter(button) + 1);
        return page.Decision.ViewOptions(page.Me).FirstOrDefault(view => view.Option == key) is { } view
            && view.State is OptionState.Locked or OptionState.GreyedOut or OptionState.NotChosen;
    }


    /// <summary>What each linked player has picked so far, like "You: want option 2     Ben: chose option 2".</summary>
    private static string Status(LinkedPage page)
    {
        if (page.Decision.Participants.Count < 2 && EventExecutor.Held is null)
        {
            return "";
        }

        return StatusLine.Join(page.Session, page.Decision.Participants, (player, isMe) => Doing(page, player, isMe));
    }

    private static string Doing(LinkedPage page, PlayerId player, bool isMe)
    {
        var decision = page.Decision;
        if (decision.CommitOf(player)?.Choice.Items[0] is { } chosen)
        {
            return $"chose {EventLinks.Describe(chosen)}";
        }

        if (isMe && EventExecutor.Held is { } held && held.Table == page.Table)
        {
            var names = string.Join(", ", held.Awaiting.Select(page.Session.NameOf));
            return $"want {EventLinks.Describe(held.Key)} (waiting for {names})";
        }

        return decision.VoteOf(player)?.Items[0] is { } vote
            ? $"{(isMe ? "want" : "wants")} {EventLinks.Describe(vote)}"
            : "choosing";
    }

    private sealed record LinkedPage(SoulLinkSession Session, string Table, OptionDecision Decision)
    {
        public PlayerId Me => Session.Me;
    }
}
