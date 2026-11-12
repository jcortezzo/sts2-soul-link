using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Cards;

/// <summary>Numbers deck cards as they come and go, and keeps transformed cards in their link groups.</summary>
[HarmonyPatch]
internal static class CardIdPatches
{
    [HarmonyPatch(typeof(CardPile), nameof(CardPile.AddInternal))]
    [HarmonyPostfix]
    private static void Added(CardPile __instance, CardModel card) => Guard(() => CardIds.OnAdded(__instance, card));

    [HarmonyPatch(typeof(CardPile), nameof(CardPile.RemoveInternal))]
    [HarmonyPostfix]
    private static void Removed(CardPile __instance, CardModel card) => Guard(() => CardIds.OnRemoved(__instance, card));

    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Transform), [typeof(CardModel), typeof(CardModel), typeof(MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle)])]
    [HarmonyPrefix]
    private static void Transforming(CardModel original, CardModel replacement) => Guard(() => CardLinksSync.Transforming(original, replacement));

    [HarmonyPatch(typeof(CardTransformation), nameof(CardTransformation.GetReplacement))]
    [HarmonyPostfix]
    private static void ReplacementChosen(ref CardTransformation __instance, CardModel __result)
    {
        var original = __instance.Original;
        Guard(() => CardLinksSync.Transforming(original, __result));
    }

    /// <summary>Deck bookkeeping must never break the game's own card handling.</summary>
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't track a deck change: {ex}");
        }
    }
}

/// <summary>
/// Links card screens that follow a linked choice: notes when the game starts waiting for one, and takes the pick
/// out of the screen as it would finish, keeping the screen open until the host commits the local player's cards.
/// </summary>
[HarmonyPatch]
internal static class CardScreenPatches
{
    [HarmonyPatch(typeof(NCardGridSelectionScreen), nameof(NCardGridSelectionScreen.CardsSelected))]
    [HarmonyPostfix]
    private static void Opened(NCardGridSelectionScreen __instance) => Views.Refresh("card choice", () => DeckChoices.Opened(__instance));

    [HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "CheckIfSelectionComplete")]
    [HarmonyPatch(typeof(NDeckCardSelectScreen), "CheckIfSelectionComplete")]
    [HarmonyPatch(typeof(NDeckEnchantSelectScreen), "CheckIfSelectionComplete")]
    [HarmonyPatch(typeof(NSimpleCardSelectScreen), "CompleteSelection")]
    [HarmonyPatch(typeof(NDeckTransformSelectScreen), "CompleteSelection")]
    [HarmonyPrefix]
    private static void BeforeFinish(NCardGridSelectionScreen __instance, out TaskCompletionSource<IEnumerable<CardModel>>? __state)
    {
        __state = null;
        if (!Guard.Show("check a card screen", () => DeckChoices.IsLinked(__instance)))
        {
            return;
        }

        // The screen finishes into a stand-in, so the pick can be read without the game acting on it.
        __state = CardScreenAccess.Completion(__instance);
        CardScreenAccess.Completion(__instance) = new TaskCompletionSource<IEnumerable<CardModel>>();
        DeckChoices.KeepOpen = __instance;
    }

    [HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "CheckIfSelectionComplete")]
    [HarmonyPatch(typeof(NDeckCardSelectScreen), "CheckIfSelectionComplete")]
    [HarmonyPatch(typeof(NDeckEnchantSelectScreen), "CheckIfSelectionComplete")]
    [HarmonyPatch(typeof(NSimpleCardSelectScreen), "CompleteSelection")]
    [HarmonyPatch(typeof(NDeckTransformSelectScreen), "CompleteSelection")]
    [HarmonyPostfix]
    private static void AfterFinish(NCardGridSelectionScreen __instance, TaskCompletionSource<IEnumerable<CardModel>>? __state)
    {
        if (__state is null)
        {
            return;
        }

        var standIn = CardScreenAccess.Completion(__instance);
        CardScreenAccess.Completion(__instance) = __state;
        DeckChoices.KeepOpen = null;
        if (standIn?.Task.IsCompletedSuccessfully == true)
        {
            var picked = standIn.Task.Result.ToList();
            Views.Refresh("card choice", () => DeckChoices.Picked(__instance, picked));
        }
    }

    [HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Remove))]
    [HarmonyPrefix]
    private static bool KeepLinkedScreenOpen(IOverlayScreen screen) => !ReferenceEquals(screen, DeckChoices.KeepOpen);
}

/// <summary>The card screen fields the patches swap.</summary>
internal static class CardScreenAccess
{
    public static readonly AccessTools.FieldRef<NCardGridSelectionScreen, TaskCompletionSource<IEnumerable<CardModel>>?> Completion =
        AccessTools.FieldRefAccess<NCardGridSelectionScreen, TaskCompletionSource<IEnumerable<CardModel>>?>("_completionSource");
}
