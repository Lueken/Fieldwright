A client-side blueprint and build-along tool. Copy any region with an anchor corner, save it as a JSON file, then later paste a translucent ghost that snaps to your crosshair and auto-rotates to face you. Rebuild block-by-block in survival or creative with a live materials checklist, red highlights on blocks that need to be cleared, and three matching modes for type fidelity. Pure client-side. Works on any server that whitelists the mod.

* * *

## What Fieldwright does

- **Translucent ghost preview.** Saved blueprints render as a see-through 3D copy in the world. Only you see it. Other players see only the real blocks you eventually place through normal channels.
- **Snap-to-look placement with auto-rotation.** While the ghost is floating, it follows your crosshair and rotates so the anchor's saved player-facing side points at you. No manual rotation keys needed for most placements.
- **3-axis mirror toggle.** `Ctrl+Shift+M` cycles the ghost through X, Y, Z mirrors. The mirror composes correctly with the auto-rotation.
- **Schematic library UI.** `Ctrl+Shift+K` opens a browser listing every saved blueprint with size, block count, anchor face, and modified date. Per-row Paste and Delete with a two-step confirm step. Pagination over scroll.
- **Layer-by-layer view.** Once the ghost is placed, `PgDn` peels the top layer off and `PgUp` restores it. Use it to see interior layers of tall builds while constructing.
- **Three matching modes.** Loose (any cobble for any cobble, default), Medium (variant-aware: type matters, orientation doesn't), Strict (exact block code). Configurable per-paste or globally via the library dropdown.
- **Live materials checklist.** Top-left HUD shows materials needed grouped by clean block names, what's currently in your hotbar + backpack, and a real-time progress bar. Updates every place / break.
- **Red highlight on blocks to clear.** Cells that should be empty (or that hold the wrong block for the cell) get a translucent red overlay so you can locate them in 3D instead of guessing from a count.
- **Auto-completion.** When the structure fully matches, the ghost and HUD auto-dismiss after about 5 seconds with a sound cue.
- **Overwrite protection with rolling backup.** Save twice over the same name? The previous version is preserved as a `.bak.json`. Restore it with `.fw restore <name>`. Run restore twice to swap back.
- **Mod config file.** `%APPDATA%/VintagestoryData/ModConfig/Fieldwright.json` lets you tune ghost alpha, render distance, HUD position, auto-dismiss timeout, default matching mode.

* * *

## A typical session

1. Stand at the corner of a structure you want to copy. Aim at the corner block. The face you target becomes the saved "front" for later auto-rotation.
2. Press `Ctrl+Shift+B` to set corner 1 (also the anchor).
3. Walk to the opposite top corner. Aim at it. Press `Ctrl+Shift+N` to set corner 2. A green translucent box marks the selection.
4. Run `.fw save my-house`. A JSON file lands in `%APPDATA%/VintagestoryData/Blueprints/`.
5. Travel anywhere. Press `Ctrl+Shift+K` to open the library. Click Paste on `my-house`.
6. A translucent ghost appears, tracking your crosshair. Walk around. It stays aligned.
7. Aim at a block face where you want to build. The ghost snaps and rotates so its saved front faces you.
8. Optional: press `Ctrl+Shift+M` to mirror, or use `PgDn` / `PgUp` to peel layers off the top.
9. Press `Ctrl+Shift+P` to lock placement. The checklist HUD opens.
10. Build. Each correct block clears its ghost cell and decrements the materials list. Wrong blocks and stray air-violations light up red.
11. When everything matches, the ghost auto-dismisses.

* * *

## Controls

| Hotkey | Action |
|---|---|
| `Ctrl+Shift+B` | Set corner 1 (placement anchor + face) |
| `Ctrl+Shift+N` | Set corner 2 |
| `Ctrl+Shift+P` | Place / unplace the active ghost |
| `Ctrl+Shift+M` | Cycle ghost mirror axis (None / X / Y / Z) |
| `Ctrl+Shift+X` | Cancel: dismiss active ghost + clear selection |
| `Ctrl+Shift+L` | Toggle the build checklist HUD |
| `Ctrl+Shift+K` | Open the blueprint library |
| `PgUp` / `PgDn` | Restore / peel layers off the top of the ghost |

All hotkeys are rebindable under **Settings → Controls → Mod controls** → "Fieldwright". A reference card is also available from the **Hotkeys** button in the library footer.

### Chat commands

Most have a hotkey equivalent. Useful command-only entries:

- `.fw grow <up|down|north|south|east|west> [n]`: extend the selection on one face
- `.fw shrink <up|down|north|south|east|west> [n]`: contract one face
- `.fw save <name> [overwrite]`: save the current selection. `overwrite` required to replace an existing file (a rolling backup is created).
- `.fw paste <name> [loose|medium|strict]`: load a blueprint with an optional matching mode override.
- `.fw restore <name>`: swap a blueprint with its rolling backup. Reversible.
- `.fw list`: print all saved blueprints to chat
- `.fw status`: show current selection size and anchor

* * *

## Matching modes

The mod ships with three levels of block fidelity, controllable per-paste or globally:

- **Loose** (default). `block.FirstCodePart()` only. Andesite cobble, granite cobble, basalt cobble all count as "cobble". Oak logs, pine logs, redwood logs all count as "log". Survival-friendly when you can't source the exact creative materials.
- **Medium**. VS's variant system, but orientation variants stripped (rotation, side, facing). Andesite cobble stays distinct from granite cobble. Oak log stays distinct from pine log. But all rotations of slanted thatch still group together (you can't directly control orientation anyway; the engine picks it at placement).
- **Strict**. Full block code including every variant. Practically unusable because the engine auto-orients blocks at placement, but exposed for completeness.

The library dropdown saves your choice to `Fieldwright.json` so it persists across sessions. Whatever you pick is also the default when you use `.fw paste <name>` without a mode argument.

* * *

## Server compatibility

Fieldwright is fully client-side. **No network packets sent or received. No world mutations. Other players cannot see your ghost, HUD, or selection box.** Real blocks are placed through Vintage Story's normal channels and validated by the server.

It works on any vanilla server out of the box. **Some servers run a strict mod allowlist** that rejects unknown client mods at connection time, even read-only ones. If you can't connect with Fieldwright installed, ask the server admin:

> Could you add `fieldwright` to the server's allowed-mods list? It's a client-side blueprint-preview tool. No network packets, no world changes. Same risk profile as a minimap mod.

* * *

## What's new in v0.1.1

- Schematic library UI (`Ctrl+Shift+K`) with paste, delete, two-step confirm, matching-mode dropdown that persists to disk
- 3-axis mirror toggle (`Ctrl+Shift+M`)
- Layer-by-layer view (`PgUp` / `PgDn`)
- Three matching modes (Loose / Medium / Strict) with per-paste override
- Red highlight on cells that need to be cleared (air-violations + wrong-block cells)
- Mod config file with knobs for ghost alpha, render distance, HUD position, auto-dismiss timeout, default matching mode
- `.fw restore <name>` command for reversible backup swaps
- Combined `Ctrl+Shift+X` cancel (dismiss ghost + clear selection)
- Hotkey reference modal accessible from the library footer
- Cleaner HUD labels independent of matching mode

* * *

## Roadmap

- **v0.2.0 headline: Phase 4 chisel voxel overlay**. Per-voxel red/green/yellow hints for chiseled positions after base blocks match. Also fixes block-variant rotation (stairs, logs, doors will rotate correctly with the ghost). No other VS blueprint mod does per-voxel ghost annotation.
- **Smaller backlog**: localization scaffold, modicon.png, multiple named active ghosts simultaneously.

* * *

## FAQ

**Will it work on a server I don't own?**
Yes, as long as the server's mod allowlist (if any) includes `fieldwright`. The mod sends nothing across the network and changes nothing in the world.

**Can other players see my ghost?**
No. The ghost is a client-side render with no entity, no block, and no packet. Only you see it.

**What happens if I save a blueprint on a modded world and paste it somewhere the modded blocks don't exist?**
Fieldwright will load the blueprint, log a warning about the missing block codes, and skip those cells in the ghost. You won't be able to complete the build, but the ghost won't crash. The schematic file itself remains intact.

**Does this place blocks for me?**
No. You still place every block by hand through Vintage Story's normal channels. Fieldwright is a memory and visual-guidance tool, not an auto-builder. Survival ethos stays intact.

**The HUD says "cobble: 5 / 5" but the ghost won't complete. Why?**
You're probably in Medium or Strict matching mode and your cobble is the wrong rock type. Hover over an unmatched red cell to see what's expected, or run `.fw paste <name> loose` for the v0.1.0 family-grouping behavior.

**How do I uninstall?**
Delete `fieldwright` (folder or zip) from `%APPDATA%/VintagestoryData/Mods/`. Your blueprints and config stay where they are. Both are user-owned and outside the mod scope.

* * *

## Source, issues, support

- **GitHub**: <https://github.com/Lueken/Fieldwright>
- **Issues / feature requests**: <https://github.com/Lueken/Fieldwright/issues>
- **License**: MIT (code). Blueprint files saved by users are user-owned.
- **Author**: Venah (`Lueken` on GitHub).

Built for Vintage Story 1.20+. Not affiliated with or endorsed by Anego Studios. Vintage Story is © Anego Studios.
