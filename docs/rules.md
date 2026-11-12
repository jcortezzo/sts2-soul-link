# Soul Link Rules Spec

This is the rules spec the mod implements, kept in the repo so the code and the rules change together.

## Overview

In a co-op run, every out-of-combat decision is linked: the first player to choose sets the choice, and everyone who can make the same choice must. Combat, potions and the map stay free.

- **Players:** 2 to 4, all running the mod.
- **Linked:** card rewards, deck card choices (upgrade, remove, transform, enchant, duplicate), shop purchases, campfires, events, Ancients, relic rewards and chests.
- **Settings** decide how strict it is: how cards are matched, what happens when someone can't follow (nice or mean), and whether one death ends the run.
- **Origin:** our own rules, plus the campfire categories and linked cards from the [Slay the Soul-Link](https://docs.google.com/document/d/1XyDEOh4QqVfNqKOosRGrHEikjtB77w95TzjEURAe4lY/edit?tab=t.0) challenge by Rayditz and naturesbf, extended to 4 players and enforced by the mod.

## Settings

The host picks these before the run starts, and everyone plays under the same settings.

| Setting | Options | Default | What it controls |
| --- | --- | --- | --- |
| Card matching | Linked cards, Deck index | Linked cards | Which of your cards counts as "the same card" for upgrades, removals and other deck choices |
| Mode | Nice, Mean | Nice | What happens when a linked choice isn't possible for everyone |
| Shared death | Off, On | Off | Whether any player's death ends the run |
| Who decides | First click | First click | Fixed for now; consensus could be added later as another option |

The card matching and mode defaults are proposals; see Proposed defaults and open items.

## How a linked decision resolves

The first player to commit a choice decides it for everyone who can make the same choice. A commit is picking a card, pressing Skip, buying, choosing an option or confirming a selection; hovering doesn't count.

**Nice mode (whoever can)**

1. When someone commits, every player who hasn't decided yet and can make the same choice must make it.
2. Players who can't are not bound. They decide for themselves, and the first of them to commit binds the rest of them who can follow.
3. An exclusive option, one only some players have (like a Lasting Candy extra card), can always be taken instead of following. Anyone else who has it must then match.

**Mean mode (everyone or no one)**

1. An option is greyed out unless every player in the decision can take it, so the first commit always binds everyone.
2. Exclusive options are greyed out unless everyone has them.
3. If no option works for everyone, that one decision falls back to nice mode.

**Both modes**

- **Can make the choice** means the option exists for you and the game would let you take it now: enough gold, not sold out, a free potion slot, an eligible card, not greyed out by the game.
- **Players in a decision** are the ones facing it at that moment. For example, only players with a second card reward share the second reward screen.
- **Ties:** if two commits land at nearly the same time, the host's order decides which came first.

## Card rewards

Everyone takes the same slot, or everyone skips. Slots count left to right on each player's own screen, so the same slot usually holds different cards.

- **Fewer cards:** if your screen lacks the chosen slot, you can't follow. Nice: you choose. Mean: that slot was greyed out.
- **Extra reward screens:** card reward screens are matched in order, and screen 2 is shared only by players who have a second one. This covers Prayer Wheel, Dream Catcher and [The Hunt](https://metabot.gg/en/slay-the-spire-2/card/the_hunt) (an extra card reward on a fatal hit). A screen only you have is yours to pick from freely, in both modes.
- **Extra cards on a screen:** an extra card only some players have, like [Lasting Candy](https://metabot.gg/en/slay-the-spire-2/relic/lasting_candy)'s bonus Power every other combat, is an exclusive option. Nice: the holder may take it instead of following. Mean: it's greyed out unless everyone has one.
- **Pael's Wing:** sacrificing is linked only among Pael's Wing holders, separately from the card pick. When a holder sacrifices, the other holders who haven't picked must sacrifice too, and players without Pael's Wing aren't affected. In both modes, a holder can sacrifice instead of following a card pick.
- **Driftwood (proposed):** rerolls are linked among Driftwood holders. When one rerolls a screen, the other holders who can must reroll theirs. A reroll isn't a commit; the first pick still decides the slot.

## Deck card actions

When several players choose a card from their deck for the same action, the first player's pick sets the target and everyone else takes their matching card. This covers upgrades, removals, transforms, enchants, duplicates and any other card choice outside combat.

The players in the action are the ones doing that same action at that moment: everyone who Smiths at a campfire, everyone who bought card removal, everyone who took the same event option. For multi-card picks (choose 2), the whole set must match.

### Linked cards (default)

Cards are linked when they come from the same linked decision, so a link group holds at most one card per player.

- **Same source:** the same card reward slot, shop slot, event pick or Ancient choice links the cards it gave.
- **Starter decks (proposed):** pair by name in deck order: Strike with Strike, Defend with Defend, then each character's other starter cards in order. Starter cards with no counterpart are unlinked.
- **Free choices:** players who decided freely together in nice mode link with each other.
- **Solo cards:** a card only you gained (from a relic, an exclusive option or an effect on one player) is unlinked.
- **Changes (proposed):** transforming keeps the link; upgrading and enchanting change nothing; removing takes the card out of its group; duplicating makes a new group of the copies.

### Deck index

Your matching card is the one at the same position on the selection screen, counted left to right and top to bottom as the game shows it. If your screen has fewer cards, your last card counts.

### When your matching card can't take the action

For example, it's already upgraded, it was removed, or the target was unlinked.

- **Nice:** you're not bound, so you pick any eligible card yourself.
- **Mean:** the first player can only pick targets whose matching cards can all take the action. With linked cards, unlinked cards and half-upgraded groups are locked.

## Shops

Each shop slot is its own linked decision: every card, relic and potion, plus the card removal service. Each player's shop has different stock and prices, so you buy whatever sits in that slot in your own shop, at your own price.

- **Nice:** when anyone buys a slot, everyone else who can buy that slot must buy it. Purchases resolve in the order they were made, so earlier purchases get first claim on gold.
- **Mean:** a slot can only be bought if every player can buy theirs at that moment; otherwise it's greyed out for everyone.
- **Can't buy** means not enough gold, sold out, no free potion slot, or removal already used in this shop.
- **Card removal:** the first buyer's chosen card sets the target, and everyone else removes their matching card (see Deck card actions).
- **Timing (proposed):** purchase obligations last until the party leaves the shop. Not buying isn't a decision, so it binds no one.
- **Potions** bought here follow these rules, even though potions are otherwise free.

## Campfires

The first player to choose at a campfire sets the category, and everyone else picks an option within it.

| Category | Options |
| --- | --- |
| Recover | Rest, Mend |
| Improve | Smith, Dig, Cook, Lift, and any other option a relic or quest adds (proposed) |

- **Within a category** you choose freely, and Mend's target is your choice.
- **Smith targets:** if more than one player Smiths, the first Smith target decides (see Deck card actions).
- **Can't do the category:** nice, you choose freely; mean, that category was greyed out.
- **[Miniature Tent](https://metabot.gg/en/slay-the-spire-2/relic/miniature_tent):** holders can choose any number of options, and their choices don't set the category for others. Anything a holder does that others also do still matches, like a Smith target.

## Events, Ancients, relic rewards and chests

Everyone takes the same option slot, counted in the order each player sees the options. Players often see different options and outcomes in the same slot, and that's expected.

- **Events:** each page of an event is its own decision. Events the game already puts to a group vote stay as they are.
- **Multi-pick screens** like Room Full of Cheese (choose 2 of 8 commons): the first player to confirm sets the set of slots, and everyone else picks the same set.
- **Card choices inside an event** (remove, transform, upgrade) follow Deck card actions.
- **Ancients:** same slot.
- **Relic rewards after fights:** taking or skipping is linked.
- **Treasure chests:** same slot, and this is intended. Co-op chests hold [one relic per player in a shared pool](https://slaythespire-2.com/blog/20260403-co-op), so everyone goes for the same relic. The game's rock-paper-scissors decides who gets it, and the others get what's left.

## Not linked

These stay exactly as in normal co-op:

- **Combat:** card plays, targets, potion use and ending your turn.
- **Potions**, except shop purchases: rewards, using, discarding and throwing them to allies.
- **The map:** the game's own vote.
- **Automatic effects:** gold and HP changes, and anything a relic or card does on its own.
- **Personal picks:** Mend's target and character selection.

## Death

Shared death is off by default. Then the game works as normal co-op: [a dead player revives at 1 HP after combat if anyone survives, and only a full wipe ends the run](https://slay-the-spire-2.pages.dev/guides/multiplayer-faq/).

With shared death on, any player's death ends the run for everyone, as in a Pokémon Soul Link. Either way, the mod counts each player's deaths for the end-of-run summary (proposed).

## QOL features

The mod shows what's linked before anyone clicks, so nobody has to count slots or remember links.

- **Slot peek:** hover any slot (card reward, shop item, event option, Ancient choice, chest relic) to see what every other player has in that slot.
- **Lock indicator:** after someone commits, your matching option is highlighted with their name. Options you can't take are disabled, with the reason.
- **Free indicator:** in nice mode, shows when you're not bound and why.
- **Linked-card view:** with linked cards, cards carry a link badge. Hover one to see its linked cards in every deck, and selection screens highlight your matching card.
- **Index view:** with deck index, selection screens number the cards.
- **Shop obligations:** a banner lists purchases you still owe, and who made you owe them.
- **Run summary:** first clicks and deaths per player.

## Worked examples

Each example is a scenario the mod must handle exactly this way, so each one becomes an acceptance test. The players are Ana, Ben and Cal.

1. **Card reward, nice.** Ana has a Lasting Candy extra Power; Ben and Cal have three cards each. Ben takes slot 2 first, so Cal must take slot 2. Ana may take slot 2 or her extra Power.
2. **Card reward, mean.** Same screens. Ana's extra Power is greyed out. Ben takes slot 2, so Ana and Cal take slot 2.
3. **Exclusive option first, nice.** Ana takes her extra Power first. Ben and Cal aren't bound by it; Ben skips first, so Cal skips too.
4. **Extra screens.** Ana has Prayer Wheel and Cal's The Hunt killed an enemy, so both get a second card reward. Screen 1 is linked for all three; screen 2 for Ana and Cal only.
5. **Shop, nice.** Ana buys relic slot 2. Ben can afford his relic slot 2, so he must buy it; Cal can't, so he's unaffected. Cal then buys card slot 1, so Ana and Ben must buy theirs if they can still afford them.
6. **Shop, mean.** Cal can't afford his relic slot 2, so relic slot 2 is greyed out for everyone.
7. **Smith, linked cards, nice.** Ana smiths a Strike from starter group 1. Ben's linked Strike is already upgraded, so Ben upgrades any card he likes. Cal upgrades his linked Strike.
8. **Smith, linked cards, mean.** Same decks. Starter group 1 is greyed out for Ana, because Ben's Strike can't be upgraded again.
9. **Smith, deck index.** Ana upgrades card 17 on her upgrade screen. Ben's screen shows 15 cards, so he upgrades card 15.
10. **Campfire with Miniature Tent.** Ana has Miniature Tent. Ben smiths first, so Cal must pick Smith, Dig, Cook or Lift. Ana can do every option; if she or Cal smith, their targets match Ben's.
11. **Chest.** Ana picks relic 1, so Ben and Cal must pick relic 1. Rock-paper-scissors decides who gets it, and the others get the remaining relics.
12. **Shared death on.** Cal dies in an elite fight. The run ends for everyone, even if Ana and Ben win the fight.

## Proposed defaults and open items

These fill gaps we haven't explicitly agreed on.

- **Defaults:** Linked cards and Nice mode.
- **Relics:** Driftwood rerolls are linked among holders.
- **Links:** starters pair by name in deck order; transforming keeps a link; duplicating makes a new group.
- **Shops:** purchase obligations last until the party leaves the shop.
- **Campfires:** options added by relics or quests count as Improve.
- **Ties:** the host's order decides simultaneous commits.
- **Summary:** death counter and first clicks per player.
- **Left out:** Slay the Soul-Link's random, different characters and must-take-the-first-multiplayer-card rules. Either could become a toggle later.
- **Name:** an existing StS2 mod is already called [Soul Link](https://www.nexusmods.com/slaythespire2/mods/907) (shared HP, block and gold), so our Workshop name is open.

To check in-game:

- [ ] How shop slots are laid out, so they can be numbered
- [ ] How selection screens order cards, for deck index
- [ ] How chest rock-paper-scissors works when 3 or 4 players pick one relic
- [ ] Which events the game puts to a group vote

## Credits and sources

- [Slay the Soul-Link rules](https://docs.google.com/document/d/1XyDEOh4QqVfNqKOosRGrHEikjtB77w95TzjEURAe4lY/edit?tab=t.0) by Rayditz and naturesbf ([video](https://www.youtube.com/watch?v=yIpWyO6Xgzk)): campfire categories, linked cards and the relic rules started here.
- Co-op mechanics: [slaythespire-2.com co-op guide](https://slaythespire-2.com/blog/20260403-co-op), [FinalBoss multiplayer guide](https://finalboss.io/slay-the-spire-2-how-to-play-multiplayer), [multiplayer FAQ](https://slay-the-spire-2.pages.dev/guides/multiplayer-faq/).
- Relics and cards: [Miniature Tent](https://metabot.gg/en/slay-the-spire-2/relic/miniature_tent), [Lasting Candy](https://metabot.gg/en/slay-the-spire-2/relic/lasting_candy), [The Hunt](https://metabot.gg/en/slay-the-spire-2/card/the_hunt), [Pael's Wing](https://brokenbuilds.gg/slay-the-spire-2/relics/paels-wing), [Driftwood](https://metabot.gg/en/slay-the-spire-2/relic/driftwood).
- Events: [Room Full of Cheese (Destructoid)](https://www.destructoid.com/?p=1190388).
