# Soul Link for Slay the Spire 2

A co-op mod for Slay the Spire 2 that links every out-of-combat decision between players: everyone who can make the same choice makes it together. By default a choice waits until everyone has clicked it; a setting lets the first click decide for everyone instead. Combat, potions and the map stay free.

It's inspired by Pokémon Soul Link challenges and the [Slay the Soul-Link](https://docs.google.com/document/d/1XyDEOh4QqVfNqKOosRGrHEikjtB77w95TzjEURAe4lY/edit?tab=t.0) challenge by Rayditz and naturesbf.

> **Status:** early development, ready for co-op playtesting. Every decision in the rules is linked in game: card
> rewards, deck card choices, shops, campfires, events, Ancients (Neow included), relic rewards and chests. Links between
> cards aren't saved with the run yet. Not on the Steam Workshop yet, and the name is a working title.

## Rules

The full rules, settings and worked examples are in [docs/rules.md](docs/rules.md). In short:

- **Linked:** card rewards, deck card choices (upgrade, remove, transform, enchant, duplicate), shop purchases, campfires, events, Ancients, relic rewards and chests.
- **Who decides:** with everyone agrees (the default), a click is a vote and a choice is made once everyone who can make it has clicked it. With first click, the first click makes the choice for everyone at once.
- **Nice or mean:** when a choice isn't possible for everyone, nice mode lets whoever can follow it, and mean mode greys it out for everyone.
- **Card matching:** your "same card" is the card at the same position on your card screen by default, or the card linked to theirs (gained from the same choice).
- **Shared death:** optional. When on, any player's death ends the run.

The host picks these in BaseLib's mod config menu, and everyone in the run plays under the host's settings.

## Repository layout

The mod is a project from Alchyr's template at the repo root, like any other StS2 mod, with the rules engine and its
tests in two folders beside it.

```text
SoulLink.csproj, SoulLink.json   the mod project and its manifest
SoulLink/                        mod image and config text, packed into SoulLink.pck on build
SoulLinkCode/                    config, network messages, run session, and the patches for each linked screen
SoulLink.Core/                   rules engine with no game references; compiled into SoulLink.dll
  Decisions/                     linked decisions: votes or first click, nice and mean modes
  Cards/                         link groups, starter pairing, linked cards and deck index matching
  Rewards/ Shops/ Campfires/     card reward screens, shop purchases, campfire categories
  Sync/                          host-ordered operations, so every player sees the same locks
SoulLink.Core.Tests/             xUnit tests, including one per worked example in the rules spec
docs/                            rules spec
```

Every peer runs the same rules engine. Players send their clicks to the host, which applies them in the order they
arrive and broadcasts numbered notices that everyone replays, so the host decides who clicked first. A notice carries
every pick that followed from the click, for any player, and each player's game then makes their own pick on screen.

A player joins a linked screen when it opens, and every click on it carries the join again until that player's game
has seen the host accept it, so one lost message can't leave a player out of a decision; the host ignores a join it
already has. The log flags a request the host hasn't answered in ten seconds and an update from the host that's
missing, since every later update waits for it.

## Build

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"; dotnet build SoulLink.csproj
```

Rider's Build button does the same. The build finds the game through Steam and copies `SoulLink.dll`, `SoulLink.pck`
and `SoulLink.json` into the game's `mods/SoulLink` folder. If the game isn't found, set `Sts2Path` in a
`Directory.Build.props` file next to the project. Godot isn't needed: the `.pck` is packed during the build.

You need the .NET SDK 9 or later, and [BaseLib](https://github.com/Alchyr/BaseLib-StS2) subscribed on the Steam
Workshop (ID 3737335127) and turned on in the game's mod list (Settings → Mods). Turn it on with only one game window
open, then restart: a second window that's still running can save its own copy of the setting over yours.

## Test

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"; dotnet test SoulLink.Core.Tests/SoulLink.Core.Tests.csproj
```

The rules engine tests don't need the game, and CI runs them on every push. The mod project isn't built in CI because it needs the game's DLLs.

To try co-op on one PC, build the mod, put a `steam_appid.txt` file containing `2868840` in the game folder so the game can start outside Steam, and start a host and a client from that folder:

```bat
start "" SlayTheSpire2.exe -fastmp host_standard -wpos 0 0
start "" SlayTheSpire2.exe -fastmp join -wpos 960 0
```

The host opens a standard co-op lobby and the client joins it after a moment; more clients need `-clientId 1001`, `1002` and so on. Each window's log should show `Soul Link ... loaded` near the top and `Soul Link session started as host` (or `client`) when the run starts; `depends on mods which have not been loaded: BaseLib` means BaseLib is still turned off. To watch the mod's log lines live, turn on "Open log window on startup" under Mod Configuration → BaseLib; the log is also saved in `%AppData%\SlayTheSpire2\logs\godot.log`.

## Contributing

- `main` is always releasable. Work happens on feature branches and merges through pull requests.
- Commit messages use a type prefix and a capitalized imperative summary, for example `feat: Add first-click decision resolver`.

## Credits

- Rules adapted from [Slay the Soul-Link](https://docs.google.com/document/d/1XyDEOh4QqVfNqKOosRGrHEikjtB77w95TzjEURAe4lY/edit?tab=t.0) by Rayditz and naturesbf.
- Built on [BaseLib](https://github.com/Alchyr/BaseLib-StS2) and [ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2) by Alchyr.

No license has been chosen yet.
