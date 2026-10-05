# Thor Dual Screen

A free SMAPI mod that turns the AYN Thor's second screen into a touch companion for Stardew Valley
running in [Cinderbox](https://github.com/Ekyso/Cinderbox).

![Title screen: the logo on top, the menu on the bottom](docs/screenshots/title.png)

## Why this exists

A few dual-screen projects for handhelds have appeared recently that put basic features behind a
paywall or ship closed builds, often quickly assembled and hard to inspect. I think a mod that
runs inside your game, next to your save files, should be something you can read before you trust
it.

So this one is, and always will be:

> [!IMPORTANT]
> **Free and open source, forever.** No paid tier, no Patreon-only build, no feature held back.
> Every line that runs on your device is in this repository under the [GPL-3.0](LICENSE): read it,
> build it yourself, or point someone you trust at it. Any fork, including a paid one, has to stay
> open too.

> [!IMPORTANT]
> **Built with AI.** This mod was written with heavy help from an AI coding assistant (Claude),
> directed, tested and reviewed on real hardware by me. I'd rather say so up front than leave you
> to guess.

## What it adds

> [!TIP]
> **It adds, it never duplicates.** Every feature either gives you something the game doesn't, or
> makes something the controller makes awkward easy. The bottom screen is not a second copy of the
> top one: it doesn't mirror the map, repeat the HUD, or reveal anything the game hides from you.

| **Today** | **Gifts** | **Bag** | **Craft** |
|---|---|---|---|
| <a href="docs/screenshots/today.png"><img src="docs/screenshots/today.png" alt="Today"></a> | <a href="docs/screenshots/gifts.png"><img src="docs/screenshots/gifts.png" alt="Gifts"></a> | <a href="docs/screenshots/bag.png"><img src="docs/screenshots/bag.png" alt="Bag"></a> | <a href="docs/screenshots/craft.png"><img src="docs/screenshots/craft.png" alt="Craft"></a> |
| Luck, weather, crops, birthdays | Who loves what you hold | Touch inventory, drag, trash, stack | Recipes with have/need |
| **Aim** | **Chest** | **Shop** | **Shipping bin** |
| <a href="docs/screenshots/aim.png"><img src="docs/screenshots/aim.png" alt="Aim"></a> | <a href="docs/screenshots/chest.png"><img src="docs/screenshots/chest.png" alt="Chest"></a> | <a href="docs/screenshots/shop.png"><img src="docs/screenshots/shop.png" alt="Shop"></a> | <a href="docs/screenshots/shipping.png"><img src="docs/screenshots/shipping.png" alt="Shipping bin"></a> |
| Tap a tile to place or use a tool | Tap to take or store | Buy 1/5/25, sell any amount | Pick an amount to ship |
| **Clint's geodes** | **Title screen** | **Load** | **Tabs** |
| <a href="docs/screenshots/geodes.png"><img src="docs/screenshots/geodes.png" alt="Clint's geodes"></a> | <a href="docs/screenshots/title.png"><img src="docs/screenshots/title.png" alt="Title screen"></a> | <a href="docs/screenshots/load.png"><img src="docs/screenshots/load.png" alt="Load"></a> | <a href="docs/screenshots/tabs.png"><img src="docs/screenshots/tabs.png" alt="Tabs"></a> |
| Tap a geode to crack it | Buttons on the bottom | Your saves as big cards | Hide and reorder tabs |

Every menu goes through the game's own code, so prices, rules and sounds are exactly the game's.
**[Read the full feature guide →](docs/FEATURES.md)**

## What it deliberately doesn't do

- **No duplicates.** No minimap that just repeats the top screen, no second copy of the clock or
  money.
- **No cheats.** It shows what the game would tell you anyway (the TV, the calendar, a villager's
  reaction), not hidden numbers.
- **No keep-awake.** The panel hides when the Thor sleeps and never holds the screen on.
- **Your controller stays in charge.** The bottom window never takes button focus, and touching it
  doesn't switch the game into mouse mode.

## Requirements

- AYN Thor (the bottom screen is the Android "presentation" display).
- [Cinderbox](https://github.com/Ekyso/Cinderbox) with its SMAPI.

> [!IMPORTANT]
> **Bring your own game.** You need your own copy of Stardew Valley from
> [Steam](https://store.steampowered.com/app/413150/Stardew_Valley/) or
> [GOG](https://www.gog.com/en/game/stardew_valley). No game files ship with this mod.

## Install

1. Download `ThorDualScreen` from the [releases page](../../releases), or build it (below).
2. Copy the folder to `/sdcard/StardewValley/desktop/Mods/ThorDualScreen/`.
3. Start the game from Cinderbox. The bottom screen lights up on the title screen.

> [!WARNING]
> Back up `/sdcard/StardewValley/desktop/Saves` before trying any new mod.

## How it works

Cinderbox runs SMAPI inside the same Android process as the game, so a mod can reach Android
directly. This one opens a `Presentation` window on the second display, draws each panel offscreen
with the game's own `SpriteBatch`, textures and fonts, and copies the pixels into that window a few
times a second (30 on Aim). Touches are read on the Android side and replayed on the game thread as
clicks on the game's own menus, so the game decides what every tap does.

## Build

`refs/` holds the assemblies the mod compiles against. They're the game's and Cinderbox's, so they
aren't in this repo; pull them from your own device:

- `Stardew Valley.dll`, `StardewValley.GameData.dll`, `xTile.dll` from `/sdcard/StardewValley/desktop/GameFiles`
- `StardewModdingAPI.dll` from `/sdcard/StardewValley/smapi-internal`
- `Mono.Android.dll`, `Java.Interop.dll`, `MonoGame.Framework.dll` unpacked from Cinderbox's
  `lib/arm64-v8a/libassembly-store.so` (LZ4 blocks with an `XALZ` header)

```sh
cd mod && dotnet build -c Release
adb push mod/bin/Release/net10.0/DualScreen.dll mod/manifest.json /sdcard/StardewValley/desktop/Mods/ThorDualScreen/
```

## Credits

This mod stands on other people's work. Thank you to:

**What it runs on**
- **[ConcernedApe](https://www.stardewvalley.net/)** for Stardew Valley. Every sprite on the bottom
  screen is drawn live from your own copy of the game; none of the game's art or code is in this
  repository.
- **[Ekyso](https://github.com/Ekyso)** for [Cinderbox](https://github.com/Ekyso/Cinderbox) and
  [SMAPI for Cinderbox](https://github.com/Ekyso/SMAPI-For-Cinderbox), which make desktop Stardew
  and its mods run on Android in the first place.
- **[Pathoschild](https://github.com/Pathoschild)** and the SMAPI contributors for
  [SMAPI](https://smapi.io), the modding API everything here plugs into.
- The **[MonoGame](https://monogame.net/)** team, and Microsoft's
  **[.NET for Android](https://github.com/dotnet/android)**, whose Android bindings let a game mod
  open a window on a second screen.
- **[AYN](https://www.ayntec.com/)** for the Thor and its second screen.

**What showed the way**
- The Nintendo DS and 3DS games that worked out what a second screen is for, especially
  *Animal Crossing: New Leaf* and *Story of Seasons*: touch items, quick reference, nothing you
  have to watch during action.

**What it was built with**
- [ILSpy](https://github.com/icsharpcode/ILSpy) by the ICSharpCode team, for reading how the game's
  own menus work so the mod can call them instead of reimplementing them.
- [Claude](https://claude.com/claude-code) by Anthropic, as a coding assistant.

## Licence

[GPL-3.0](LICENSE). Stardew Valley and its assets belong to ConcernedApe; this licence covers only
the code in this repository.
