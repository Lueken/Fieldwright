# Changelog

All notable changes to this project will be documented in this file. Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.1] — 2026-05-20

The "polish + power-user" release. Adds the schematic library UI, three matching modes, a 3-axis mirror, layer-by-layer view, in-world red highlights for blocks that need to be cleared, plus a mod config file with user-tunable knobs.

### Added
- **Mod config file** at `%APPDATA%/VintagestoryData/ModConfig/Fieldwright.json`. Auto-created with defaults on first load. Knobs: `DefaultMatchingMode`, `GhostAlpha`, `RenderDistanceBlocks`, `AutoDismissMs`, `HudOffsetX`, `HudOffsetY`.
- **MatchingMode toggle (Loose / Medium / Strict)**. Loose stays the default (any cobble counts as any cobble), matching v0.1.0 behavior. Medium uses VS's Variant API to strip orientation variants only (rotation, facing, side) while preserving rock type / wood type / condition. Strict requires the exact block code including every variant. Per-paste override via `.fw paste my-house medium`. The library UI dropdown saves the chosen default back to disk.
- **Schematic library UI** (`Ctrl+Shift+K` or `.fw library`). Native GuiDialog listing saved blueprints with size, block count, anchor face, modified date, and "has backup" badge. Per-row Paste and Delete buttons. Two-step delete confirmation with red "Confirm?" text. Pagination over scroll. Matching-mode dropdown at the top.
- **Hotkey reference modal** accessible from the library footer.
- **Layer-by-layer view**. `PgDn` peels the top layer off the placed ghost, `PgUp` restores. Useful for tall builds and seeing interior layers while constructing.
- **3-axis mirror toggle** (`Ctrl+Shift+M` or `.fw mirror`). Cycles None → X → Y → Z → None. Applied in the anchor-block-centered local frame so it composes correctly with player-yaw rotation. Tracker rebuilds expected positions when the ghost is already placed.
- **`.fw restore <name>` command**. Swaps a blueprint with its rolling backup (reversible by running again). Handles the no-backup and main-file-missing cases gracefully.
- **`Ctrl+Shift+X` cancel hotkey**. Dismisses any active ghost AND clears the selection box in one stroke. `.fw cancel` now does the same combined operation. `.fw clear` stays selection-only for granularity.
- **Red highlight on cells that need to be cleared**. Both air-violation cells (should-be-empty but isn't) and wrong-block cells (expected block X, got block Y) render with a translucent red overlay, so the player can locate them in 3D rather than guess from a count.

### Changed
- **HUD labels now stay clean regardless of matching mode**. Display always uses `FirstCodePart` (`"cobble"`, `"log"`, `"slantedroofing"`); the variant detail used for matching lives only in the backend. Inventory counts remain truthful per mode.
- **"Blocks to remove" count** now sums both air-violation and wrong-block cells.

### Notes
- Block variants still don't rotate with the ghost mesh; that lands with Phase 4 schematic-transform rotation in v0.2.x.
- Chiseled blocks still render as their default shape and auto-match any chiseled block at the cell. Per-voxel comparison is Phase 4.

[0.1.1]: https://github.com/Lueken/Fieldwright/releases/tag/v0.1.1

## [0.1.0] — 2026-05-18

Initial public release. Phases 1, 2a, 2b, and 3a from the development brief.

### Added
- Two-corner region selection with anchor-aware corner 1. Corner 1 doubles as the placement reference for paste-time auto-rotation.
- Hotkeys: `Ctrl+Shift+B` (corner 1 + anchor), `Ctrl+Shift+N` (corner 2), `Ctrl+Shift+P` (toggle place / unlock ghost), `Ctrl+Shift+L` (toggle checklist HUD).
- Chat commands: `.fw corner1`, `.fw corner2`, `.fw grow <dir> [n]`, `.fw shrink <dir> [n]`, `.fw status`, `.fw clear`, `.fw save <name> [overwrite]`, `.fw paste <name>`, `.fw place`, `.fw cancel`, `.fw list`.
- Land-claim-style live preview: translucent green box shows the active selection. Anchor block has a brighter accent. Per-cell painting up to 65,536 cells, with face-only fallback above that.
- Blueprint file format: JSON wrapper around `BlockSchematic` with `anchorOffset` and `anchorFace`. Compatible with bare `BlockSchematic` files from WorldEdit / BetterRuins.
- Save protection: refuses to overwrite without explicit `overwrite` keyword; one rolling `.bak.json` backup per name.
- Floating-ghost paste mode: translucent 3D mesh that snaps to the look-target each frame and auto-rotates so the saved front face points at the player. Works with horizontal and vertical look-targets via player-yaw fallback.
- Place toggle: `Ctrl+Shift+P` locks the ghost in place and opens the build-along checklist. Pressing again unlocks for repositioning.
- Match tracking: subscribes to `BlockChanged` events, compares placed blocks against expected cells using group keys (`block.FirstCodePart()`) so variant-rich blocks (slanted roofing, hay bales) match any orientation.
- Checklist HUD: top-left, transparent, color-coded materials list with hotbar + backpack inventory counts. Air-position violations shown separately.
- Auto-dismiss: ghost and HUD clear automatically about 5.5 seconds after the structure fully matches. Re-arms if a matched block is broken.
- Multi-block-aware: secondary cells of beds, tall doors, double-tall plants (anything coded `multiblock`) are excluded from the materials list and air-violation set since the engine fills them when the primary block is placed.

### Known limitations
- Strict per-variant matching was relaxed to group-key matching to handle blocks the engine auto-orients on placement. Oak and pine logs both group as "log" today.
- Chiseled blocks render as their default shape (no per-voxel ghost) and can't be strict-matched. The build will auto-complete with any chiseled block at those positions.
- No in-game handbook tab yet for browsing saved blueprints. Use `.fw list` for now.
- Rotation pivot is the anchor cell's center. Mesh rotation is render-time only; block variants don't rotate with the ghost (they will when Phase 4 lands proper schematic rotation).

[0.1.0]: https://github.com/Lueken/Fieldwright/releases/tag/v0.1.0
