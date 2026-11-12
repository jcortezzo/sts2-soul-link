using System.Reflection;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SoulLink.SoulLinkCode.Campfires;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Events;
using SoulLink.SoulLinkCode.Rewards;
using SoulLink.SoulLinkCode.Shops;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "SoulLink";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        ModConfigRegistry.Register(ModId, new SoulLinkConfig());
        CardLinksSync.Initialize();
        SoulLinkSession.StateChanged += Views.RefreshAll;
        SoulLinkSession.StateChanged += CardRewardExecutor.Schedule;
        SoulLinkSession.StateChanged += EventExecutor.Schedule;
        SoulLinkSession.StateChanged += CampfireLinks.Schedule;
        SoulLinkSession.StateChanged += ShopLinks.Schedule;
        SoulLinkSession.StateChanged += DeckChoices.Schedule;

        // Each patch class is applied on its own, so one that no longer fits the game can't stop the others.
        Harmony harmony = new(ModId);
        var failed = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
        {
            if (type.GetCustomAttribute<HarmonyPatch>() is null)
            {
                continue;
            }

            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Error($"Soul Link couldn't patch {type.Name}: {ex}");
            }
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Logger.Info($"Soul Link {version} loaded: {harmony.GetPatchedMethods().Count()} game methods patched, {failed} patch classes failed.");
    }
}
