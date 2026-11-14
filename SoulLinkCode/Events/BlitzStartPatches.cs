using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Blitz;
using SoulLink.SoulLinkCode.Cards;
using SoulLink.SoulLinkCode.Linking;
using SoulLink.SoulLinkCode.Ui;

namespace SoulLink.SoulLinkCode.Events;

/// <summary>
/// Links the start of a Blitz run: instead of Neow's blessings, every player picks 3 of 10 cards offered on the spot,
/// and the picks match by slot like any other offered cards. Neow's page there has a single option, so it isn't a
/// decision itself; the card screen it opens is.
/// </summary>
[HarmonyPatch]
internal static class BlitzStartPatches
{
    [HarmonyPatch(typeof(BlitzRunModel), nameof(BlitzRunModel.GiveNeowRewards))]
    [HarmonyPrefix]
    private static void LinkStartingCards(Player player) => Guard.Run("link Blitz's starting cards", () =>
    {
        if (LocalContext.IsMe(player) && OptionLinks.Room is { } room && OptionLinks.RunState is { } state)
        {
            DeckChoices.Acting($"{room}/blitz-start", "cards", () => state.Players.Select(OptionLinks.IdOf).ToList());
        }
    });
}
