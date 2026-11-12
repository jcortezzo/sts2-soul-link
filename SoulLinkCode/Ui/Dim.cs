using Godot;

namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// Dims options the local player can't take. Only options the mod dimmed are restored, so the game's own fades and
/// entrance animations on other options are left alone.
/// </summary>
internal static class Dim
{
    private const string DimmedMeta = "soul_link_dimmed";

    private static readonly Color Dimmed = new(1f, 1f, 1f, 0.35f);

    public static void Set(Control option, bool dimmed)
    {
        if (!GodotObject.IsInstanceValid(option))
        {
            return;
        }

        if (dimmed)
        {
            option.Modulate = Dimmed;
            option.SetMeta(DimmedMeta, true);
        }
        else if (option.HasMeta(DimmedMeta))
        {
            option.Modulate = Colors.White;
            option.RemoveMeta(DimmedMeta);
        }
    }
}
