using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SoulLink.Core.Decisions;
using SoulLink.Core.Sync;
using SoulLink.SoulLinkCode.Rewards;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// The lock indicator: dims options the local player can't take right now because someone else chose first or, in
/// mean mode, because another player can't follow.
/// </summary>
internal static class LockView
{
    private static readonly Color Dimmed = new(1f, 1f, 1f, 0.35f);

    private static NCardRewardSelectionScreen? _cardRewardScreen;

    public static void Show(NCardRewardSelectionScreen screen)
    {
        _cardRewardScreen = screen;
        Refresh();
    }

    /// <summary>Re-applies the indicator after the linked state changed.</summary>
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
            || session.State.Find<RewardTable>(table)?.Screen.Decision is not { } decision
            || !decision.IsParticipant(session.Me))
        {
            return;
        }

        var views = decision.ViewOptions(session.Me).ToDictionary(view => view.Option);
        var options = RewardAccess.Options(screen);
        for (var i = 0; i < options.Count; i++)
        {
            if (screen.GetCardHolder(options[i].Card) is not { } holder || !GodotObject.IsInstanceValid(holder))
            {
                continue;
            }

            var blocked = views.TryGetValue(CardRewardLinks.KeyOfCard(options, i), out var view)
                && view.State is OptionState.Locked or OptionState.GreyedOut or OptionState.Unavailable;
            holder.Modulate = blocked ? Dimmed : Colors.White;
        }
    }
}
