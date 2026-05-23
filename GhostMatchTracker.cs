using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Holds the strict expected-block map for a placed ghost and watches the world
/// for matches. When a real block is placed at a tracked cell, comparison fires
/// and the matched/unmatched sets update.
///
/// World positions are computed once at construction using the same Y rotation
/// increment as the renderer, so events on those positions are O(1) to check.
///
/// Phase 3a: chiseled-block matching is NOT performed yet, chisel positions
/// are added to the expected map with their default block code and will match
/// when a player places a generic chiseled block of the right substrate. Strict
/// voxel comparison is Phase 4.
/// </summary>
public class GhostMatchTracker : IDisposable
{
    private const string Component = "match";
    private const uint PosBitMask = 0x3ff;

    // HighlightBlocks slot ID for air-violation cells. Stable so we can clear it on
    // dispose. Distinct from the selection-box and selection-anchor slot IDs used by
    // FieldwrightModSystem (8501, 8502).
    private const int HighlightSlotAirViolation = 8503;

    private readonly ICoreClientAPI capi;

    /// <summary>
    /// cell world pos → expected block GROUP KEY (block.FirstCodePart()).
    /// Group-key matching means "any thatch roof variant satisfies any thatch roof
    /// cell", consistent with the checklist HUD's row grouping. Strict per-variant
    /// matching was over-fussy for auto-orienting blocks (logs, slanted roofs, hay
    /// bales) where the variant is picked by the engine based on placement context
    /// and ghost rotation, not directly by the player.
    /// </summary>
    private readonly Dictionary<BlockPos, string> expected = new();

    /// <summary>Display label per expected position, always FirstCodePart so the HUD shows
    /// clean names ("cobble", "slantedroofing") regardless of which matching mode is enforcing
    /// behind the scenes. The variant detail stays in <see cref="expected"/> for match checks.</summary>
    private readonly Dictionary<BlockPos, string> expectedDisplay = new();

    /// <summary>display label → set of group keys that satisfy it. Used by CountInInventory to
    /// sum across all variants that map to a single display label without exposing the variant
    /// detail to the player.</summary>
    private readonly Dictionary<string, HashSet<string>> groupKeysByDisplay = new();

    /// <summary>cells that should be air to count as matched</summary>
    private readonly HashSet<BlockPos> airPositions = new();

    /// <summary>cells the player has successfully matched (block-bearing positions)</summary>
    private readonly HashSet<BlockPos> matched = new();

    /// <summary>cells that should be air but aren't right now</summary>
    private readonly HashSet<BlockPos> airViolations = new();

    /// <summary>cells that expect a specific block but currently have the wrong (non-air) block.
    /// Same UX as airViolations from the player's perspective, they need to remove the block
    /// before the cell can be matched. Highlighted in the same red overlay.</summary>
    private readonly HashSet<BlockPos> wrongBlockViolations = new();

    public int TotalBlockCells => expected.Count;
    public int TotalAirCells => airPositions.Count;
    public int MatchedBlocks => matched.Count;
    public int AirViolationCount => airViolations.Count;
    public int WrongBlockCount => wrongBlockViolations.Count;
    /// <summary>Combined count of cells the player needs to clear (air-violation + wrong-block).</summary>
    public int BlocksToRemoveCount => airViolations.Count + wrongBlockViolations.Count;
    public bool IsComplete => matched.Count == expected.Count && airViolations.Count == 0 && wrongBlockViolations.Count == 0;

    /// <summary>Event listener handle so we can detach on Dispose.</summary>
    private readonly BlockChangedDelegate listener;

    private readonly MatchingMode matchingMode;

    public GhostMatchTracker(
        ICoreClientAPI capi,
        BlockSchematic schematic,
        Vec3i anchorOffset,
        BlockPos origin,
        int rotationIncrement,
        MirrorAxis mirror,
        MatchingMode matchingMode)
    {
        this.capi = capi;
        this.matchingMode = matchingMode;

        BuildExpectedMap(schematic, anchorOffset, origin, rotationIncrement, mirror);

        // Initial scan: any cells already containing the right block count as matched.
        // Also detects pre-existing air-position violations.
        InitialScan();

        listener = OnBlockChanged;
        capi.Event.BlockChanged += listener;

        RefreshAirViolationHighlight();

        FieldwrightLogger.Info(capi, Component,
            $"tracker active: {expected.Count} expected blocks, {airPositions.Count} air positions, " +
            $"{matched.Count} pre-matched, {airViolations.Count} initial air violations");
    }

    private void BuildExpectedMap(BlockSchematic schematic, Vec3i anchorOffset, BlockPos origin, int rot, MirrorAxis mirror)
    {
        // Track multiblock secondary cells so they're excluded from both expected
        // and air-positions. VS auto-creates these when the primary block is placed
        // (e.g. bed-head primary → foot secondary, tall doors, double-tall plants).
        // The player can't place them directly, so showing "multiblock: 0/N" in the
        // checklist is a UX bug.
        var multiblockCells = new HashSet<BlockPos>();

        // Schematic stores packed cells; decode position bits and map to world.
        for (int i = 0; i < schematic.Indices.Count; i++)
        {
            uint encoded = schematic.Indices[i];
            int dx = (int)(encoded & PosBitMask);
            int dy = (int)((encoded >> 20) & PosBitMask);
            int dz = (int)((encoded >> 10) & PosBitMask);

            if (!schematic.BlockCodes.TryGetValue(schematic.BlockIds[i], out var code)) continue;

            var world = CellToWorld(dx, dy, dz, anchorOffset, origin, rot, mirror);

            if (IsMultiblockPlaceholder(code))
            {
                multiblockCells.Add(world);
                continue;
            }

            var groupKey = GroupKey(code);
            var displayLabel = DisplayLabel(code);
            expected[world] = groupKey;
            expectedDisplay[world] = displayLabel;

            if (!groupKeysByDisplay.TryGetValue(displayLabel, out var set))
            {
                set = new HashSet<string>();
                groupKeysByDisplay[displayLabel] = set;
            }
            set.Add(groupKey);
        }

        // Air-positions live in EmptyAirBlocks if the schematic stored them.
        // Some serializations omit this; in that case air verification is a no-op.
        // We compute all cells inside the bounding box and subtract the block-bearing
        // ones, anything left over is implicit air that must stay air. Multiblock
        // secondary cells are excluded too (they'll fill in when the primary block
        // is placed and shouldn't be flagged as violations).
        for (int x = 0; x < schematic.SizeX; x++)
        {
            for (int y = 0; y < schematic.SizeY; y++)
            {
                for (int z = 0; z < schematic.SizeZ; z++)
                {
                    var world = CellToWorld(x, y, z, anchorOffset, origin, rot, mirror);
                    if (expected.ContainsKey(world)) continue;
                    if (multiblockCells.Contains(world)) continue;
                    airPositions.Add(world);
                }
            }
        }
    }

    private static bool IsMultiblockPlaceholder(AssetLocation? code)
    {
        var path = code?.Path;
        return path != null && path.StartsWith("multiblock", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Map a schematic-local cell (dx, dy, dz) to world position. Applies mirror in
    /// anchor-centered local space FIRST, then rotation. Mirror+rotation order matches
    /// the renderer's model matrix so expected positions stay aligned with the visible
    /// ghost mesh.
    /// </summary>
    private static BlockPos CellToWorld(int dx, int dy, int dz, Vec3i anchorOffset, BlockPos origin, int rot, MirrorAxis mirror)
    {
        int cx = dx - anchorOffset.X;
        int cy = dy - anchorOffset.Y;
        int cz = dz - anchorOffset.Z;

        // Mirror in anchor-centered local frame, before rotation. Block cells are unit
        // cubes so the integer negation here mirrors the same cell-center the renderer's
        // Scale applies (both pivot on the anchor block center, [0,1]^3 stays at [0,1]^3).
        switch (mirror)
        {
            case MirrorAxis.X: cx = -cx; break;
            case MirrorAxis.Y: cy = -cy; break;
            case MirrorAxis.Z: cz = -cz; break;
        }

        int rdx, rdz;
        switch (((rot % 4) + 4) % 4)
        {
            case 0: rdx = cx; rdz = cz; break;
            case 1: rdx = cz; rdz = -cx; break;     // +90° CCW around Y
            case 2: rdx = -cx; rdz = -cz; break;
            case 3: rdx = -cz; rdz = cx; break;
            default: rdx = cx; rdz = cz; break;
        }

        return new BlockPos(origin.X + rdx, origin.Y + cy, origin.Z + rdz, origin.dimension);
    }

    /// <summary>
    /// Variants that describe orientation rather than identity. Stripped from the
    /// group key in Medium mode so a player can satisfy a south-facing thatch slope
    /// with any other orientation, but not with a different material (oak instead of
    /// hay) or a different variant family (planks instead of cobble).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> OrientationVariants = new()
    {
        "rotation", "side", "facing", "horizontalorientation", "verticalorientation",
    };

    /// <summary>
    /// Group key for an AssetLocation. Behavior depends on the configured matching mode:
    ///   - Loose: block.FirstCodePart() ("any cobble for any cobble"). v0.1.0 default.
    ///   - Medium: VS Variant API, stripping orientation variants only. Preserves rock/wood type.
    ///   - Strict: full block code (effectively unusable due to VS auto-orientation at placement).
    /// </summary>
    private string GroupKey(AssetLocation? code)
    {
        if (code == null) return string.Empty;

        if (matchingMode == MatchingMode.Strict)
        {
            return code.ToString();
        }

        var block = capi.World.GetBlock(code);

        if (matchingMode == MatchingMode.Medium && block != null)
        {
            return BuildMediumKey(block);
        }

        // Loose (default) and fallback for unrecognized block codes.
        if (block != null)
        {
            var first = block.FirstCodePart();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        var path = code.Path ?? string.Empty;
        int dash = path.IndexOf('-');
        return dash < 0 ? path : path.Substring(0, dash);
    }

    /// <summary>
    /// Display label for the HUD, always FirstCodePart so player-facing rows are clean
    /// ("cobble", "log", "slantedroofing") regardless of matching mode. Variant detail
    /// lives only in the GroupKey used for backend match checks.
    /// </summary>
    private string DisplayLabel(AssetLocation? code)
    {
        if (code == null) return string.Empty;
        var block = capi.World.GetBlock(code);
        if (block != null)
        {
            var first = block.FirstCodePart();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        var path = code.Path ?? string.Empty;
        int dash = path.IndexOf('-');
        return dash < 0 ? path : path.Substring(0, dash);
    }

    private static string BuildMediumKey(Block block)
    {
        var first = block.FirstCodePart() ?? string.Empty;
        var variants = block.Variant;
        if (variants == null || variants.Count == 0) return first;

        // Stable order: sort by variant group name so two blocks with the same
        // non-orientation variants always produce the same key.
        var kept = new System.Collections.Generic.SortedDictionary<string, string>();
        foreach (var pair in variants)
        {
            if (OrientationVariants.Contains(pair.Key)) continue;
            kept[pair.Key] = pair.Value;
        }
        if (kept.Count == 0) return first;

        var sb = new System.Text.StringBuilder(first);
        sb.Append('[');
        bool firstPair = true;
        foreach (var pair in kept)
        {
            if (!firstPair) sb.Append(',');
            sb.Append(pair.Key).Append('=').Append(pair.Value);
            firstPair = false;
        }
        sb.Append(']');
        return sb.ToString();
    }

    private void InitialScan()
    {
        var ba = capi.World.BlockAccessor;

        foreach (var (pos, expectedGroup) in expected)
        {
            var b = ba.GetBlock(pos);
            bool isAir = b == null || b.Id == 0;
            bool isMatch = !isAir && b!.Code != null && GroupKey(b.Code) == expectedGroup;

            if (isMatch) matched.Add(pos);
            else if (!isAir) wrongBlockViolations.Add(pos);
        }

        foreach (var pos in airPositions)
        {
            var b = ba.GetBlock(pos);
            if (b != null && b.Id != 0)
            {
                airViolations.Add(pos);
            }
        }
    }

    private void OnBlockChanged(BlockPos pos, Block oldBlock)
    {
        if (pos == null) return;

        if (expected.TryGetValue(pos, out var expectedGroup))
        {
            var current = capi.World.BlockAccessor.GetBlock(pos);
            bool isAir = current == null || current.Id == 0;
            bool isMatch = !isAir && current!.Code != null && GroupKey(current.Code) == expectedGroup;

            if (isMatch) matched.Add(pos);
            else matched.Remove(pos);

            // Wrong-block detection: cell has a non-air, non-matching block.
            bool nowWrong = !isAir && !isMatch;
            bool wrongChanged = nowWrong
                ? wrongBlockViolations.Add(pos)
                : wrongBlockViolations.Remove(pos);
            if (wrongChanged) RefreshAirViolationHighlight();
            return;
        }

        if (airPositions.Contains(pos))
        {
            var current = capi.World.BlockAccessor.GetBlock(pos);
            bool isAir = current == null || current.Id == 0;

            bool changed = isAir ? airViolations.Remove(pos) : airViolations.Add(pos);
            if (changed) RefreshAirViolationHighlight();
        }
    }

    /// <summary>
    /// Paint every "needs-to-be-cleared" cell with a translucent red highlight so the player
    /// can locate them in 3D, not just see a count in the HUD. Covers both:
    ///   - airViolations: cell should be air but has a non-air block
    ///   - wrongBlockViolations: cell has a block of the wrong type for that ghost cell
    /// From the player's perspective the action is the same, remove the block.
    /// Red at alpha 40 vs the green selection box at alpha 60 keeps the two visually distinct.
    /// </summary>
    private void RefreshAirViolationHighlight()
    {
        int total = airViolations.Count + wrongBlockViolations.Count;
        var positions = new List<BlockPos>(total);
        var colors = new List<int>(total);
        int violationColor = ColorUtil.ColorFromRgba(220, 80, 80, 40);

        foreach (var pos in airViolations)
        {
            positions.Add(pos);
            colors.Add(violationColor);
        }
        foreach (var pos in wrongBlockViolations)
        {
            positions.Add(pos);
            colors.Add(violationColor);
        }

        capi.World.HighlightBlocks(
            capi.World.Player, HighlightSlotAirViolation,
            positions, colors,
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
    }

    /// <summary>
    /// Aggregate unmatched expected blocks by GROUP key (block.FirstCodePart()).
    /// Returns display label → count of cells still needing a block in that family.
    /// Aggregated by FirstCodePart so HUD rows stay clean even when Medium / Strict
    /// matching is enforcing variant identity behind the scenes.
    /// </summary>
    public Dictionary<string, int> GetMaterialNeeds()
    {
        var needs = new Dictionary<string, int>();
        foreach (var pos in expected.Keys)
        {
            if (matched.Contains(pos)) continue;
            var label = expectedDisplay[pos];
            needs.TryGetValue(label, out int count);
            needs[label] = count + 1;
        }
        return needs;
    }

    /// <summary>
    /// Sum of inventory items whose group key matches ANY expected variant under the given
    /// display label. Truthful per-mode: in Loose mode all cobble counts as "cobble"; in
    /// Medium mode only the cobbles whose specific variants appear in the blueprint count.
    /// </summary>
    public int CountInInventory(string displayLabel)
    {
        var player = capi.World.Player;
        if (player?.InventoryManager == null) return 0;
        if (!groupKeysByDisplay.TryGetValue(displayLabel, out var acceptableGroups)) return 0;

        var hotbar = player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        var backpack = player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        var seen = new HashSet<ItemSlot>();

        int total = 0;
        foreach (var inv in new IInventory?[] { hotbar, backpack })
        {
            if (inv == null) continue;
            foreach (var slot in inv)
            {
                if (slot == null || !seen.Add(slot)) continue;
                var stack = slot.Itemstack;
                if (stack?.Block?.Code != null && acceptableGroups.Contains(GroupKey(stack.Block.Code)))
                {
                    total += stack.StackSize;
                }
            }
        }
        return total;
    }

    public void Dispose()
    {
        capi.Event.BlockChanged -= listener;

        // Clear the air-violation highlight so it doesn't linger after the ghost is
        // dismissed (auto-completion, manual clear, or .fw unplace).
        capi.World.HighlightBlocks(
            capi.World.Player, HighlightSlotAirViolation,
            new List<BlockPos>(),
            EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);

        FieldwrightLogger.Info(capi, Component, "tracker disposed");
    }
}
