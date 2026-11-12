using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.SoulLinkCode.Rewards;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// The lock and vote indicators on the card reward screen. Options the local player can't take are dimmed, their own
/// vote and the others' votes are tinted, and a line at the top says what each linked player has picked so far.
/// </summary>
internal static class LockView
{
    private static readonly Color Dimmed = new(1f, 1f, 1f, 0.35f);
    private static readonly Color MyVote = new(0.7f, 1f, 0.7f);
    private static readonly Color TheirVote = new(0.75f, 0.85f, 1f);

    private static NCardRewardSelectionScreen? _cardRewardScreen;

    public static void Show(NCardRewardSelectionScreen screen)
    {
        _cardRewardScreen = screen;
        Refresh();
    }

    /// <summary>Re-applies the indicators after the linked state changed.</summary>
    public static void Refresh()
    {
        if (_cardRewardScreen is not { } screen || !GodotObject.IsInstanceValid(screen))
        {
            _cardRewardScreen = null;
            return;
        }

        if (SoulLinkSession.Current is not { Run: not null } session
            || CardRewardLinks.ActiveReward is not { } reward
            || CardRewardLinks.TableId(reward) is not { } table
            || CardRewardLinks.ScreenOf(table) is not { } linked)
        {
            return;
        }

        var decision = linked.Decision;
        var views = decision.ViewOptions(session.Me).ToDictionary(view => view.Option);
        var myVote = decision.VoteOf(session.Me);
        var options = RewardAccess.Options(screen);
        for (var i = 0; i < options.Count; i++)
        {
            if (screen.GetCardHolder(options[i].Card) is not { } holder || !GodotObject.IsInstanceValid(holder))
            {
                continue;
            }

            var key = CardRewardLinks.KeyOfCard(options, i);
            var blocked = views.TryGetValue(key, out var view)
                && view.State is OptionState.Locked or OptionState.GreyedOut or OptionState.Unavailable or OptionState.NotChosen;
            holder.Modulate = blocked ? Dimmed
                : myVote?.Contains(key) == true ? MyVote
                : decision.VotersFor(session.Me, key).Count > 0 ? TheirVote
                : Colors.White;
        }

        StatusLine.Set(
            screen,
            decision.Participants.Count > 1 ? StatusLine.Join(session, linked.Participants, (player, isMe) => Doing(linked, player, isMe)) : "");
    }

    private static string Doing(CardRewardScreen screen, PlayerId player, bool isMe)
    {
        if (screen.Decision.CommitOf(player)?.Choice.Items[0] is { } taken)
        {
            return taken == CardRewardKeys.Skip ? "skipped"
                : taken == CardRewardKeys.Sacrifice ? "sacrificed"
                : $"took {CardRewardLinks.Describe(taken)}";
        }

        if (screen.RerollsOwed(player) > 0)
        {
            return "rerolling";
        }

        return screen.Decision.VoteOf(player)?.Items[0] is { } vote
            ? $"{(isMe ? "want" : "wants")} {CardRewardLinks.Describe(vote)}"
            : "choosing";
    }
}
