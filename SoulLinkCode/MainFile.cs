using System.Reflection;
using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SoulLink.SoulLinkCode.Events;
using SoulLink.SoulLinkCode.Rewards;
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
        SoulLinkSession.StateChanged += Views.RefreshAll;
        SoulLinkSession.StateChanged += CardRewardExecutor.Schedule;
        SoulLinkSession.StateChanged += EventExecutor.Schedule;

        Harmony harmony = new(ModId);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
}
