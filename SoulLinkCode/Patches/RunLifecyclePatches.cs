using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using SoulLink.SoulLinkCode.Events;
using SoulLink.SoulLinkCode.Rewards;

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
        CardRewardLinks.Reset();
        CardRewardExecutor.Reset();
        EventLinks.Reset();
        EventExecutor.Reset();
    }
}
