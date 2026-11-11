using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;

namespace SoulLink.SoulLinkCode.Patches;

/// <summary>Starts and stops the Soul Link session with the run, and closes a room's decisions when the party leaves.</summary>
[HarmonyPatch(typeof(RunManager))]
internal static class RunLifecyclePatches
{
    [HarmonyPatch(nameof(RunManager.InitializeShared))]
    [HarmonyPostfix]
    private static void StartSession(RunManager __instance) => SoulLinkSession.Start(__instance.NetService);

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPostfix]
    private static void StopSession() => SoulLinkSession.Stop();

    [HarmonyPatch("EnterRoom")]
    [HarmonyPrefix]
    private static void CloseRoomDecisions() => SoulLinkSession.Current?.State.CloseAll();
}
