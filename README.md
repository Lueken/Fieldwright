# Fieldwright

A client-side blueprint and build-along tool for [Vintage Story](https://www.vintagestory.at/). Copy a region with an anchor corner, save it as a blueprint file, then paste a translucent ghost that snaps to your crosshair and auto-rotates to face you. Rebuild the structure block-by-block in survival or creative with a live material checklist showing what you have versus what you still need.

**Status**: v0.1.1. Selection, save/load, ghost render with snap-to-look, place toggle, match tracking, checklist HUD, schematic library UI, layer-by-layer view, 3-axis mirror, three matching modes, and red highlights on blocks to clear. Chiseled-block voxel matching is Phase 4 (v0.2).

---

## Features

- **Translucent ghost preview.** Saved blueprints render as a see-through 3D copy of the structure, visible only to the local player.
- **Snap-to-look placement with auto-rotation.** While in floating mode, the ghost follows your crosshair and auto-rotates so the anchor's saved player-facing side faces you. No rotate keys needed for most placements.
- **3-axis mirror toggle.** Cycle the floating or placed ghost through X, Y, Z mirrors with a single hotkey. Applied in the local frame so it composes correctly with rotation.
- **Schematic library UI.** Browse saved blueprints in a native dialog with size, block count, anchor face, modified date, and a "has backup" badge. Per-row Paste and Delete with two-step confirmation.
- **Layer-by-layer view.** Peel layers off the top of the placed ghost with PgDn / PgUp so you can see interior layers of tall builds.
- **Three matching modes.** Loose ("any cobble for any cobble"), Medium (variant-aware: type matters, orientation doesn't), or Strict (exact block code). Default is Loose; configurable per-paste or globally.
- **Live build-along checklist.** Top-left HUD shows materials needed (with clean labels regardless of matching mode), what's currently in your hotbar + backpack, and a real-time progress bar.
- **Red highlight on blocks to clear.** Cells that should be air (or that hold the wrong block) get a translucent red overlay so you can locate them in 3D.
- **Auto-completion.** When every cell matches and no extra blocks remain, the ghost and HUD auto-dismiss after a short countdown.
- **Overwrite protection.** Saving over an existing name requires an explicit `overwrite` keyword and creates a single rolling `.bak.json` backup. Restore with `.fw restore <name>`.
- **Pure client-side.** No server install required. No network packets sent, no world mutations, no other players see anything. Works on any server that whitelists the mod.
- **Mod config file** at `%APPDATA%/VintagestoryData/ModConfig/Fieldwright.json`. Tune ghost alpha, render distance, HUD position, auto-dismiss timeout, default matching mode.

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
| Clear selection only | — | `.fw clear` |
| Save selection as blueprint | — | `.fw save <name> [overwrite]` |
| List saved blueprints (text) | — | `.fw list` |
| Open the blueprint library UI | `Ctrl+Shift+K` | `.fw library` |
| Paste a blueprint (floating ghost) | — | `.fw paste <name> [loose\|medium\|strict]` |
| Toggle place / unlock ghost | `Ctrl+Shift+P` | `.fw place` |
| Cycle ghost mirror axis | `Ctrl+Shift+M` | `.fw mirror` |
| Restore/peel ghost layers | `PgUp` / `PgDn` | — |
| Toggle checklist HUD | `Ctrl+Shift+L` | — |
| Cancel: dismiss ghost + clear selection | `Ctrl+Shift+X` | `.fw cancel` |
| Restore a blueprint from its rolling backup | — | `.fw restore <name>` |

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

- **v0.1.0** (shipped 2026-05-18): selection, save/load, ghost, snap-to-look, match tracking, checklist HUD.
- **v0.1.1** (current): library UI, layer view, 3-axis mirror, three matching modes, red highlights for blocks to clear, mod config file, `.fw restore`, combined cancel hotkey.
- **v0.2.0 candidate** (pick one): Phase 4 chisel voxel overlay OR Phase 3b in-game handbook tab. Phase 4 is the unique differentiator; Phase 3b is the more visible polish.
- **Smaller backlog**: localization scaffold, modicon.png replacing the ModDB default, multiple named active ghosts simultaneously.

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
