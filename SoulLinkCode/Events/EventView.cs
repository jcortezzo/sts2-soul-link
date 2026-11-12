using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Events;

/// <summary>
/// The lock and vote indicators on a linked event page. Teammates' portraits sit on the options they clicked, as the
/// game shows votes on group events, options the local player can't take are dimmed, and hovering an option shows
/// each teammate's option in the same slot when theirs is different.
/// </summary>
internal static class EventView
{
    /// <summary>How far the portraits on an Ancient's option stay from its bottom edge.</summary>
    private const float AncientVoteMargin = 4;
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
            if (button.Event is AncientEventModel)
            {
                KeepVotesInside(button);
            }
        }
    }

    /// <summary>
    /// Keeps the portraits on an Ancient's option inside the option, so nothing around it cuts them off. The game
    /// never shows votes on Ancient options itself, so their portrait row isn't made to fit.
    /// </summary>
    private static void KeepVotesInside(NEventOptionButton button)
    {
        if (button.VoteContainer is not { } votes || !GodotObject.IsInstanceValid(votes))
        {
            return;
        }

        votes.ClipContents = false;
        var height = votes.GetChildren().OfType<Control>().Select(icon => icon.GetCombinedMinimumSize().Y).DefaultIfEmpty(0f).Max() * votes.Scale.Y;
        var limit = button.Size.Y - AncientVoteMargin;
        if (height > 0 && votes.Position.Y + height > limit)
        {
            // The row is the option's own child, so its position is in the option's coordinates.
            votes.Position = new Vector2(votes.Position.X, Math.Max(0, limit - height));
        }
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

    /// <summary>
    /// Slot peek: each teammate's option in the hovered slot, when it differs from the local player's, as at an
    /// Ancient, where everyone is offered different relics, or when they can't take it. Every player's event runs on
    /// every machine, so nothing needs sharing.
    /// </summary>
    public static IReadOnlyList<IHoverTip> Peek(NEventOptionButton button)
    {
        if (Current() is not { } page || button.Option is not { } mine || OptionLinks.RunState is not { } state)
        {
            return [];
        }

        var index = IndexGetter(button);
        var tips = new List<IHoverTip>();
        foreach (var teammate in state.Players)
        {
            var player = OptionLinks.IdOf(teammate);
            if (player == page.Me
                || EventLinks.EventOf(teammate) is not { } ev
                || ev.IsShared
                || index < 0
                || index >= ev.CurrentOptions.Count
                || EventLinks.TableId(ev) != page.Table)
            {
                continue;
            }

            var theirs = ev.CurrentOptions[index];
            var available = EventLinks.IsAvailable(ev, theirs);
            if (!Same(mine, theirs))
            {
                // The game adds the event's numbers to an option's text when it shows the option, so do the same.
                foreach (var text in new[] { theirs.Title, theirs.Description }.OfType<LocString>())
                {
                    ev.DynamicVars.AddTo(text);
                }
                var description = PeekTips.Text(theirs.Description) ?? "";
                if (!available)
                {
                    description = description.Length > 0 ? $"{description}\nThey can't take it." : "They can't take it.";
                }

                tips.Add(PeekTips.About(player, description, PeekTips.Text(theirs.Title), theirs.Relic?.Icon));
            }
            else if (!available)
            {
                tips.Add(PeekTips.About(player, "Can't take this option."));
            }
        }

        return tips;
    }

    /// <summary>Whether two options are the same option, like the same event's same choice, without formatting their text.</summary>
    private static bool Same(EventOption mine, EventOption theirs) =>
        mine.TextKey == theirs.TextKey && mine.Relic?.Id == theirs.Relic?.Id;

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


    private sealed record LinkedPage(SoulLinkSession Session, string Table, OptionDecision Decision)
    {
        public PlayerId Me => Session.Me;
    }
}
