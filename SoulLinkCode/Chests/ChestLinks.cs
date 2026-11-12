using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.Core;
using SoulLink.Core.Decisions;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Chests;

/// <summary>
/// Links the relic pick in a co-op treasure chest: everyone picks the same relic, or everyone skips. The game then
/// does its usual rock-paper-scissors among everyone who picked it, and the others get the relics that are left.
/// A click is a vote or a first-click pick; the mod makes the game pick once the host commits it.
/// </summary>
internal static class ChestLinks
{
    private static readonly AccessTools.FieldRef<NTreasureRoom, NProceedButton> ProceedButtonField =
        AccessTools.FieldRefAccess<NTreasureRoom, NProceedButton>("_proceedButton");

    private static readonly AccessTools.FieldRef<NTreasureRoom, NTreasureRoomRelicCollection?> CollectionField =
        AccessTools.FieldRefAccess<NTreasureRoom, NTreasureRoomRelicCollection?>("_relicCollection");

    private static readonly AccessTools.FieldRef<NTreasureRoomRelicCollection, List<NTreasureRoomRelicHolder>?> HoldersField =
        AccessTools.FieldRefAccess<NTreasureRoomRelicCollection, List<NTreasureRoomRelicHolder>?>("_holdersInUse");

    private static readonly Action<NTreasureRoom> RefreshSkipVotes =
        AccessTools.MethodDelegate<Action<NTreasureRoom>>(AccessTools.Method(typeof(NTreasureRoom), "RefreshVotes"));

    private static readonly Action<NTreasureRoom, NButton> PressProceed =
        AccessTools.MethodDelegate<Action<NTreasureRoom, NButton>>(AccessTools.Method(typeof(NTreasureRoom), "OnProceedButtonPressed"));

    private static readonly HashSet<string> Done = new(StringComparer.Ordinal);
    private static bool _scheduled;

    public static TreasureRoomRelicSynchronizer? Synchronizer => RunManager.Instance?.TreasureRoomRelicSynchronizer;

    /// <summary>Forgets everything from the last run.</summary>
    public static void Reset() => Done.Clear();

    /// <summary>The chest's table while its relics are up for picking, or null.</summary>
    public static string? TableId() =>
        Synchronizer?.CurrentRelics is { Count: > 0 } && OptionLinks.Room is { } room ? $"{room}/chest" : null;

    /// <summary>Joins the chest's table once its relics are shown: one slot per relic, plus skipping.</summary>
    public static void JoinCurrent() => OptionLinks.Submit(JoinOps());

    /// <summary>
    /// The chest's join: once when its relics are shown, and again with a click (<paramref name="again"/>) until the
    /// host's acceptance arrives.
    /// </summary>
    private static IReadOnlyList<GatedOp> JoinOps(bool again = false)
    {
        if (OptionLinks.Session is null || TableId() is not { } table || Synchronizer?.CurrentRelics is not { } relics)
        {
            return [];
        }

        var options = Enumerable.Range(1, relics.Count).Select(slot => new DecisionOption(OptionKey.Slot(slot))).Append(new DecisionOption(OptionKey.Skip));
        return OptionLinks.JoinOps(table, options.ToList(), again: again);
    }

    /// <summary>
    /// Handles a click on a relic while the chest is linked: sends it to the host as a vote or a first-click pick.
    /// Once the local player's pick is decided, further clicks do nothing, so the game keeps the linked pick.
    /// </summary>
    /// <returns>True when the chest isn't linked and the game should act on the click as it is.</returns>
    public static bool ClickRelic(int index)
    {
        if (OptionLinks.Session is null || TableId() is not { } table)
        {
            return true;
        }

        if (OptionLinks.CommitOf(table) is null)
        {
            ClickGate.Request([.. JoinOps(again: true), OptionLinks.Choose(table, OptionKey.Slot(index + 1))]);
        }

        return false;
    }

    /// <summary>
    /// Handles the skip button while the chest is linked. Skipping is a vote or a first-click pick like a relic; once
    /// the local player's skip is decided the button works as usual, and after picking a relic it does nothing.
    /// </summary>
    /// <returns>True when the game should act on the press as it is.</returns>
    public static bool ClickSkip(NTreasureRoom room)
    {
        if (OptionLinks.Session is null || TableId() is not { } table || ProceedButtonField(room) is not { IsSkip: true })
        {
            return true;
        }

        if (OptionLinks.CommitOf(table) is { } commit)
        {
            return commit.Choice.Contains(OptionKey.Skip);
        }

        ClickGate.Request([.. JoinOps(again: true), OptionLinks.Choose(table, OptionKey.Skip)]);
        return false;
    }

    /// <summary>Checks for a committed pick to make after the linked state changed. Once per frame at most.</summary>
    public static void Schedule()
    {
        if (_scheduled)
        {
            return;
        }

        _scheduled = true;
        Callable.From(Run).CallDeferred();
    }

    /// <summary>Whether to show a player's portrait on a relic or on skip: they voted for it or chose it.</summary>
    public static bool Backs(ulong playerNetId, OptionKey key) =>
        TableId() is { } table
        && OptionLinks.DecisionOf(table) is { } decision
        && OptionLinks.Backers(decision, key).Contains(new PlayerId(playerNetId));

    /// <summary>Re-applies the vote portraits.</summary>
    public static void Refresh()
    {
        if (NRun.Instance?.TreasureRoom is not { } room || !GodotObject.IsInstanceValid(room))
        {
            return;
        }

        var decision = TableId() is { } table ? OptionLinks.DecisionOf(table) : null;
        if (decision is not null)
        {
            if (CollectionField(room) is { } collection && GodotObject.IsInstanceValid(collection))
            {
                foreach (var holder in HoldersField(collection) ?? [])
                {
                    if (GodotObject.IsInstanceValid(holder) && holder.VoteContainer is { } votes && GodotObject.IsInstanceValid(votes))
                    {
                        votes.RefreshPlayerVotes();
                    }
                }
            }

            RefreshSkipVotes(room);
        }
    }

    private static void Run()
    {
        _scheduled = false;
        try
        {
            JoinCurrent();
            Pump();
            Refresh();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't do a linked chest pick: {ex}");
        }
    }

    private static void Pump()
    {
        if (TableId() is not { } table || Done.Contains(table) || OptionLinks.CommitOf(table) is not { } commit
            || Synchronizer is not { } synchronizer)
        {
            return;
        }

        Done.Add(table);
        var key = commit.Choice.Items[0];
        MainFile.Logger.Info($"Soul Link picked {key} in the chest.");
        if (key == OptionKey.Skip)
        {
            // Skipping is the skip button, which also leaves the chest, as it does when you press it yourself.
            if (NRun.Instance?.TreasureRoom is { } room && GodotObject.IsInstanceValid(room) && ProceedButtonField(room) is { IsSkip: true } button)
            {
                ClickGate.RunApproved(room, () => PressProceed(room, button));
            }
            else
            {
                synchronizer.SkipRelicLocally();
            }

            return;
        }

        if (key.SlotNumber is { } slot && slot <= (synchronizer.CurrentRelics?.Count ?? 0))
        {
            synchronizer.PickRelicLocally(slot - 1);
        }
    }
}
