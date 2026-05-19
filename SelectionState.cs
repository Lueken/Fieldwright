using System;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Client-side singleton tracking the active capture selection. Stores the bounding
/// box (Min/Max) and the anchor block (corner1's original position). Grow/Shrink
/// adjust Min/Max independently; Anchor never moves once set.
/// </summary>
public class SelectionState
{
    public BlockPos? Anchor { get; private set; }
    public BlockFacing? AnchorFace { get; private set; }
    public BlockPos? Min { get; private set; }
    public BlockPos? Max { get; private set; }

    public bool HasSelection => Anchor != null && Min != null && Max != null;

    /// <summary>
    /// Set corner 1 — the placement anchor. Resets the box to a 1×1×1 cube at this position.
    /// The face param records which face of the anchor block was player-facing when captured;
    /// this drives auto-rotate on paste so the saved face aligns with the paste-time face.
    /// </summary>
    public void SetCorner1(BlockPos pos, BlockFacing? face = null)
    {
        Anchor = pos.Copy();
        AnchorFace = face;
        Min = pos.Copy();
        Max = pos.Copy();
    }

    /// <summary>
    /// Set corner 2. If corner 1 isn't set yet, this becomes corner 1.
    /// Otherwise, the bounding box expands so corner 1 (anchor) and the new pos
    /// are opposite corners.
    /// </summary>
    public void SetCorner2(BlockPos pos)
    {
        if (Anchor == null)
        {
            SetCorner1(pos);
            return;
        }

        Min = new BlockPos(
            Math.Min(Anchor.X, pos.X),
            Math.Min(Anchor.Y, pos.Y),
            Math.Min(Anchor.Z, pos.Z),
            Anchor.dimension
        );

        Max = new BlockPos(
            Math.Max(Anchor.X, pos.X),
            Math.Max(Anchor.Y, pos.Y),
            Math.Max(Anchor.Z, pos.Z),
            Anchor.dimension
        );
    }

    /// <summary>
    /// Expand the bounding box outward on the given face by n blocks.
    /// Anchor position is unaffected.
    /// </summary>
    public void Grow(BlockFacing face, int n)
    {
        if (Min == null || Max == null || face == null) return;
        if (n <= 0) return;

        var normal = face.Normali;

        if (normal.X > 0) Max.X += n;
        else if (normal.X < 0) Min.X -= n;
        else if (normal.Y > 0) Max.Y += n;
        else if (normal.Y < 0) Min.Y -= n;
        else if (normal.Z > 0) Max.Z += n;
        else if (normal.Z < 0) Min.Z -= n;
    }

    /// <summary>
    /// Contract the bounding box inward on the given face by n blocks.
    /// Refuses to shrink past a 1-block thickness on that axis.
    /// Anchor position is unaffected.
    /// </summary>
    public void Shrink(BlockFacing face, int n)
    {
        if (Min == null || Max == null || face == null) return;
        if (n <= 0) return;

        var normal = face.Normali;

        if (normal.X > 0) Max.X = Math.Max(Max.X - n, Min.X);
        else if (normal.X < 0) Min.X = Math.Min(Min.X + n, Max.X);
        else if (normal.Y > 0) Max.Y = Math.Max(Max.Y - n, Min.Y);
        else if (normal.Y < 0) Min.Y = Math.Min(Min.Y + n, Max.Y);
        else if (normal.Z > 0) Max.Z = Math.Max(Max.Z - n, Min.Z);
        else if (normal.Z < 0) Min.Z = Math.Min(Min.Z + n, Max.Z);
    }

    public void Clear()
    {
        Anchor = null;
        AnchorFace = null;
        Min = null;
        Max = null;
    }

    public (int width, int height, int depth) GetDimensions()
    {
        if (Min == null || Max == null) return (0, 0, 0);
        return (Max.X - Min.X + 1, Max.Y - Min.Y + 1, Max.Z - Min.Z + 1);
    }

    /// <summary>
    /// Returns the anchor's offset from Min — used as anchorOffset in the saved blueprint.
    /// </summary>
    public Vec3i GetAnchorOffsetFromMin()
    {
        if (Anchor == null || Min == null) return new Vec3i(0, 0, 0);
        return new Vec3i(Anchor.X - Min.X, Anchor.Y - Min.Y, Anchor.Z - Min.Z);
    }
}
