# Thor Dual Screen

SMAPI mod that puts Stardew Valley panels on the AYN Thor's bottom screen when running in
Cinderbox, with no companion app. See `BRIEF.md` for background.

Panels: **Today** (luck, weather, crops, events, birthdays), **Nearby** (held item gift
tastes and bundles, nearest villager), **Bag** (touch inventory; moves/sells when a chest or
shop is open).

## Build

`refs/` holds the assemblies the mod compiles against. Pull them from the device:

- `Stardew Valley.dll`, `StardewValley.GameData.dll`, `xTile.dll` from `/sdcard/StardewValley/desktop/GameFiles`
- `StardewModdingAPI.dll` from `/sdcard/StardewValley/smapi-internal`
- `Mono.Android.dll`, `Java.Interop.dll`, `MonoGame.Framework.dll` unpacked from Cinderbox's
  `lib/arm64-v8a/libassembly-store.so` (LZ4 blocks with an `XALZ` header)

```sh
cd mod && dotnet build -c Release
```

## Deploy

```sh
adb push mod/bin/Release/net10.0/DualScreen.dll mod/manifest.json /sdcard/StardewValley/desktop/Mods/ThorDualScreen/
adb shell am force-stop com.game.cinderbox
```
