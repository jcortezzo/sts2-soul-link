namespace SoulLink.SoulLinkCode.Ui;

/// <summary>
/// Runs the mod's part of a game method, so a bug in the mod logs an error instead of breaking the game. A gate that
/// fails lets the game act as it would without the mod.
/// </summary>
internal static class Guard
{
    public static void Run(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link failed to {what}: {ex}");
        }
    }

    /// <summary>Runs a prefix gate. True lets the game act; that's also the answer when the gate fails.</summary>
    public static bool Gate(string what, Func<bool> gate)
    {
        try
        {
            return gate();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link failed to {what}, so the game handles it as usual: {ex}");
            return true;
        }
    }

    /// <summary>Asks whether to show an indicator. False when the check fails.</summary>
    public static bool Show(string what, Func<bool> show)
    {
        try
        {
            return show();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link failed to {what}: {ex}");
            return false;
        }
    }
}
