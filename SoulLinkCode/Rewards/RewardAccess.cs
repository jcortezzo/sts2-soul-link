using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;

namespace SoulLink.SoulLinkCode.Rewards;

/// <summary>The game's private reward fields and methods the mod reads or replays, looked up once.</summary>
internal static class RewardAccess
{
    private static readonly AccessTools.FieldRef<CardReward, List<CardCreationResult>> CardsField =
        AccessTools.FieldRefAccess<CardReward, List<CardCreationResult>>("_cards");

    private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, IReadOnlyList<CardCreationResult>> ScreenOptionsField =
        AccessTools.FieldRefAccess<NCardRewardSelectionScreen, IReadOnlyList<CardCreationResult>>("_options");

    private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, IReadOnlyList<CardRewardAlternative>> ScreenAlternativesField =
        AccessTools.FieldRefAccess<NCardRewardSelectionScreen, IReadOnlyList<CardRewardAlternative>>("_extraOptions");

    private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, TaskCompletionSource<int?>?> PickField =
        AccessTools.FieldRefAccess<NCardRewardSelectionScreen, TaskCompletionSource<int?>?>("_completionSource");

    private static readonly AccessTools.FieldRef<CardReward, NCardRewardSelectionScreen?> ShownScreenField =
        AccessTools.FieldRefAccess<CardReward, NCardRewardSelectionScreen?>("_currentlyShownScreen");

    private static readonly AccessTools.FieldRef<NRewardsScreen, RewardsSet> RewardsSetField =
        AccessTools.FieldRefAccess<NRewardsScreen, RewardsSet>("_rewardsSet");

    private static readonly AccessTools.FieldRef<NRewardsScreen, List<Control>> RewardButtonsField =
        AccessTools.FieldRefAccess<NRewardsScreen, List<Control>>("_rewardButtons");

    private static readonly MethodInfo SelectCardMethod =
        AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard", [typeof(NCardHolder)]);

    private static readonly MethodInfo SelectAlternativeMethod =
        AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected", [typeof(int)]);

    private static readonly MethodInfo ProceedMethod =
        AccessTools.Method(typeof(NRewardsScreen), "OnProceedButtonPressed", [typeof(NButton)]);

    private static readonly MethodInfo GetRewardMethod = AccessTools.Method(typeof(NRewardButton), "GetReward");

    public static IReadOnlyList<CardCreationResult> Cards(CardReward reward) => CardsField(reward) ?? [];

    public static IReadOnlyList<CardCreationResult> Options(NCardRewardSelectionScreen screen) => ScreenOptionsField(screen) ?? [];

    public static IReadOnlyList<CardRewardAlternative> Alternatives(NCardRewardSelectionScreen screen) =>
        ScreenAlternativesField(screen) ?? [];

    public static RewardsSet? RewardsSet(NRewardsScreen screen) => RewardsSetField(screen);

    /// <summary>
    /// Whether the screen is waiting for a pick. Before that, picking does nothing; after it, picking again throws.
    /// </summary>
    public static bool IsWaitingForPick(NCardRewardSelectionScreen screen) => PickField(screen) is { Task.IsCompleted: false };

    /// <summary>The card reward's selection screen while it's open, or null.</summary>
    public static NCardRewardSelectionScreen? ShownScreen(CardReward reward) =>
        ShownScreenField(reward) is { } screen && GodotObject.IsInstanceValid(screen) && screen.IsInsideTree() ? screen : null;

    /// <summary>The button for a reward on the rewards screen, or null once it's been claimed.</summary>
    public static NRewardButton? ButtonFor(NRewardsScreen screen, Reward reward) =>
        (RewardButtonsField(screen) ?? [])
            .OfType<NRewardButton>()
            .FirstOrDefault(button => GodotObject.IsInstanceValid(button) && ReferenceEquals(button.Reward, reward));

    /// <summary>Claims a reward the way clicking its button does, which opens a card reward's selection screen.</summary>
    public static void Claim(NRewardButton button) => TaskHelper.RunSafely((Task)GetRewardMethod.Invoke(button, [])!);

    public static void SelectCard(NCardRewardSelectionScreen screen, NCardHolder holder) => SelectCardMethod.Invoke(screen, [holder]);

    public static void SelectAlternative(NCardRewardSelectionScreen screen, int index) => SelectAlternativeMethod.Invoke(screen, [index]);

    public static void Proceed(NRewardsScreen screen, NButton button) => ProceedMethod.Invoke(screen, [button]);
}
