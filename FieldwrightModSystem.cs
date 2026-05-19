using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

[assembly: ModInfo("Fieldwright", "fieldwright",
    Authors = new string[] { "Venah" },
    Description = "A surveyor's pocket aid for builders. Client-side personal blueprint mod with anchor-snap paste.",
    Version = "0.1.0-dev")]

namespace Fieldwright;

public class FieldwrightModSystem : ModSystem
{
    private const string Component = "mod-system";
    private const string HotkeyCorner1 = "fieldwright-corner1";
    private const string HotkeyCorner2 = "fieldwright-corner2";
    private const string HotkeyPlace = "fieldwright-place";

    // HighlightBlocks slot IDs. Arbitrary but must be stable per mod so we can clear them.
    private const int HighlightSlotBox = 8501;
    private const int HighlightSlotAnchor = 8502;

    private SelectionState selection = new SelectionState();
    private ICoreClientAPI? capi;

    // Active paste state — null when nothing is being pasted.
    private GhostMesh? activeGhostMesh;
    private GhostRenderer? activeGhostRenderer;
    private string? activeGhostName;
    private BlockSchematic? activeSchematic;
    private Vec3i activeAnchorOffset = new Vec3i(0, 0, 0);

    // Phase 3a: match tracker + HUD created on .fw place, disposed on cancel / unlock.
    private GhostMatchTracker? activeMatchTracker;
    private GhostChecklistDialog? activeChecklistDialog;

    // Auto-dismiss timer: ghost + HUD auto-clear N ms after the structure first
    // becomes fully matched. Re-arms if the player breaks a block (uncompleting it).
    // Phase 4 will swap this for a chisel-pass toggle so users can keep the base
    // ghost visible while annotating chisel positions.
    private const int CompletionAutoDismissMs = 5500;
    private long completionTickListenerId = -1;
    private long completionDetectedAtMs = 0;
    private bool completionDismissNotified = false;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        FieldwrightLogger.Info(api, Component, "loading Fieldwright v0.1.0 (Phase 1: foundation)");

        RegisterHotkeys(api);
        RegisterCommands(api);

        FieldwrightLogger.Info(api, Component,
            $"ready — blueprints directory: {BlueprintStore.GetBlueprintsDirectory(api)}");
    }

    private void RegisterHotkeys(ICoreClientAPI api)
    {
        api.Input.RegisterHotKey(HotkeyCorner1, "Fieldwright: Set corner 1 (anchor)",
            GlKeys.B, HotkeyType.CharacterControls, ctrlPressed: true, shiftPressed: true);
        api.Input.SetHotKeyHandler(HotkeyCorner1, _ => HandleCornerHotkey(corner: 1));

        api.Input.RegisterHotKey(HotkeyCorner2, "Fieldwright: Set corner 2",
            GlKeys.N, HotkeyType.CharacterControls, ctrlPressed: true, shiftPressed: true);
        api.Input.SetHotKeyHandler(HotkeyCorner2, _ => HandleCornerHotkey(corner: 2));

        api.Input.RegisterHotKey(HotkeyPlace, "Fieldwright: Place active ghost",
            GlKeys.P, HotkeyType.CharacterControls, ctrlPressed: true, shiftPressed: true);
        api.Input.SetHotKeyHandler(HotkeyPlace, _ => { ToggleGhostPlacement(); return true; });

        // Checklist HUD toggle — manual open/close to avoid the dialog system's
        // interactive-toggle path (which grabs the cursor).
        api.Input.RegisterHotKey("fieldwright-checklist-toggle", "Fieldwright: Toggle build checklist HUD",
            GlKeys.L, HotkeyType.CharacterControls, ctrlPressed: true, shiftPressed: true);
        api.Input.SetHotKeyHandler("fieldwright-checklist-toggle", _ => { ToggleChecklistHud(); return true; });

        FieldwrightLogger.Info(api, Component,
            "registered hotkeys Ctrl+Shift+B (corner1+anchor), Ctrl+Shift+N (corner2), Ctrl+Shift+P (place)");
    }

    private bool HandleCornerHotkey(int corner)
    {
        if (capi == null) return false;

        var sel = capi.World.Player?.CurrentBlockSelection;
        if (sel?.Position == null)
        {
            capi.ShowChatMessage($"[Fieldwright] No block under crosshair — look at a block to set corner {corner}.");
            return true;
        }

        SetCorner(corner, sel.Position, sel.Face);
        return true;
    }

    private void SetCorner(int corner, BlockPos pos, BlockFacing? face)
    {
        if (capi == null) return;

        if (corner == 1)
        {
            selection.SetCorner1(pos, face);
            var faceNote = face == null
                ? string.Empty
                : (face.IsHorizontal
                    ? $" (facing {face.Code} — auto-rotate on paste will align this face)"
                    : $" (facing {face.Code} — vertical face captured; auto-rotate disabled)");
            capi.ShowChatMessage($"[Fieldwright] Corner 1 (anchor) set at {FormatPos(pos)}.{faceNote}");
        }
        else
        {
            selection.SetCorner2(pos);
            capi.ShowChatMessage($"[Fieldwright] Corner 2 set at {FormatPos(pos)}.");
        }

        UpdateHighlight();
        EchoSelectionSize();

        FieldwrightLogger.Debug(capi, Component, $"corner{corner} = {FormatPos(pos)}, face={face?.Code ?? "none"}");
    }

    private void EchoSelectionSize()
    {
        if (capi == null || !selection.HasSelection) return;

        var (dx, dy, dz) = selection.GetDimensions();
        var heightHint = dy == 1
            ? "  (Only 1 block tall — set corner2 at the opposite TOP corner, or grow up with .fw grow up <n>.)"
            : string.Empty;
        capi.ShowChatMessage(
            $"[Fieldwright] Selection: {dx} wide × {dy} tall × {dz} deep = {dx * dy * dz} cells. " +
            $"Anchor at {FormatPos(selection.Anchor!)}.{heightHint}");
    }

    private const int MaxHighlightCells = 65536;

    private void UpdateHighlight()
    {
        if (capi == null) return;

        if (!selection.HasSelection)
        {
            ClearHighlight();
            return;
        }

        var min = selection.Min!;
        var max = selection.Max!;
        var anchor = selection.Anchor!;

        int width = max.X - min.X + 1;
        int height = max.Y - min.Y + 1;
        int depth = max.Z - min.Z + 1;
        long total = (long)width * height * depth;

        if (total > MaxHighlightCells)
        {
            // Too many cells to paint individually without lagging the engine.
            // Fall back to face-only highlight: only the 6 outer faces of the cuboid.
            HighlightFacesOnly(min, max, anchor);
            FieldwrightLogger.Debug(capi, Component,
                $"selection {width}×{height}×{depth}={total} > {MaxHighlightCells} — using face-only highlight");
            return;
        }

        // Paint every cell in the cuboid translucent green. Anchor block gets a brighter
        // tint so the placement reference stays visible. One slot for everything so
        // the engine only re-uploads one mesh per change.
        var positions = new List<BlockPos>((int)total);
        var colors = new List<int>((int)total);
        int boxColor = ColorUtil.ColorFromRgba(80, 200, 100, 60);     // R, G, B, A
        int anchorColor = ColorUtil.ColorFromRgba(50, 255, 220, 200); // anchor accent

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    var p = new BlockPos(x, y, z, min.dimension);
                    positions.Add(p);
                    colors.Add(p.X == anchor.X && p.Y == anchor.Y && p.Z == anchor.Z ? anchorColor : boxColor);
                }
            }
        }

        capi.World.HighlightBlocks(
            capi.World.Player, HighlightSlotBox,
            positions, colors,
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);

        // HighlightSlotAnchor unused in fill mode; clear in case it was set previously.
        capi.World.HighlightBlocks(capi.World.Player, HighlightSlotAnchor,
            new List<BlockPos>(),
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
    }

    private void HighlightFacesOnly(BlockPos min, BlockPos max, BlockPos anchor)
    {
        if (capi == null) return;

        var positions = new List<BlockPos>();
        var colors = new List<int>();
        int boxColor = ColorUtil.ColorFromRgba(80, 200, 100, 90);
        int anchorColor = ColorUtil.ColorFromRgba(50, 255, 220, 200);

        // Add every cell on the 6 outer faces of the cuboid (with edge dedup).
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    bool onFace =
                        x == min.X || x == max.X ||
                        y == min.Y || y == max.Y ||
                        z == min.Z || z == max.Z;
                    if (!onFace) continue;

                    var p = new BlockPos(x, y, z, min.dimension);
                    positions.Add(p);
                    colors.Add(p.X == anchor.X && p.Y == anchor.Y && p.Z == anchor.Z ? anchorColor : boxColor);
                }
            }
        }

        capi.World.HighlightBlocks(
            capi.World.Player, HighlightSlotBox,
            positions, colors,
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);

        capi.World.HighlightBlocks(capi.World.Player, HighlightSlotAnchor,
            new List<BlockPos>(),
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
    }

    private void ClearHighlight()
    {
        if (capi == null) return;
        var empty = new List<BlockPos>();
        capi.World.HighlightBlocks(capi.World.Player, HighlightSlotBox, empty,
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        capi.World.HighlightBlocks(capi.World.Player, HighlightSlotAnchor, empty,
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
    }

    private void RegisterCommands(ICoreClientAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        var root = api.ChatCommands.Create("fw")
            .WithDesc("Fieldwright — personal blueprint commands");

        root.BeginSub("corner1")
            .WithDesc("Set corner 1 (the placement anchor) at the block under your crosshair")
            .HandleWith(_ => { HandleCornerHotkey(corner: 1); return TextCommandResult.Success(); })
            .EndSub();

        root.BeginSub("corner2")
            .WithDesc("Set corner 2 at the block under your crosshair")
            .HandleWith(_ => { HandleCornerHotkey(corner: 2); return TextCommandResult.Success(); })
            .EndSub();

        root.BeginSub("grow")
            .WithDesc("Expand the selection on one face by n blocks (default 1). Direction: up|down|north|south|east|west")
            .WithArgs(parsers.Word("direction"), parsers.OptionalInt("amount", 1))
            .HandleWith(args => OnCmdGrowOrShrink(args, grow: true))
            .EndSub();

        root.BeginSub("shrink")
            .WithDesc("Contract the selection on one face by n blocks (default 1). Direction: up|down|north|south|east|west")
            .WithArgs(parsers.Word("direction"), parsers.OptionalInt("amount", 1))
            .HandleWith(args => OnCmdGrowOrShrink(args, grow: false))
            .EndSub();

        root.BeginSub("status")
            .WithDesc("Show current selection size and anchor position")
            .HandleWith(OnCmdStatus)
            .EndSub();

        root.BeginSub("clear")
            .WithDesc("Clear the current selection")
            .HandleWith(OnCmdClear)
            .EndSub();

        root.BeginSub("save")
            .WithDesc("Save the current selection as a blueprint file. Pass 'overwrite' as second arg to replace an existing file.")
            .WithArgs(parsers.Word("name"), parsers.OptionalWord("overwrite"))
            .HandleWith(OnCmdSave)
            .EndSub();

        root.BeginSub("paste")
            .WithDesc("Load a blueprint and show its translucent ghost at your current position")
            .WithArgs(parsers.Word("name"))
            .HandleWith(OnCmdPaste)
            .EndSub();

        root.BeginSub("place")
            .WithDesc("Toggle the active ghost between placed (locked) and floating (follows crosshair)")
            .HandleWith(_ => { ToggleGhostPlacement(); return TextCommandResult.Success(); })
            .EndSub();

        root.BeginSub("cancel")
            .WithDesc("Dismiss the active ghost (does not affect saved files)")
            .HandleWith(OnCmdCancel)
            .EndSub();

        root.BeginSub("list")
            .WithDesc("List saved blueprints")
            .HandleWith(OnCmdList)
            .EndSub();

        FieldwrightLogger.Info(api, Component,
            "registered .fw commands: corner1, corner2, grow, shrink, status, clear, save, paste, place, cancel, list");
    }

    private TextCommandResult OnCmdGrowOrShrink(TextCommandCallingArgs args, bool grow)
    {
        if (capi == null) return TextCommandResult.Error("Client API not available.");

        if (!selection.HasSelection)
        {
            return TextCommandResult.Error("[Fieldwright] No selection. Set corner 1 first.");
        }

        var dirString = ((string)args[0]).ToLowerInvariant();
        var face = BlockFacing.FromCode(dirString);
        if (face == null)
        {
            return TextCommandResult.Error($"[Fieldwright] Unknown direction '{dirString}'. Use up|down|north|south|east|west.");
        }

        int amount = (int)args[1];
        if (amount <= 0)
        {
            return TextCommandResult.Error("[Fieldwright] Amount must be at least 1.");
        }

        if (grow) selection.Grow(face, amount);
        else selection.Shrink(face, amount);

        UpdateHighlight();

        var (dx, dy, dz) = selection.GetDimensions();
        var verb = grow ? "grew" : "shrunk";
        return TextCommandResult.Success(
            $"[Fieldwright] {verb} {dirString} by {amount}. Selection now: {dx}×{dy}×{dz} = {dx * dy * dz} cells.");
    }

    private TextCommandResult OnCmdStatus(TextCommandCallingArgs args)
    {
        if (!selection.HasSelection)
        {
            return TextCommandResult.Success("[Fieldwright] No active selection.");
        }

        var (dx, dy, dz) = selection.GetDimensions();
        return TextCommandResult.Success(
            $"[Fieldwright] Selection: {dx} wide × {dy} tall × {dz} deep = {dx * dy * dz} cells. " +
            $"Anchor at {FormatPos(selection.Anchor!)}. Min {FormatPos(selection.Min!)}, Max {FormatPos(selection.Max!)}.");
    }

    private TextCommandResult OnCmdClear(TextCommandCallingArgs args)
    {
        selection.Clear();
        ClearHighlight();
        return TextCommandResult.Success("[Fieldwright] Selection cleared.");
    }

    private TextCommandResult OnCmdSave(TextCommandCallingArgs args)
    {
        if (capi == null) return TextCommandResult.Error("Client API not available.");

        if (!selection.HasSelection)
        {
            return TextCommandResult.Error("[Fieldwright] Set corner 1 first (Ctrl+Shift+B or .fw corner1).");
        }

        var name = (string)args[0];
        var overwriteArg = (args[1] as string)?.Trim().ToLowerInvariant();
        bool overwriteRequested = overwriteArg == "overwrite";

        bool willOverwrite = BlueprintStore.Exists(capi, name);
        if (willOverwrite && !overwriteRequested)
        {
            return TextCommandResult.Error(
                $"[Fieldwright] A blueprint named '{name}' already exists. " +
                $"Run `.fw save {name} overwrite` to replace it, or pick a different name.");
        }

        try
        {
            var min = selection.Min!;
            var max = selection.Max!;
            var anchorOffset = selection.GetAnchorOffsetFromMin();

            // BlockSchematic.AddArea uses a strict-less-than loop on the end coord,
            // so we have to pass max + (1,1,1) to capture the user's chosen max
            // block inclusively. Without this shift, single-Y selections capture 0
            // blocks because the Y loop runs zero times.
            var endExclusive = new BlockPos(max.X + 1, max.Y + 1, max.Z + 1, max.dimension);

            var schematic = new BlockSchematic();
            schematic.AddArea(capi.World, min, endExclusive);
            schematic.Pack(capi.World, min);

            // Auto-backup the existing file before overwrite — single rolling backup
            // at {name}.bak.json so one accidental overwrite is recoverable.
            string? backupPath = null;
            if (willOverwrite)
            {
                backupPath = BlueprintStore.BackupExisting(capi, name);
            }

            var blueprint = BlueprintFile.Wrap(schematic, anchorOffset, selection.AnchorFace);
            BlueprintStore.Save(capi, name, blueprint);

            var (dx, dy, dz) = selection.GetDimensions();
            int totalBlocks = schematic.Indices?.Count ?? 0;

            // Auto-clear selection + highlight after a successful save so the
            // green overlay doesn't linger over the source structure.
            selection.Clear();
            ClearHighlight();

            var overwriteNote = backupPath != null
                ? $" (overwrote — previous version backed up to {System.IO.Path.GetFileName(backupPath)})"
                : string.Empty;
            return TextCommandResult.Success(
                $"[Fieldwright] Saved '{name}' — {dx}×{dy}×{dz} bounds, {totalBlocks} non-air block positions, anchor offset ({anchorOffset.X},{anchorOffset.Y},{anchorOffset.Z}).{overwriteNote} Selection cleared.");
        }
        catch (System.Exception ex)
        {
            FieldwrightLogger.Error(capi, Component, $"save failed: {ex}");
            return TextCommandResult.Error($"[Fieldwright] Save failed: {ex.Message}");
        }
    }

    private TextCommandResult OnCmdPaste(TextCommandCallingArgs args)
    {
        if (capi == null) return TextCommandResult.Error("Client API not available.");

        var name = (string)args[0];

        try
        {
            var blueprint = BlueprintStore.Load(capi, name);
            var schematic = blueprint.ToBlockSchematic();

            schematic.Init(capi.World.BlockAccessor);
            schematic.LoadMetaInformationAndValidate(capi.World.BlockAccessor, capi.World, name);

            var anchorOffset = blueprint.AnchorOffsetAsVec3i();
            var savedFace = blueprint.AnchorFacingResolved();

            // Phase 2b: ghost enters floating mode and tracks the player's crosshair.
            // .fw place / Ctrl+Shift+P locks it; .fw cancel dismisses.
            ClearActiveGhost();

            var ghostMesh = new GhostMesh(capi, schematic, anchorOffset);
            if (!ghostMesh.HasMesh)
            {
                ghostMesh.Dispose();
                return TextCommandResult.Error(
                    $"[Fieldwright] '{name}' produced an empty mesh ({ghostMesh.SkippedCount} blocks skipped). Check the log.");
            }

            // Initial origin: current look-target if any, else the block in front of the player.
            var initialOrigin = capi.World.Player?.CurrentBlockSelection?.Position?.Copy()
                ?? capi.World.Player!.Entity.Pos.AsBlockPos;

            activeGhostMesh = ghostMesh;
            activeGhostRenderer = new GhostRenderer(
                capi, ghostMesh, initialOrigin, anchorOffset, savedFace, startFloating: true);
            activeGhostName = name;
            activeSchematic = schematic;
            activeAnchorOffset = anchorOffset;

            FieldwrightLogger.Info(capi, Component,
                $"pasting '{name}': size={schematic.SizeX}×{schematic.SizeY}×{schematic.SizeZ}, " +
                $"meshed={ghostMesh.BlockCount}, skipped={ghostMesh.SkippedCount}, " +
                $"initialOrigin=({initialOrigin.X},{initialOrigin.Y},{initialOrigin.Z}), " +
                $"savedFace={savedFace?.Code ?? "none"}");

            var rotateNote = savedFace != null
                ? "Aim at a block face — ghost rotates to align."
                : "Saved blueprint has no horizontal anchor face; ghost won't auto-rotate.";
            return TextCommandResult.Success(
                $"[Fieldwright] Ghost active (floating): '{name}' — {schematic.SizeX}×{schematic.SizeY}×{schematic.SizeZ}, " +
                $"{ghostMesh.BlockCount} blocks. {rotateNote} .fw place / Ctrl+Shift+P to lock, .fw cancel to dismiss.");
        }
        catch (System.IO.FileNotFoundException)
        {
            return TextCommandResult.Error($"[Fieldwright] No blueprint named '{name}'. Try .fw list.");
        }
        catch (System.Exception ex)
        {
            FieldwrightLogger.Error(capi, Component, $"paste failed: {ex}");
            return TextCommandResult.Error($"[Fieldwright] Load failed: {ex.Message}");
        }
    }

    private TextCommandResult OnCmdCancel(TextCommandCallingArgs args)
    {
        if (activeGhostName == null)
        {
            return TextCommandResult.Success("[Fieldwright] No active ghost to cancel.");
        }

        var prevName = activeGhostName;
        ClearActiveGhost();
        return TextCommandResult.Success($"[Fieldwright] Ghost '{prevName}' dismissed.");
    }

    private void ClearActiveGhost()
    {
        TearDownTracking();

        if (activeGhostRenderer != null)
        {
            activeGhostRenderer.Dispose();
            activeGhostRenderer = null;
        }
        if (activeGhostMesh != null)
        {
            activeGhostMesh.Dispose();
            activeGhostMesh = null;
        }
        activeGhostName = null;
        activeSchematic = null;
        activeAnchorOffset = new Vec3i(0, 0, 0);
    }

    private void TearDownTracking()
    {
        if (capi != null && completionTickListenerId != -1)
        {
            capi.Event.UnregisterGameTickListener(completionTickListenerId);
            completionTickListenerId = -1;
        }
        completionDetectedAtMs = 0;
        completionDismissNotified = false;

        if (activeChecklistDialog != null)
        {
            if (activeChecklistDialog.IsOpened()) activeChecklistDialog.TryClose();
            activeChecklistDialog.Dispose();
            activeChecklistDialog = null;
        }
        if (activeMatchTracker != null)
        {
            activeMatchTracker.Dispose();
            activeMatchTracker = null;
        }
    }

    /// <summary>
    /// Toggle: floating ghost → placed (locks position/rotation + spins up match
    /// tracking + opens HUD), or placed ghost → floating (tears down tracking).
    /// No-op when no ghost is active.
    /// </summary>
    private void ToggleGhostPlacement()
    {
        if (capi == null) return;

        if (activeGhostRenderer == null)
        {
            capi.ShowChatMessage("[Fieldwright] No active ghost.");
            return;
        }

        if (activeGhostRenderer.IsFloating)
        {
            activeGhostRenderer.Place();
            SpinUpTracking();

            var o = activeGhostRenderer.Origin;
            capi.ShowChatMessage(
                $"[Fieldwright] Ghost '{activeGhostName}' placed at ({o.X}, {o.Y}, {o.Z}). " +
                $"Build to match — checklist HUD opened (Ctrl+Shift+L toggles).");
        }
        else
        {
            activeGhostRenderer.Unplace();
            TearDownTracking();
            capi.ShowChatMessage(
                $"[Fieldwright] Ghost '{activeGhostName}' unlocked — back to floating. Aim to reposition, Ctrl+Shift+P to relock.");
        }
    }

    private void SpinUpTracking()
    {
        if (capi == null || activeGhostRenderer == null || activeSchematic == null || activeGhostName == null) return;

        // Compute the rotation increment (0/1/2/3 = 0°/90°/180°/270°) from the renderer's RotationY.
        float quarterTurn = MathF.PI / 2f;
        int rot = (int)MathF.Round(activeGhostRenderer.RotationY / quarterTurn);
        rot = ((rot % 4) + 4) % 4;

        activeMatchTracker = new GhostMatchTracker(
            capi, activeSchematic, activeAnchorOffset, activeGhostRenderer.Origin, rot);

        activeChecklistDialog = new GhostChecklistDialog(capi, activeMatchTracker, activeGhostName);
        activeChecklistDialog.TryOpen();

        // Poll every 250ms for completion → start the dismiss timer.
        completionTickListenerId = capi.Event.RegisterGameTickListener(CheckCompletion, 250);
        completionDetectedAtMs = 0;
        completionDismissNotified = false;

        FieldwrightLogger.Info(capi, Component,
            $"spun up match tracking: rot={rot * 90}°, expected={activeMatchTracker.TotalBlockCells}, " +
            $"air-cells={activeMatchTracker.TotalAirCells}");
    }

    private void CheckCompletion(float dt)
    {
        if (capi == null || activeMatchTracker == null) return;

        if (!activeMatchTracker.IsComplete)
        {
            // Build came uncompleted (player broke a matched block). Re-arm.
            completionDetectedAtMs = 0;
            completionDismissNotified = false;
            return;
        }

        long now = capi.World.ElapsedMilliseconds;

        if (completionDetectedAtMs == 0)
        {
            completionDetectedAtMs = now;
            if (!completionDismissNotified)
            {
                capi.ShowChatMessage(
                    $"[Fieldwright] Structure complete! Ghost will auto-dismiss in {CompletionAutoDismissMs / 1000} seconds.");
                completionDismissNotified = true;
            }
            return;
        }

        if (now - completionDetectedAtMs >= CompletionAutoDismissMs)
        {
            capi.ShowChatMessage($"[Fieldwright] Build complete — '{activeGhostName}' dismissed.");
            ClearActiveGhost();
        }
    }

    private void ToggleChecklistHud()
    {
        if (capi == null || activeChecklistDialog == null)
        {
            capi?.ShowChatMessage("[Fieldwright] No active checklist — place a ghost first.");
            return;
        }

        if (activeChecklistDialog.IsOpened())
        {
            activeChecklistDialog.TryClose();
        }
        else
        {
            activeChecklistDialog.TryOpen();
        }
    }

    private TextCommandResult OnCmdList(TextCommandCallingArgs args)
    {
        if (capi == null) return TextCommandResult.Error("Client API not available.");

        var names = BlueprintStore.List(capi);
        if (names.Count == 0)
        {
            return TextCommandResult.Success(
                $"[Fieldwright] No blueprints saved yet. Directory: {BlueprintStore.GetBlueprintsDirectory(capi)}");
        }

        return TextCommandResult.Success($"[Fieldwright] {names.Count} blueprint(s): {string.Join(", ", names)}");
    }

    private static string FormatPos(BlockPos pos) => $"({pos.X}, {pos.Y}, {pos.Z})";

    public override void Dispose()
    {
        ClearActiveGhost();
        base.Dispose();
    }
}
