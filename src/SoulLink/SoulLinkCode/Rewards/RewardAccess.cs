using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
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

    private static readonly AccessTools.FieldRef<NRewardsScreen, RewardsSet> RewardsSetField =
        AccessTools.FieldRefAccess<NRewardsScreen, RewardsSet>("_rewardsSet");

    private static readonly MethodInfo SelectCardMethod =
        AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard", [typeof(NCardHolder)]);

    private static readonly MethodInfo SelectAlternativeMethod =
        AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected", [typeof(int)]);

    private static readonly MethodInfo ProceedMethod =
        AccessTools.Method(typeof(NRewardsScreen), "OnProceedButtonPressed", [typeof(NButton)]);

    public static IReadOnlyList<CardCreationResult> Cards(CardReward reward) => CardsField(reward) ?? [];

    public static IReadOnlyList<CardCreationResult> Options(NCardRewardSelectionScreen screen) => ScreenOptionsField(screen) ?? [];

    public static IReadOnlyList<CardRewardAlternative> Alternatives(NCardRewardSelectionScreen screen) =>
        ScreenAlternativesField(screen) ?? [];

    public static RewardsSet? RewardsSet(NRewardsScreen screen) => RewardsSetField(screen);

    public static void SelectCard(NCardRewardSelectionScreen screen, NCardHolder holder) => SelectCardMethod.Invoke(screen, [holder]);

    public static void SelectAlternative(NCardRewardSelectionScreen screen, int index) => SelectAlternativeMethod.Invoke(screen, [index]);

    public static void Proceed(NRewardsScreen screen, NButton button) => ProceedMethod.Invoke(screen, [button]);
}
