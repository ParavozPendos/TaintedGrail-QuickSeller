# QuickSeller

Sell loot on the spot in **Tainted Grail: The Fall of Avalon** — for a share of the merchant price (50% by default). Works on both game builds: the default one (IL2CPP, BepInEx 6) and the `mono` beta branch (BepInEx 5).

- **Loot window:** R — sell the selected item, hold G — sell everything sellable; on-the-spot price shown in each row.
- **Inventory** (Bag tab and the list opened from an equipment slot): hold R — sell; a quick tap still drops. The tooltip shows the real price next to the base value.
- **World:** look at a lying item to see its price, press G — pick it up and sell.
- **Buyback:** U opens the last 25 sales (↑/↓, →/Enter to buy back, Esc to close).
- Price comes from the game's own trade formula (trade perks apply). Never sells quest items, keys, important, stolen, equipped, favourite items or money items. The item is removed and verified before coins are added — a failed sale never eats an item.
- UI follows the game language.

## Install
Pick the file for your game build. If the game folder has `GameAssembly.dll`, it is **IL2CPP** (the default Steam/GOG build); otherwise it is **Mono** (Steam → Properties → Betas → `mono`).

| Game build | Loader | File |
|---|---|---|
| IL2CPP (default) | [BepInEx 6 IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) x64, tested with 6.0.0-be.789 | `QuickSeller-x.y.z-IL2CPP.zip` |
| Mono (`mono` beta) | [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) x64, tested with 5.4.23.5 | `QuickSeller-x.y.z-Mono.zip` |

1. Install the loader into the game folder and start the game once.
2. Download the matching zip from [Releases](../../releases) and extract it into the game folder — the dll goes to `BepInEx\plugins\QuickSeller\`.
3. Config: `BepInEx\config\leon.tg.quickseller.cfg`.

To uninstall, delete `BepInEx\plugins\QuickSeller`.

## Build
.NET SDK 6+. One source builds both runtimes (`net6.0` → IL2CPP, `netstandard2.1` → Mono); each one is built when its references are found:
IL2CPP needs a game folder with BepInEx 6 started once (`BepInEx\interop`), Mono needs a Mono-branch game folder with BepInEx 5.
Copy `local.props.example` to `local.props` and set the paths, then run `./build.ps1` (zips go to `dist\`; `-Deploy` also copies the matching dll into the game).

License: MIT.
