using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Events;

/// <summary>
/// Sends the local player's event clicks to the host and does whatever the host commits for them, whoever's click
/// caused it: their own first click, a first click that picked for them, or an option everyone agreed on. A click
/// waits while a teammate's page hasn't opened yet, so it can't decide for them before they've seen it.
/// </summary>
internal static class EventExecutor
{
    /// <summary>How long a click waits for teammates before it's sent anyway, in case one of them never arrives.</summary>
    private const ulong HoldLimitMsec = 30000;

    /// <summary>How often a waiting click checks again, in seconds.</summary>
    private const double HoldCheckSec = 1;

    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);

    /// <summary>Pages whose committed option the mod couldn't take, so the game takes the player's clicks as they are.</summary>
    private static readonly HashSet<string> GivenBack = new(StringComparer.Ordinal);

    private static HeldClick? _held;
    private static bool _scheduled;
    private static bool _checkingHeld;

    /// <summary>A click waiting for teammates to reach the page.</summary>
    public static HeldClick? Held => _held;

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset()
    {
        Done.Clear();
        GivenBack.Clear();
        _held = null;
    }

    /// <summary>Whether the mod gave the page back to the player because it couldn't take their committed option.</summary>
    public static bool IsGivenBack(string table) => GivenBack.Contains(table);

    /// <summary>The local event room, when it uses the standard option layout the mod can drive.</summary>
    public static NEventRoom? Room() =>
        NEventRoom.Instance is { } room && GodotObject.IsInstanceValid(room) && room.IsInsideTree() && room.Layout is not null ? room : null;

    /// <summary>
    /// Lets a click go straight to the game when it's the local player's committed option, so an option the mod
    /// couldn't take for them is still theirs to take, exactly once.
    /// </summary>
    public static bool TakeCommittedClick(string table, OptionKey key)
    {
        if (Done.Contains(table) || SoulLinkSession.Current is not { } session || EventLinks.DecisionOf(table)?.CommitOf(session.Me) is not { } commit
            || !commit.Choice.Contains(key))
        {
            return false;
        }

        Done.Add(table);
        MarkSource(table, commit);
        return true;
    }

    /// <summary>
    /// Marks the option the local player takes as the source of what follows from it: cards it gives link with the
    /// cards the others got from it, and a card screen it opens links with theirs.
    /// </summary>
    private static void MarkSource(string table, Commit<OptionKey> commit)
    {
        var key = commit.Choice.Items[0];
        CardLinksSync.GainingFrom(table, key.Value, commit.Root);
        DeckChoices.Acting(table, key.Value, () =>
            EventLinks.DecisionOf(table) is { } decision
                ? decision.Participants.Where(player => decision.CommitOf(player)?.Choice.Contains(key) == true).ToList()
                : []);
    }

    /// <summary>Handles the local player's click on an option of a linked page. The game never acts on it directly.</summary>
    public static void Click(EventModel ev, string table, OptionKey key)
    {
        if (_held is { } held && held.Table == table && held.Key == key)
        {
            // Clicking a waiting option again takes it back, as with a vote.
            _held = null;
            EventView.Refresh();
            return;
        }

        if (EventLinks.Awaited(ev) is { Count: > 0 } awaited)
        {
            _held = new HeldClick(table, key, awaited, Time.GetTicksMsec());
            CheckHeldLater();
            EventView.Refresh();
            return;
        }

        _held = null;
        ClickGate.Request([.. EventLinks.JoinOps(ev), EventLinks.Choose(table, key)]);
    }

    /// <summary>Checks for clicks to send and options to take after the linked state or the event changed. Once per frame at most.</summary>
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
            EventView.Refresh();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't do a linked event choice: {ex}");
        }
    }

    /// <summary>Checks a waiting click again shortly, so it's sent once its time limit passes even if nothing else changes.</summary>
    private static void CheckHeldLater()
    {
        if (_checkingHeld || Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        _checkingHeld = true;
        tree.CreateTimer(HoldCheckSec).Timeout += () =>
        {
            _checkingHeld = false;
            if (_held is not null)
            {
                Schedule();
            }
        };
    }

    private static void Pump()
    {
        if (SoulLinkSession.Current is not { Run: not null } session
            || Room() is not { } room
            || EventLinks.LocalEvent() is not { } ev
            || !EventLinks.IsLinked(ev)
            || EventLinks.TableId(ev) is not { } table)
        {
            _held = null;
            return;
        }

        // Joins pages shown before the host's settings arrived, like Neow's at the start of a run.
        EventLinks.JoinCurrent();

        if (_held is { } held)
        {
            var awaited = EventLinks.Awaited(ev);
            if (held.Table != table)
            {
                _held = null;
            }
            else if (awaited.Count > 0 && Time.GetTicksMsec() - held.Since < HoldLimitMsec)
            {
                _held = held with { Awaiting = awaited };
                CheckHeldLater();
            }
            else
            {
                if (awaited.Count > 0)
                {
                    MainFile.Logger.Warn($"Soul Link stopped waiting for teammates to reach {table}.");
                }

                _held = null;
                ClickGate.Request([.. EventLinks.JoinOps(ev), EventLinks.Choose(table, held.Key)]);
            }
        }

        if (Done.Contains(table) || EventLinks.DecisionOf(table)?.CommitOf(session.Me) is not { } commit)
        {
            return;
        }

        Done.Add(table);
        var index = (commit.Choice.Items[0].SlotNumber ?? 0) - 1;
        if (index < 0 || index >= ev.CurrentOptions.Count || !EventLinks.IsAvailable(ev, ev.CurrentOptions[index]))
        {
            GivenBack.Add(table);
            MainFile.Logger.Warn($"Soul Link can't take {commit.Choice} on {table} for you, so the choice is left to you.");
            return;
        }

        var option = ev.CurrentOptions[index];
        MainFile.Logger.Info($"Soul Link chose {commit.Choice} on {table}.");
        MarkSource(table, commit);
        ClickGate.RunApproved(room, () => room.OptionButtonClicked(option, index));
    }
}

/// <summary>A click on an event option that waits for teammates to reach the page.</summary>
/// <param name="Table">The page's table.</param>
/// <param name="Key">The option clicked.</param>
/// <param name="Awaiting">The teammates it waits for.</param>
/// <param name="Since">When it was clicked, in engine ticks.</param>
internal sealed record HeldClick(string Table, OptionKey Key, IReadOnlyList<PlayerId> Awaiting, ulong Since);
