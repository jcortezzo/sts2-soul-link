using Godot;
using MegaCrit.Sts2.Core.Helpers;
using SoulLink.Core;
using SoulLink.SoulLinkCode.Linking;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// Character portraits on an option for each player who voted for or chose it, like the game's own vote icons on
/// group events and chests, for screens that don't have them, such as cards and shop slots.
/// </summary>
internal static class VoteIcons
{
    private const string RowName = "SoulLinkVotes";
    private const string IconScene = "ui/multiplayer_vote_icon";
    private const string PlayerMeta = "soul_link_player";

    /// <summary>
    /// How tall the portraits on a card are, as a share of the card's height. The game's portraits are sized for event
    /// buttons, which made them tiny on cards.
    /// </summary>
    public const float OnCards = 0.18f;

    /// <summary>Where the row of portraits sits on the option.</summary>
    public enum Placement
    {
        /// <summary>Centered just above the option's top edge.</summary>
        Above,

        /// <summary>Centered just below the option's bottom edge.</summary>
        Below,

        /// <summary>Just right of the option, centered vertically.</summary>
        Right,
    }

    /// <summary>
    /// Shows a portrait for each player on the control, adding and removing portraits as votes change.
    /// </summary>
    /// <param name="target">The option.</param>
    /// <param name="bounds">The option's visible area in the target's own coordinates.</param>
    /// <param name="players">The players to show, in order.</param>
    /// <param name="placement">Where to put the row.</param>
    /// <param name="relativeHeight">
    /// How tall the portraits are as a share of the option's height, like <see cref="OnCards"/>; 0 keeps the game's
    /// size.
    /// </param>
    public static void Show(Control target, Rect2 bounds, IReadOnlyList<PlayerId> players, Placement placement = Placement.Above, float relativeHeight = 0)
    {
        if (!GodotObject.IsInstanceValid(target))
        {
            return;
        }

        var row = target.GetNodeOrNull<HBoxContainer>(RowName);
        if (players.Count == 0)
        {
            row?.QueueFree();
            return;
        }

        // The row is the option's last child, so it's drawn over the option but, unlike a raised z index, still under
        // hover tips, such as the card preview slot peek shows.
        if (row is null)
        {
            row = new HBoxContainer { Name = RowName, MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", -6);
            target.AddChild(row);
        }
        else if (row.GetIndex() != target.GetChildCount() - 1)
        {
            target.MoveChild(row, -1);
        }

        foreach (var child in row.GetChildren().OfType<Control>().ToList())
        {
            if (PlayerOf(child) is not { } shown || !players.Contains(shown))
            {
                row.RemoveChild(child);
                child.QueueFree();
            }
        }

        foreach (var player in players)
        {
            if (row.GetChildren().OfType<Control>().Any(child => PlayerOf(child) == player) || Icon(player) is not { } icon)
            {
                continue;
            }

            row.AddChild(icon);
            icon.Modulate = new Color(1f, 1f, 1f, 0f);
            icon.CreateTween().TweenProperty(icon, "modulate:a", 1f, 0.2);
        }

        var natural = row.GetCombinedMinimumSize();
        var scale = relativeHeight > 0 && natural.Y > 0 ? bounds.Size.Y * relativeHeight / natural.Y : 1f;
        row.Size = natural;
        row.Scale = new Vector2(scale, scale);
        var size = natural * scale;
        var x = bounds.Position.X + (bounds.Size.X - size.X) / 2;
        row.Position = placement switch
        {
            Placement.Above => new Vector2(x, bounds.Position.Y - size.Y + 6 * scale),
            Placement.Below => new Vector2(x, bounds.End.Y - 6 * scale),
            _ => new Vector2(bounds.End.X + 4 * scale, bounds.Position.Y + (bounds.Size.Y - size.Y) / 2),
        };
    }

    /// <summary>The visible area of a part of an option, like a card holder's hitbox, in the option's own coordinates.</summary>
    public static Rect2 BoundsOf(Control target, Control? area)
    {
        if (area is null || !GodotObject.IsInstanceValid(area) || ReferenceEquals(area, target))
        {
            return new Rect2(Vector2.Zero, target.Size);
        }

        return target.GetGlobalTransform().AffineInverse() * area.GetGlobalRect();
    }

    /// <summary>Removes the portraits from an option.</summary>
    public static void Clear(Control target)
    {
        if (GodotObject.IsInstanceValid(target) && target.GetNodeOrNull<HBoxContainer>(RowName) is { } row)
        {
            row.QueueFree();
        }
    }

    private static PlayerId? PlayerOf(Control icon) =>
        icon.HasMeta(PlayerMeta) ? new PlayerId(icon.GetMeta(PlayerMeta).AsUInt64()) : null;

    private static TextureRect? Icon(PlayerId player)
    {
        if (OptionLinks.PlayerOf(player)?.Character is not { } character)
        {
            return null;
        }

        try
        {
            var icon = SceneHelper.Instantiate<TextureRect>(IconScene);
            icon.Texture = character.IconTexture;
            if (icon.GetNodeOrNull<TextureRect>("Outline") is { } outline)
            {
                outline.Texture = character.IconOutlineTexture;
            }

            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
            icon.SetMeta(PlayerMeta, player.Value);
            return icon;
        }
        catch (Exception ex)
        {
            // The portraits are only an indicator, so a missing scene shouldn't break the screen.
            MainFile.Logger.Warn($"Soul Link couldn't show a vote portrait: {ex.Message}");
            return null;
        }
    }
}
