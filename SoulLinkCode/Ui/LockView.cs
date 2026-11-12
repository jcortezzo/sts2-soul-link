using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Rewards;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// The lock and vote indicators on the card reward screen. Each card shows the portraits of the players who voted for
/// or took it, cards the local player can't take are dimmed, and a line at the top says what each linked player has
/// picked so far.
/// </summary>
internal static class LockView
{
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
            Dim.Set(holder, blocked);
            VoteIcons.Show(holder, VoteIcons.BoundsOf(holder, holder.Hitbox), OptionLinks.Backers(decision, key));
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
