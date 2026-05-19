# Fieldwright

A client-side blueprint and build-along tool for [Vintage Story](https://www.vintagestory.at/). Copy a region with an anchor corner, save it as a blueprint file, then paste a translucent ghost that snaps to your crosshair and auto-rotates to face you. Rebuild the structure block-by-block in survival or creative with a live material checklist showing what you have versus what you still need.

**Status**: v0.1.0 — Phase 3a complete (selection, save/load, ghost render, snap-to-look, place toggle, match tracking, checklist HUD). Chiseled-block matching and the in-game handbook tab are Phase 4 and 5 respectively.

---

## Features

- **Translucent ghost preview.** Saved blueprints render as a see-through 3D copy of the structure, visible only to the local player.
- **Snap-to-look placement.** While in floating mode, the ghost follows your crosshair and auto-rotates so the anchor's saved player-facing side faces you. No rotate keys needed for most placements.
- **Live build-along checklist.** Top-left HUD shows materials needed (grouped by block type), what's currently in your hotbar + backpack, and a real-time progress bar. Updates every time you place or break a block.
- **Auto-completion.** When every cell matches and no extra blocks are in air-positions, the ghost and HUD auto-dismiss after a short countdown.
- **Anchor-aware grouping.** Variant-rich blocks (slanted roofing, hay bales, oriented logs) match any orientation of the same family. Coarse on purpose: the engine picks the variant when you place a block, not you.
- **Overwrite protection.** Saving over an existing name requires an explicit `overwrite` keyword and creates a single rolling `.bak.json` backup.
- **Pure client-side.** No server install required. No network packets sent, no world mutations, no other players see anything. Works on any server that whitelists the mod.

---

## Install

1. Download the latest release `.zip` from the [Releases](https://github.com/Lueken/Fieldwright/releases) page (or the [ModDB listing](https://mods.vintagestory.at/fieldwright)).
2. Drop the zip into `%APPDATA%/VintagestoryData/Mods/`. Vintage Story will load it on next launch.

Or unpack the contents (modinfo.json + Fieldwright.dll) into a folder at `%APPDATA%/VintagestoryData/Mods/fieldwright/`.

---

## Quick start

### Save a blueprint

1. Stand at one corner of the structure you want to copy. Look at the corner block (the face you aim at becomes the saved "front" for auto-rotation later).
2. Press **Ctrl+Shift+B** to set corner 1 (also the placement anchor).
3. Walk to the diagonally opposite **top** corner. Look at it.
4. Press **Ctrl+Shift+N** to set corner 2. Chat shows the bounding size.
5. Optional: use `.fw grow up 1` / `.fw shrink north 2` etc. to adjust faces.
6. Run `.fw save my-house`. Selection clears and a JSON file is written to `%APPDATA%/VintagestoryData/Blueprints/my-house.json`.

### Paste and build along

1. Travel anywhere (any world, any server).
2. `.fw paste my-house`. A translucent ghost appears, following your crosshair.
3. Aim at a block face where you want to build. The ghost rotates automatically so its saved front faces you.
4. Press **Ctrl+Shift+P** when the position looks right. The ghost locks and the checklist HUD opens.
5. Place real blocks at the ghost cells. Each match decrements the checklist. Wrong-position blocks appear under "Blocks to remove".
6. When everything matches, the ghost auto-dismisses after about 5 seconds.

If you need to reposition mid-build, press **Ctrl+Shift+P** again to unlock. The HUD closes and the ghost goes back to floating.

---

## Commands and hotkeys

| Action | Hotkey | Chat command |
|---|---|---|
| Set corner 1 (placement anchor) | `Ctrl+Shift+B` | `.fw corner1` |
| Set corner 2 | `Ctrl+Shift+N` | `.fw corner2` |
| Grow selection on a face | — | `.fw grow <up\|down\|north\|south\|east\|west> [n]` |
| Shrink selection on a face | — | `.fw shrink <up\|down\|north\|south\|east\|west> [n]` |
| Show selection status | — | `.fw status` |
| Clear selection | — | `.fw clear` |
| Save selection as blueprint | — | `.fw save <name> [overwrite]` |
| List saved blueprints | — | `.fw list` |
| Paste a blueprint (floating ghost) | — | `.fw paste <name>` |
| Toggle place / unlock ghost | `Ctrl+Shift+P` | `.fw place` |
| Toggle checklist HUD | `Ctrl+Shift+L` | — |
| Dismiss active ghost | — | `.fw cancel` |

Hotkeys are rebindable in **Settings → Controls → Mod controls** under "Fieldwright".

---

## Blueprint files

Saved as JSON under `%APPDATA%/VintagestoryData/Blueprints/{name}.json`. The format wraps the standard Vintage Story `BlockSchematic` payload with an anchor offset and an anchor face:

```json
{
  "version": "0.1.0",
  "anchorOffset": { "x": 0, "y": 0, "z": 0 },
  "anchorFace": "north",
  "schematic": { ... standard BlockSchematic JSON ... }
}
```

Bare `BlockSchematic` files (e.g. exports from WorldEdit or BetterRuins) load as well, with an implicit anchor at the schematic's min corner and no auto-rotation.

**File ownership**: blueprint files are yours. The mod claims no rights over their content. Share them freely.

---

## Server compatibility

Fieldwright is fully client-side:
- No network packets sent or received.
- No world mutations. Real blocks are placed through Vintage Story's normal channels and validated by the server.
- Other players cannot see your ghost, HUD, or selection box.

It works on any vanilla server out of the box. **However**, some servers run a strict mod allowlist that rejects unknown client mods at connection time, even read-only ones. If you can't connect with Fieldwright installed, ask the server admin to add `fieldwright` to their allowed-mods list:

> Could you add `fieldwright` to the server's allowed-mods list? It's a client-side blueprint-preview tool, no network packets, no world changes. Same risk profile as a minimap mod.

---

## Build from source

Requires .NET 10 SDK and a Vintage Story install.

```powershell
git clone https://github.com/Lueken/Fieldwright.git
cd Fieldwright
$env:VINTAGE_STORY = "$env:APPDATA\Vintagestory"
dotnet build -c Debug
```

Build output lands in `bin/Debug/Mods/`. Copy the `Fieldwright.dll` and `modinfo.json` into a folder at `%APPDATA%/VintagestoryData/Mods/fieldwright/`.

---

## Roadmap

- **v0.1.x** (current): selection, save/load, ghost, snap-to-look, match tracking, checklist HUD.
- **Phase 4 — Chisel-pass tracking**: voxel-level comparison for chiseled blocks. After base blocks are placed and matched, the ghost transitions into a chisel-annotation pass that highlights which voxels need to be removed or recolored.
- **Phase 5 — In-game handbook tab**: browse saved blueprints in the Vintage Story handbook with size, material breakdown, and a paste-this button.
- **Polish backlog**: rebindable hotkeys via mod config file, finer-grained block grouping (separate oak vs pine logs), inline 3D preview in handbook, rotation/mirror UI for power users, .fw restore command for one-key backup recovery.

---

## Contributing

Bug reports and pull requests welcome at [github.com/Lueken/Fieldwright/issues](https://github.com/Lueken/Fieldwright/issues). When reporting issues, please include:

- Vintage Story version
- Mod version (from `modinfo.json`)
- Steps to reproduce
- Relevant lines from `%APPDATA%/VintagestoryData/Logs/client-main.log` (search for `[fieldwright:`)

---

## License

Code is MIT licensed — see [LICENSE](LICENSE). Blueprint JSON files saved by users are user-owned and outside the mod's licensing scope.

Authored by Venah (`Lueken` on GitHub).
