using Godot;
using SoulLink.Core;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// The line at the top of a linked screen that says what each linked player is doing, like
/// "You: want card 2     Ben: took card 2".
/// </summary>
internal static class StatusLine
{
    private const string NodeName = "SoulLinkStatus";

    /// <summary>Shows the text on the screen, adding the line the first time. Empty text hides it.</summary>
    public static void Set(Control screen, string text)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        var label = screen.GetNodeOrNull<Label>(NodeName);
        if (label is null)
        {
            if (text.Length == 0)
            {
                return;
            }

            label = Create(screen);
        }

        label.Text = text;
    }

    /// <summary>Joins each player's part in order, with the local player as "You".</summary>
    public static string Join(SoulLinkSession session, IEnumerable<PlayerId> players, Func<PlayerId, bool, string> doing)
    {
        var parts = new List<string>();
        foreach (var player in players)
        {
            var isMe = player == session.Me;
            parts.Add($"{(isMe ? "You" : session.NameOf(player))}: {doing(player, isMe)}");
        }

        return string.Join("     ", parts);
    }

    private static Label Create(Control screen)
    {
        var label = new Label
        {
            Name = NodeName,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 10,
        };
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        screen.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        label.OffsetTop = 96;
        label.OffsetBottom = 140;
        return label;
    }
}
