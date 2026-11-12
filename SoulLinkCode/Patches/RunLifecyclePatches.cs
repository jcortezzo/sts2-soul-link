using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.SoulLinkCode.Campfires;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Chests;
using SoulLink.SoulLinkCode.Events;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Rewards;
using SoulLink.SoulLinkCode.Shops;

namespace SoulLink.SoulLinkCode.Patches;

/// <summary>Starts and stops the Soul Link session with each run.</summary>
[HarmonyPatch(typeof(RunManager))]
internal static class RunLifecyclePatches
{
    /// <summary>Every new, loaded or test run goes through this private method once its net service is set.</summary>
    [HarmonyPatch("InitializeShared")]
    [HarmonyPostfix]
    private static void StartSession(INetGameService netService)
    {
        ResetScreens();
        SoulLinkSession.Start(netService);
        CardLinksSync.StartRun(OptionLinks.RunState);
    }

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPostfix]
    private static void StopSession()
    {
        SoulLinkSession.Stop();
        ResetScreens();
    }

    /// <summary>Forgets every screen's tracking from the last run.</summary>
    private static void ResetScreens()
    {
        OptionLinks.Reset();
        CardRewardLinks.Reset();
        CardRewardExecutor.Reset();
        RelicRewardLinks.Reset();
        EventLinks.Reset();
        EventExecutor.Reset();
        ChestLinks.Reset();
        CampfireLinks.Reset();
        ShopLinks.Reset();
        DeckChoices.Reset();
    }
}
