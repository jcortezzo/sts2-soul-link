using SoulLink.Core.Cards;
using static SoulLink.Core.Tests.TestCards;
using static SoulLink.Core.Tests.TestPlayers;

namespace SoulLink.Core.Tests.Cards;

public class StarterLinksTests
{
    private static StarterDeck Ironclad(PlayerId player) =>
        Deck(player, Repeat("Strike", 5).Concat(Repeat("Defend", 4)).Append("Bash"));

    private static StarterDeck Silent(PlayerId player) =>
        Deck(player, Repeat("Strike", 5).Concat(Repeat("Defend", 5)).Append("Neutralize").Append("Survivor"));

    private static StarterDeck Deck(PlayerId player, IEnumerable<string> names) =>
        new(player, names.Select((name, i) => new StarterCard(Card(player, i + 1), name)).ToList());

    private static IEnumerable<string> Repeat(string name, int count) => Enumerable.Repeat(name, count);

    [Fact]
    public void Identical_decks_link_card_for_card()
    {
        var groups = StarterLinks.Pair([Ironclad(Ana), Ironclad(Ben)]);

        Assert.Equal(10, groups.Count);
        Assert.All(groups, group => Assert.Equal(2, group.Count));
        Assert.Contains([Ref(Ana, 10), Ref(Ben, 10)], groups);
    }

    [Fact]
    public void Shared_names_pair_in_order_then_other_starters_pair_in_order()
    {
        var groups = StarterLinks.Pair([Ironclad(Ana), Silent(Ben)]);

        Assert.Contains([Ref(Ana, 1), Ref(Ben, 1)], groups); // 1st Strike
        Assert.Contains([Ref(Ana, 9), Ref(Ben, 9)], groups); // 4th Defend
        Assert.Contains([Ref(Ana, 10), Ref(Ben, 11)], groups); // Bash with Neutralize
        Assert.DoesNotContain(groups, group => group.Contains(Ref(Ben, 10))); // 5th Defend has no counterpart
        Assert.DoesNotContain(groups, group => group.Contains(Ref(Ben, 12))); // Survivor has no counterpart
        Assert.Equal(10, groups.Count);
    }

    [Fact]
    public void A_name_only_some_decks_have_pairs_with_the_other_starters()
    {
        var groups = StarterLinks.Pair([Ironclad(Ana), Silent(Ben), Ironclad(Cal)]);

        Assert.Contains([Ref(Ana, 10), Ref(Ben, 11), Ref(Cal, 10)], groups);
    }

    [Fact]
    public void One_deck_links_nothing()
    {
        Assert.Empty(StarterLinks.Pair([Ironclad(Ana)]));
    }
}
