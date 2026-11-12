# Soul Link for Slay the Spire 2

A co-op mod for Slay the Spire 2 that links every out-of-combat decision between players: everyone who can make the same choice makes it together. By default a choice waits until everyone has clicked it; a setting lets the first click decide for everyone instead. Combat, potions and the map stay free.

It's inspired by Pokémon Soul Link challenges and the [Slay the Soul-Link](https://docs.google.com/document/d/1XyDEOh4QqVfNqKOosRGrHEikjtB77w95TzjEURAe4lY/edit?tab=t.0) challenge by Rayditz and naturesbf.

> **Status:** early development. The rules engine and the host-ordered sync are done and tested, and card rewards
> are linked in game; the other decision screens are next. Not on the Steam Workshop yet, and the name is a working
> title.

## Rules

The full rules, settings and worked examples are in [docs/rules.md](docs/rules.md). In short:

- **Linked:** card rewards, deck card choices (upgrade, remove, transform, enchant, duplicate), shop purchases, campfires, events, Ancients, relic rewards and chests.
- **Who decides:** with everyone agrees (the default), a click is a vote and a choice is made once everyone who can make it has clicked it. With first click, the first click makes the choice for everyone at once.
- **Nice or mean:** when a choice isn't possible for everyone, nice mode lets whoever can follow it, and mean mode greys it out for everyone.
- **Card matching:** your "same card" is either the card linked to theirs (gained from the same choice) or the card at the same position.
- **Shared death:** optional. When on, any player's death ends the run.

The host picks these in BaseLib's mod config menu, and everyone in the run plays under the host's settings.

## Repository layout

```text
src/SoulLink/                  the mod (from Alchyr's BaseLib template): config, network messages, run session, patches
src/SoulLink.Core/             rules engine with no game references; compiled into the mod's DLL
  Decisions/                   linked decisions: votes or first click, nice and mean modes
  Cards/                       link groups, starter pairing, linked cards and deck index matching
  Rewards/ Shops/ Campfires/   card reward screens, shop purchases, campfire categories
  Sync/                        host-ordered operations, so every player sees the same locks
tests/SoulLink.Core.Tests/     xUnit tests, including one per worked example in the rules spec
docs/                          rules spec
```

Every peer runs the same rules engine. Players send their clicks to the host, which applies them in the order they
arrive and broadcasts numbered notices that everyone replays, so the host decides who clicked first. A notice carries
every pick that followed from the click, for any player, and each player's game then makes their own pick on screen.

## Requirements

- Slay the Spire 2 on Steam
- .NET SDK 9 or later
- [BaseLib](https://github.com/Alchyr/BaseLib-StS2): subscribe on the Steam Workshop (ID 3737335127). The project pulls it in as the `Alchyr.Sts2.BaseLib` NuGet package.

Godot isn't needed. The mod is code only, and Godot (MegaDot 4.5.1) is used only by `dotnet publish` to pack assets into a `.pck`.

## Build

Open `SoulLink.sln` in Rider or Visual Studio, or build the mod from the command line:

```sh
dotnet build src/SoulLink/SoulLink.csproj
```

The build finds the game through Steam, compiles against the game's `sts2.dll`, and copies `SoulLink.dll`, `SoulLink.json` and `SoulLink.pdb` into `<game>/mods/SoulLink/`. To use a different install, copy `src/SoulLink/Directory.Build.props.example` to `Directory.Build.props` (git-ignored) and set `Sts2Path`.

## Test

```sh
dotnet test tests/SoulLink.Core.Tests/SoulLink.Core.Tests.csproj
```

The rules engine tests don't need the game, and CI runs them on every push. The mod project isn't built in CI because it needs the game's DLLs.

To test co-op on one PC, start several clients with the community-documented `--fastmp` launch flags, run from the game folder:

```bat
start "" SlayTheSpire2.exe --fastmp=host -wpos 0 0
start "" SlayTheSpire2.exe --fastmp=join --clientId=1001 -wpos 960 0
```

## Contributing

- `main` is always releasable. Work happens on feature branches and merges through pull requests.
- Commit messages use a type prefix and a capitalized imperative summary, for example `feat: Add first-click decision resolver`.

## Credits

- Rules adapted from [Slay the Soul-Link](https://docs.google.com/document/d/1XyDEOh4QqVfNqKOosRGrHEikjtB77w95TzjEURAe4lY/edit?tab=t.0) by Rayditz and naturesbf.
- Built on [BaseLib](https://github.com/Alchyr/BaseLib-StS2) and [ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2) by Alchyr.

No license has been chosen yet.
