using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Events;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Shops;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// Slot peek: hovering a linked slot also shows what each teammate has in the same slot, so players know what a pick
/// means for everyone before they click. That's their card on a card reward or card screen, their item in the shop,
/// and their option at an Ancient or any event whose options differ between players.
/// </summary>
[HarmonyPatch]
internal static class SlotPeekPatches
{
    /// <summary>Every hover tip set in the game is made here, with the hovered node as its owner.</summary>
    [HarmonyPatch(
        typeof(NHoverTipSet),
        nameof(NHoverTipSet.CreateAndShow),
        new[] { typeof(Control), typeof(IEnumerable<IHoverTip>), typeof(HoverTipAlignment) })]
    [HarmonyPrefix]
    private static void AddTeammates(Control owner, ref IEnumerable<IHoverTip> hoverTips)
    {
        if (OptionLinks.Session is null || owner is null)
        {
            return;
        }

        IReadOnlyList<IHoverTip> extra = [];
        Guard.Run("show teammates' slots", () => extra = TipsFor(owner));
        if (extra.Count > 0)
        {
            hoverTips = (hoverTips ?? []).Concat(extra).ToList();
        }
    }

    private static IReadOnlyList<IHoverTip> TipsFor(Control owner) => owner switch
    {
        NCardHolder holder => LockView.Peek(holder) ?? DeckChoices.Peek(holder) ?? [],
        NEventOptionButton button => EventView.Peek(button),
        NMerchantSlot slot => ShopLinks.Peek(slot),
        _ => [],
    };
}
