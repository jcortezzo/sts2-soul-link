using BaseLib.Config;
using SoulLink.Core;

namespace SoulLink.SoulLinkCode;

/// <summary>
/// The Soul Link rules in BaseLib's mod config menu, with names and hover text from SoulLink/localization. Only the
/// host's values matter: the host sends them to everyone when a run starts.
/// </summary>
[ConfigHoverTipsByDefault]
internal sealed class SoulLinkConfig : SimpleModConfig
{
    /// <summary>
    /// Which of your cards counts as "the same card" for upgrades, removals and other deck choices. Saved under a new
    /// name since deck index became the default: the old setting's saved value was linked cards for everyone who ran
    /// an earlier version, and BaseLib's saved values win over defaults, so it would never have switched over.
    /// </summary>
    [ConfigSection("Rules")]
    public static CardMatching DeckCardMatching { get; set; } = SoulLinkSettings.Default.CardMatching;

    /// <summary>Nice: whoever can follow a choice must. Mean: options are greyed out unless everyone can take them.</summary>
    public static LinkMode Mode { get; set; } = SoulLinkSettings.Default.Mode;

    /// <summary>Whether any player's death ends the run for everyone.</summary>
    public static bool SharedDeath { get; set; } = SoulLinkSettings.Default.SharedDeath;

    /// <summary>
    /// Everyone agrees: a choice waits until everyone who can make it has clicked it. First click: the first click
    /// makes it for everyone.
    /// </summary>
    public static WhoDecides WhoDecides { get; set; } = SoulLinkSettings.Default.WhoDecides;

    public static SoulLinkSettings ToSettings() => new(DeckCardMatching, Mode, SharedDeath, WhoDecides);
}
