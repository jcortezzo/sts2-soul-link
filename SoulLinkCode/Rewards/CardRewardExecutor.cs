using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using SoulLink.Core.Decisions;
using SoulLink.Core.Rewards;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Rewards;

/// <summary>
/// Does the local player's linked card reward picks in the game once the host has committed them, whoever's click
/// caused them: the player's own first click, a first click that picked for them, or a pick everyone agreed on. It
/// clicks the card or button for them, opening the card reward first if it isn't open.
/// </summary>
internal static class CardRewardExecutor
{
    /// <summary>How long to wait for a card reward the mod opened to show, in case the game never shows it.</summary>
    private const ulong OpenLimitMsec = 5000;

    /// <summary>How long to wait before checking again when a screen isn't ready yet, in seconds.</summary>
    private const double RetryDelaySec = 0.25;

    /// <summary>How many times to try a pick that can't be done yet before giving it back to the player.</summary>
    private const int MaxAttempts = 20;

    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> Attempts = new(StringComparer.Ordinal);
    private static NRewardsScreen? _rewardsScreen;
    private static (NRewardsScreen Screen, NButton Button)? _pendingProceed;
    private static string? _opening;
    private static ulong _openingSince;
    private static bool _scheduled;
    private static bool _retrying;

    /// <summary>The local player's rewards screen, while it's open.</summary>
    public static NRewardsScreen? RewardsScreen => _rewardsScreen is { } screen && GodotObject.IsInstanceValid(screen) ? screen : null;

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset()
    {
        Done.Clear();
        Attempts.Clear();
        _rewardsScreen = null;
        _pendingProceed = null;
        _opening = null;
    }

    public static void TrackRewardsScreen(NRewardsScreen screen)
    {
        _rewardsScreen = screen;
        _pendingProceed = null;
        Schedule();
    }

    /// <summary>Leaves the rewards screen for the player once every card reward on it is done.</summary>
    public static void ProceedWhenDone(NRewardsScreen screen, NButton button)
    {
        _pendingProceed = (screen, button);
        Schedule();
    }

    /// <summary>Forgets a pending proceed, for example because the host refused the player's skips.</summary>
    public static void CancelProceed() => _pendingProceed = null;

    /// <summary>The player opened a card reward themselves, so they no longer want to leave right away.</summary>
    public static void OnOpened(CardReward reward)
    {
        if (CardRewardLinks.TableId(reward) is not { } table || table != _opening)
        {
            _pendingProceed = null;
        }
    }

    /// <summary>
    /// Whether the player can leave the rewards screen: every card and relic reward in the set was taken or is decided
    /// as a skip. Rewards that aren't linked don't hold the player up.
    /// </summary>
    public static bool IsDone(RewardsSet set)
    {
        if (SoulLinkSession.Current is not { } session)
        {
            return true;
        }

        if (!RelicRewardLinks.IsDone(set))
        {
            return false;
        }

        foreach (var reward in set.Rewards.OfType<CardReward>())
        {
            if (reward.SuccessfullySelected || CardRewardLinks.TableId(reward) is not { } table || CardRewardLinks.ScreenOf(table) is not { } screen)
            {
                continue;
            }

            if (screen.Decision.CommitOf(session.Me)?.Choice is not { } choice || !choice.Contains(CardRewardKeys.Skip))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The key the host committed for the local player on a table, so a click on it can go straight to the game.
    /// That way a pick the mod couldn't do, say because the screen wasn't ready, is still the player's to make.
    /// </summary>
    public static OptionKey? CommittedKey(string table) =>
        SoulLinkSession.Current is { } session && CardRewardLinks.ScreenOf(table)?.Decision.CommitOf(session.Me) is { } commit
            ? commit.Choice.Items[0]
            : null;

    /// <summary>
    /// Lets a click go straight to the game when it's the local player's committed pick and the screen is still
    /// waiting for it, so the game takes it exactly once, and then doesn't pick it again.
    /// </summary>
    /// <returns>True when the game should act on the click.</returns>
    public static bool TakeCommittedClick(NCardRewardSelectionScreen screen, string table, OptionKey key)
    {
        if (CommittedKey(table) != key || !RewardAccess.IsWaitingForPick(screen))
        {
            return false;
        }

        Done.Add(table);
        return true;
    }

    /// <summary>Checks for picks to do after the linked state or the screens changed. Runs once per frame at most.</summary>
    public static void Schedule()
    {
        if (_scheduled)
        {
            return;
        }

        _scheduled = true;
        Callable.From(Run).CallDeferred();
    }

    private static void Run()
    {
        _scheduled = false;
        try
        {
            Pump();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't do a linked card reward pick: {ex}");
        }
    }

    /// <summary>Checks again after a short delay, when a screen isn't ready to be clicked yet.</summary>
    private static void Retry()
    {
        if (_retrying || Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        _retrying = true;
        tree.CreateTimer(RetryDelaySec).Timeout += () =>
        {
            _retrying = false;
            Schedule();
        };
    }

    private static void Pump()
    {
        if (SoulLinkSession.Current is not { Run: not null } session || CardRewardLinks.CurrentSet is not { } set)
        {
            _pendingProceed = null;
            return;
        }

        if (_opening is not null && Time.GetTicksMsec() - _openingSince > OpenLimitMsec)
        {
            MainFile.Logger.Warn($"Soul Link gave up waiting for {_opening} to open.");
            _opening = null;
        }

        var rewards = set.Rewards.OfType<CardReward>().ToList();
        var openReward = rewards.FirstOrDefault(reward => RewardAccess.ShownScreen(reward) is not null);
        foreach (var reward in rewards)
        {
            if (reward.SuccessfullySelected
                || CardRewardLinks.TableId(reward) is not { } table
                || Done.Contains(table)
                || CardRewardLinks.ScreenOf(table)?.Decision.CommitOf(session.Me) is not { } commit)
            {
                continue;
            }

            var key = commit.Choice.Items[0];
            if (RewardAccess.ShownScreen(reward) is { } screen)
            {
                if (_opening == table)
                {
                    _opening = null;
                }

                if (TryClick(screen, key))
                {
                    Done.Add(table);
                    MainFile.Logger.Info($"Soul Link picked {key} on {table}.");
                }
                else
                {
                    Attempt(table, $"couldn't find {key} on the screen");
                }

                Retry();
                return;
            }

            // A skipped card reward needs nothing until it's opened: leaving the rewards screen skips it.
            if (key != CardRewardKeys.Skip && openReward is null && _opening is null)
            {
                Open(reward, table);
                return;
            }
        }

        RelicRewardLinks.Pump(RewardsScreen, set);
        if (_pendingProceed is { } proceed && openReward is null && _opening is null && IsDone(set))
        {
            _pendingProceed = null;
            if (GodotObject.IsInstanceValid(proceed.Screen) && GodotObject.IsInstanceValid(proceed.Button))
            {
                ClickGate.RunApproved(proceed.Screen, () => RewardAccess.Proceed(proceed.Screen, proceed.Button));
            }
        }
    }

    /// <summary>Clicks the card or button for a key on an open screen, or returns false if the screen isn't ready.</summary>
    private static bool TryClick(NCardRewardSelectionScreen screen, OptionKey key)
    {
        if (!RewardAccess.IsWaitingForPick(screen))
        {
            return false;
        }

        var options = RewardAccess.Options(screen);
        for (var i = 0; i < options.Count; i++)
        {
            if (CardRewardLinks.KeyOfCard(options, i) == key)
            {
                if (screen.GetCardHolder(options[i].Card) is not { } holder || !GodotObject.IsInstanceValid(holder))
                {
                    return false;
                }

                ClickGate.RunApproved(screen, () => RewardAccess.SelectCard(screen, holder));
                return true;
            }
        }

        var alternatives = RewardAccess.Alternatives(screen);
        var index = -1;
        for (var i = 0; i < alternatives.Count; i++)
        {
            if (CardRewardLinks.KeyOfAlternative(alternatives[i]) == key
                && (index < 0 || string.Equals(alternatives[i].OptionId, "Skip", StringComparison.OrdinalIgnoreCase)))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            return false;
        }

        ClickGate.RunApproved(screen, () => RewardAccess.SelectAlternative(screen, index));
        return true;
    }

    /// <summary>Opens a card reward from the rewards screen, as clicking it would.</summary>
    private static void Open(CardReward reward, string table)
    {
        if (_rewardsScreen is not { } rewardsScreen
            || !GodotObject.IsInstanceValid(rewardsScreen)
            || RewardAccess.ButtonFor(rewardsScreen, reward) is not { } button)
        {
            Attempt(table, "couldn't find it on the rewards screen");
            Retry();
            return;
        }

        _opening = table;
        _openingSince = Time.GetTicksMsec();
        RewardAccess.Claim(button);
    }

    /// <summary>Counts a failed try, and gives the pick back to the player after too many.</summary>
    private static void Attempt(string table, string problem)
    {
        var attempts = Attempts[table] = Attempts.GetValueOrDefault(table) + 1;
        if (attempts >= MaxAttempts)
        {
            Done.Add(table);
            MainFile.Logger.Warn($"Soul Link {problem} for {table}, so the pick is left to you.");
        }
    }
}
