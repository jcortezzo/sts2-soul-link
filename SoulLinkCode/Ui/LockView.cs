using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Rewards;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// The lock and vote indicators on the card reward screen. Each card shows the portraits of the players who voted for
/// or took it, cards the local player can't take are dimmed, and hovering a card shows each teammate's card in the
/// same slot.
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
    }

    /// <summary>
    /// Slot peek: each teammate's card in the hovered card's slot. Null when the holder isn't on the card reward
    /// screen.
    /// </summary>
    public static IReadOnlyList<IHoverTip>? Peek(NCardHolder holder)
    {
        if (_cardRewardScreen is not { } screen || !GodotObject.IsInstanceValid(screen) || !screen.IsAncestorOf(holder))
        {
            return null;
        }

        if (OptionLinks.Session is not { } session
            || CardRewardLinks.ActiveReward is not { } reward
            || CardRewardLinks.TableId(reward) is not { } table)
        {
            return [];
        }

        var options = RewardAccess.Options(screen);
        for (var i = 0; i < options.Count; i++)
        {
            if (ReferenceEquals(screen.GetCardHolder(options[i].Card), holder))
            {
                return PeekTips.SharedCards(session, table, CardRewardLinks.KeyOfCard(options, i), options[i].Card);
            }
        }

        return [];
    }
}
