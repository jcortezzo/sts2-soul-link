using SoulLink.SoulLinkCode.Events;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>Every linked screen's indicators, refreshed together after the linked state changes.</summary>
internal static class Views
{
    public static void RefreshAll()
    {
        LockView.Refresh();
        EventView.Refresh();
    }
}
