using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
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

/// <summary>Notes when the game starts waiting for a card screen, and keeps a linked screen open while its pick is out.</summary>
[HarmonyPatch]
internal static class CardScreenPatches
{
    [HarmonyPatch(typeof(NCardGridSelectionScreen), nameof(NCardGridSelectionScreen.CardsSelected))]
    [HarmonyPostfix]
    private static void Opened(NCardGridSelectionScreen __instance) => Views.Refresh("card choice", () => DeckChoices.Opened(__instance));

    [HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Remove))]
    [HarmonyPrefix]
    private static bool KeepLinkedScreenOpen(IOverlayScreen screen) => !ReferenceEquals(screen, DeckChoices.KeepOpen);
}

/// <summary>
/// Takes the pick out of a linked card screen as it would finish: the screen finishes into a stand-in, so the pick can
/// be read without the game acting on it, and stays open until the host commits the local player's cards.
/// </summary>
[HarmonyPatch]
internal static class CardScreenFinishPatches
{
    /// <summary>Where each card screen finishes with the player's pick.</summary>
    private static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(NDeckUpgradeSelectScreen), "CheckIfSelectionComplete"),
        AccessTools.Method(typeof(NDeckCardSelectScreen), "CheckIfSelectionComplete"),
        AccessTools.Method(typeof(NDeckEnchantSelectScreen), "CheckIfSelectionComplete"),
        AccessTools.Method(typeof(NSimpleCardSelectScreen), "CompleteSelection"),
        AccessTools.Method(typeof(NDeckTransformSelectScreen), "CompleteSelection"),
    ];

    [HarmonyPrefix]
    private static void BeforeFinish(NCardGridSelectionScreen __instance, out TaskCompletionSource<IEnumerable<CardModel>>? __state)
    {
        __state = null;
        if (!Guard.Show("check a card screen", () => DeckChoices.IsLinked(__instance)))
        {
            return;
        }

        __state = CardScreenAccess.Completion(__instance);
        CardScreenAccess.Completion(__instance) = new TaskCompletionSource<IEnumerable<CardModel>>();
        DeckChoices.KeepOpen = __instance;
    }

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
}

/// <summary>The card screen fields the patches swap.</summary>
internal static class CardScreenAccess
{
    public static readonly AccessTools.FieldRef<NCardGridSelectionScreen, TaskCompletionSource<IEnumerable<CardModel>>?> Completion =
        AccessTools.FieldRefAccess<NCardGridSelectionScreen, TaskCompletionSource<IEnumerable<CardModel>>?>("_completionSource");
}

/// <summary>
/// Keeps card ids and links when the game rebuilds a teammate's deck from the copy their game sent, which it does on
/// every room entry.
/// </summary>
[HarmonyPatch]
internal static class DeckResyncPatches
{
    [HarmonyPatch(typeof(Player), nameof(Player.SyncWithSerializedPlayer))]
    [HarmonyPrefix]
    private static void Before(Player __instance) => Guard(() => CardIds.BeginResync(__instance));

    /// <summary>Runs even if the game's rebuild throws, so the deck never stays marked as rebuilding.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SyncWithSerializedPlayer))]
    [HarmonyFinalizer]
    private static void After(Player __instance) => Guard(() => CardIds.EndResync(__instance));

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Soul Link couldn't keep a deck's card ids: {ex}");
        }
    }
}
