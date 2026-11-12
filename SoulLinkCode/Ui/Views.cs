using SoulLink.SoulLinkCode.Campfires;
using SoulLink.SoulLinkCode.Chests;
using SoulLink.SoulLinkCode.Events;
using SoulLink.SoulLinkCode.Rewards;
using SoulLink.SoulLinkCode.Shops;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>Every linked screen's indicators, refreshed together after the linked state changes.</summary>
internal static class Views
{
    public static void RefreshAll()
    {
        Refresh("card reward", LockView.Refresh);
        Refresh("relic reward", () => RelicRewardLinks.Refresh(CardRewardExecutor.RewardsScreen, CardRewardLinks.CurrentSet));
        Refresh("event", EventView.Refresh);
        Refresh("chest", ChestLinks.Refresh);
        Refresh("campfire", CampfireLinks.Refresh);
        Refresh("shop", ShopLinks.Refresh);
    }

    /// <summary>
    /// Runs one screen's refresh, so a problem showing one screen's indicators can't break the others, the sync or the
    /// game code that triggered it.
    /// </summary>
    public static void Refresh(string screen, Action refresh)
    {
        try
        {
            refresh();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't show the {screen} indicators: {ex}");
        }
    }
}
