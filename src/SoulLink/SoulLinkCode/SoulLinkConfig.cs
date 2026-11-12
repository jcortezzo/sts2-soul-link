using BaseLib.Config;
using SoulLink.Core;

namespace SoulLink.SoulLinkCode;

/// <summary>
/// The Soul Link rules in BaseLib's mod config menu. Only the host's values matter: the host sends them to everyone
/// when a run starts.
/// </summary>
internal sealed class SoulLinkConfig : SimpleModConfig
{
    /// <summary>Which of your cards counts as "the same card" for upgrades, removals and other deck choices.</summary>
    [ConfigSection("Rules")]
    public static CardMatching CardMatching { get; set; } = SoulLinkSettings.Default.CardMatching;

    /// <summary>Nice: whoever can follow a choice must. Mean: options are greyed out unless everyone can take them.</summary>
    public static LinkMode Mode { get; set; } = SoulLinkSettings.Default.Mode;

    /// <summary>Whether any player's death ends the run for everyone.</summary>
    public static bool SharedDeath { get; set; } = SoulLinkSettings.Default.SharedDeath;

    /// <summary>
    /// Everyone agrees: a choice waits until everyone who can make it has clicked it. First click: the first click
    /// makes it for everyone.
    /// </summary>
    public static WhoDecides WhoDecides { get; set; } = SoulLinkSettings.Default.WhoDecides;

    public static SoulLinkSettings ToSettings() => new(CardMatching, Mode, SharedDeath, WhoDecides);
}
