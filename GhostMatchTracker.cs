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
/// Phase 3a: chiseled-block matching is NOT performed yet — chisel positions
/// are added to the expected map with their default block code and will match
/// when a player places a generic chiseled block of the right substrate. Strict
/// voxel comparison is Phase 4.
/// </summary>
public class GhostMatchTracker : IDisposable
{
    private const string Component = "match";
    private const uint PosBitMask = 0x3ff;

    private readonly ICoreClientAPI capi;

    /// <summary>
    /// cell world pos → expected block GROUP KEY (block.FirstCodePart()).
    /// Group-key matching means "any thatch roof variant satisfies any thatch roof
    /// cell" — consistent with the checklist HUD's row grouping. Strict per-variant
    /// matching was over-fussy for auto-orienting blocks (logs, slanted roofs, hay
    /// bales) where the variant is picked by the engine based on placement context
    /// and ghost rotation, not directly by the player.
    /// </summary>
    private readonly Dictionary<BlockPos, string> expected = new();

    /// <summary>cells that should be air to count as matched</summary>
    private readonly HashSet<BlockPos> airPositions = new();

    /// <summary>cells the player has successfully matched (block-bearing positions)</summary>
    private readonly HashSet<BlockPos> matched = new();

    /// <summary>cells that should be air but aren't right now</summary>
    private readonly HashSet<BlockPos> airViolations = new();

    public int TotalBlockCells => expected.Count;
    public int TotalAirCells => airPositions.Count;
    public int MatchedBlocks => matched.Count;
    public int AirViolationCount => airViolations.Count;
    public bool IsComplete => matched.Count == expected.Count && airViolations.Count == 0;

    /// <summary>Event listener handle so we can detach on Dispose.</summary>
    private readonly BlockChangedDelegate listener;

    public GhostMatchTracker(
        ICoreClientAPI capi,
        BlockSchematic schematic,
        Vec3i anchorOffset,
        BlockPos origin,
        int rotationIncrement)
    {
        this.capi = capi;

        BuildExpectedMap(schematic, anchorOffset, origin, rotationIncrement);

        // Initial scan: any cells already containing the right block count as matched.
        // Also detects pre-existing air-position violations.
        InitialScan();

        listener = OnBlockChanged;
        capi.Event.BlockChanged += listener;

        FieldwrightLogger.Info(capi, Component,
            $"tracker active: {expected.Count} expected blocks, {airPositions.Count} air positions, " +
            $"{matched.Count} pre-matched, {airViolations.Count} initial air violations");
    }

    private void BuildExpectedMap(BlockSchematic schematic, Vec3i anchorOffset, BlockPos origin, int rot)
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

            var world = CellToWorld(dx, dy, dz, anchorOffset, origin, rot);

            if (IsMultiblockPlaceholder(code))
            {
                multiblockCells.Add(world);
                continue;
            }

            expected[world] = GroupKey(code);
        }

        // Air-positions live in EmptyAirBlocks if the schematic stored them.
        // Some serializations omit this; in that case air verification is a no-op.
        // We compute all cells inside the bounding box and subtract the block-bearing
        // ones — anything left over is implicit air that must stay air. Multiblock
        // secondary cells are excluded too (they'll fill in when the primary block
        // is placed and shouldn't be flagged as violations).
        for (int x = 0; x < schematic.SizeX; x++)
        {
            for (int y = 0; y < schematic.SizeY; y++)
            {
                for (int z = 0; z < schematic.SizeZ; z++)
                {
                    var world = CellToWorld(x, y, z, anchorOffset, origin, rot);
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

    /// <summary>Map a schematic-local cell (dx, dy, dz) to world position, applying the rotation pivot at the anchor cell.</summary>
    private static BlockPos CellToWorld(int dx, int dy, int dz, Vec3i anchorOffset, BlockPos origin, int rot)
    {
        int cx = dx - anchorOffset.X;
        int cy = dy - anchorOffset.Y;
        int cz = dz - anchorOffset.Z;

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
    /// Group key for an AssetLocation — uses Block.FirstCodePart() so all variants
    /// of the same conceptual block (e.g. all slanted-roof orientations) share a
    /// key. Matches the HUD's row grouping.
    /// </summary>
    private string GroupKey(AssetLocation? code)
    {
        if (code == null) return string.Empty;
        var block = capi.World.GetBlock(code);
        if (block != null)
        {
            var first = block.FirstCodePart();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        // Fallback: first hyphen-separated segment of the path.
        var path = code.Path ?? string.Empty;
        int dash = path.IndexOf('-');
        return dash < 0 ? path : path.Substring(0, dash);
    }

    private void InitialScan()
    {
        var ba = capi.World.BlockAccessor;

        foreach (var (pos, expectedGroup) in expected)
        {
            var b = ba.GetBlock(pos);
            if (b != null && b.Id != 0 && b.Code != null && GroupKey(b.Code) == expectedGroup)
            {
                matched.Add(pos);
            }
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
            bool isMatch = current != null && current.Id != 0
                           && current.Code != null && GroupKey(current.Code) == expectedGroup;

            if (isMatch) matched.Add(pos);
            else matched.Remove(pos);
            return;
        }

        if (airPositions.Contains(pos))
        {
            var current = capi.World.BlockAccessor.GetBlock(pos);
            bool isAir = current == null || current.Id == 0;

            if (isAir) airViolations.Remove(pos);
            else airViolations.Add(pos);
        }
    }

    /// <summary>
    /// Aggregate unmatched expected blocks by GROUP key (block.FirstCodePart()).
    /// Returns group → count of cells still needing that group. Group keys match
    /// the HUD's row display so player-facing counts and internal matching stay
    /// in sync.
    /// </summary>
    public Dictionary<string, int> GetMaterialNeeds()
    {
        var needs = new Dictionary<string, int>();
        foreach (var (pos, group) in expected)
        {
            if (matched.Contains(pos)) continue;
            needs.TryGetValue(group, out int count);
            needs[group] = count + 1;
        }
        return needs;
    }

    /// <summary>Look up how many blocks-as-items the player carries in any group.</summary>
    public int CountInInventory(string groupKey)
    {
        var player = capi.World.Player;
        if (player?.InventoryManager == null) return 0;

        // Only hotbar + backpack — exclude creative inventory and aux inventories
        // that would over-count.
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
                if (stack?.Block?.Code != null && GroupKey(stack.Block.Code) == groupKey)
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
        FieldwrightLogger.Info(capi, Component, "tracker disposed");
    }
}
