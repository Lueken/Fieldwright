# Changelog

All notable changes to this project will be documented in this file. Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.5] - 2026-05-29

Follow-up to v0.1.4 covering a remote-server-only case that solo testing missed. v0.1.4 was briefly published to ModDB and then retracted when paste failed on real multiplayer servers despite working fine in solo. v0.1.5 ships the same v0.1.4 surface area plus the remote-server fix.

### Fixed
- **Paste no longer NREs on real multiplayer servers.** `BlockSchematic.Remap` iterates `BlockSchematic.BlockRemaps` and `ItemRemaps` at lines 186 / 201 — both public static auto-properties the game populates from world config. On a remote-server-connected client neither is ever set, so the foreach NRE'd; solo worked because the integrated server populated them. `EnsureSchematicCollectionsInitialized` now null-coalesces both static dictionaries to empty before the schematic gets `Init`ed. Confirmed by reading the open-source `vsapi` `BlockSchematic.cs`.

[0.1.5]: https://github.com/Lueken/Fieldwright/releases/tag/v0.1.5

## [0.1.4] - 2026-05-29

Hotfix for the residual load NRE Fish reported on Discord after v0.1.3 shipped. Originally tagged v0.1.3a; renamed to v0.1.4 because ModDB's modinfo parser rejects letter-suffixed semver strings. Briefly uploaded to ModDB before paste failures on real multiplayer servers were spotted; superseded by v0.1.5 within hours.

### Fixed
- **Paste no longer NREs in `BlockSchematic.Remap` line 186 for older blueprints with missing collection fields.** The v0.1.3 patch null-coalesced six BlockSchematic collections (`Entities`, `BlockEntities`, `ItemCodes`, `DecorIds`, `EntitiesUnpacked`, `BlockEntitiesUnpacked`), but missed eight others (`DecorIndices`, `BlocksUnpacked`, `FluidsLayerUnpacked`, `DecorsUnpacked`, `Connectors`, `PathwaySides`, `PathwayStarts`, `PathwayOffsets`, `UndergroundCheckPositions`, `AbovegroundCheckPositions`). v0.1.0-era blueprint JSON omits several of these, leaving them null on load. `EnsureSchematicCollectionsInitialized` now uses reflection to walk every public collection-typed field on `BlockSchematic` and null-coalesces all of them, so any present or future field is covered automatically.

### Changed
- **Stack traces no longer leak the build machine's source paths.** Added `<PathMap>` to the csproj so absolute developer paths in PDB symbols get rewritten to `./` at compile time. End users still get file names and line numbers in crash reports, but the embedded path is now `./FieldwrightModSystem.cs` instead of the developer's local folder layout. Triggered after Fish read `C:\Users\<devname>\...` in her stack trace and reasonably wondered whether the mod was reaching across the network.

[0.1.4]: https://github.com/Lueken/Fieldwright/releases/tag/v0.1.4

## [0.1.3] - 2026-05-25

The "ghost shows the real thing" release. Chests, crates, baskets, generic containers, and chiseled blocks now render with their actual block-entity geometry instead of mystery placeholders. The build checklist gets a movable, scrollable, copyable dialog mode in addition to the existing always-on HUD.

### Added
- **BE-aware ghost rendering.** New per-family mesh sources (`BEMeshSources.cs`) read each cell's saved block-entity tree at paste time and reconstruct the real mesh:
  - **Chests / labeled chests / querns / beds**: tessellated normally + rotated by their saved `meshAngle`, so askew chests show askew in the ghost.
  - **Crates / stationary baskets / generic containers**: a throwaway BlockEntity is spun up from the schematic's BE tree and its private mesh-builder (`GenMesh(tesselator)` or `loadOrCreateMesh()`) is invoked via reflection to extract the geometry. No more empty placeholders for BE-driven mesh families. MethodInfo lookups are cached per BE type.
  - **Chiseled blocks / microblocks**: render the substrate the chisel was carved from (oak plank, granite, etc.) as a full cube. Voxel-level chisel detail still deferred to v0.2's chisel phase.
- **Substrate-aware materials list.** Chiseled cells contribute to their substrate's row in the build checklist (`planks: 0/12` includes chiseled-from-plank cells) rather than showing as a separate `chiseledblock` line. Placing the substrate counts the cell as matched. Voxel-comparison matching is the v0.2 chisel phase.
- **Movable / scrollable / copyable checklist dialog.** Ctrl+Shift+L now cycles three view states: HUD (transparent, always-on, no cursor grab) → Modal (draggable title bar, clipped scroll body, "Copy to clipboard" button) → Hidden → HUD. The HUD opens automatically on `.fw place`; the modal is opt-in via the cycle.

### Fixed
- **Paste no longer crashes with `NullReferenceException` on blueprints saved by v0.1.2's block-only fallback path.** Reported by Fish and 3CHØ on Discord. `BlockSchematic.LoadFromString` leaves `Entities` / `BlockEntities` / `ItemCodes` / `DecorIds` collections as null when the serialized JSON omitted them (which v0.1.2's `CaptureBlocksOnly` did), and `BlockSchematic.Init -> Remap` then iterates them without null-checks. `BlueprintFile.ToBlockSchematic` now calls `EnsureSchematicCollectionsInitialized` immediately after `LoadFromString` so the collections are always present before `Init` runs. Affects loads from single-player into a server, library paste, and the `.fw paste` chat command alike.

### Notes
- Crate / basket meshes don't have the in-world label / contents detail since we don't reconstruct items inside containers; mesh geometry + rotation only.
- Reflection-based BE instantiation is best-effort. If a third-party mod's BE class throws inside Initialize without populating its mesh field, that cell falls back to bare tessellation (and likely an empty mesh). Warnings go to client-main.log under `[fieldwright:be-mesh]`.
- Phase 1 build counts treat chiseled cells as their substrate. Players who care about chisel detail today should keep an eye on the source structure; the build-vs-chisel separation lands with v0.2.

[0.1.3]: https://github.com/Lueken/Fieldwright/releases/tag/v0.1.3

## [0.1.2] - 2026-05-23

Hotfix for a save crash reported by multiple users on the ModDB page (SimonBBallin, Sir_Capon).

### Fixed
- **Save no longer crashes with `NullReferenceException` when the selection contains entities.** The vanilla `BlockSchematic.AddArea` captures every entity in the region (mobs, dropped items, falling blocks, projectiles) and runs `Entity.OnStoreCollectibleMappings` on each, which NREs for any entity with a null collectible reference. New `BlueprintStore.CaptureBlocksOnly` method builds the schematic from blocks only, bypassing `AddArea` entirely. Block entities (chest contents, sign text, chisel voxel data) are also skipped for now; they aren't read by any v0.1.x feature. Per-cell block-entity capture with proper try/catch returns when Phase 4 chisel overlay needs it.

[0.1.2]: https://github.com/Lueken/Fieldwright/releases/tag/v0.1.2

## [0.1.1] - 2026-05-20

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
