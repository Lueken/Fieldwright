using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// On-disk wrapper for a blueprint. Stores the BlockSchematic JSON alongside the
/// anchor offset (corner1's position within the schematic's local coordinate space).
///
/// Forward-compat: future versions can add fields without breaking old files.
/// Backward-compat: if a file parses as a bare BlockSchematic (no version / anchorOffset
/// fields), treat anchorOffset as (0, 0, 0). This lets the mod read raw WorldEdit /
/// BetterRuins schematics with an implicit anchor at the schematic's min corner.
/// </summary>
public class BlueprintFile
{
    [JsonProperty("version")]
    public string Version { get; set; } = "0.1.0";

    [JsonProperty("anchorOffset")]
    public Vec3iSerializable AnchorOffset { get; set; } = new Vec3iSerializable();

    /// <summary>
    /// Which face of the anchor block was player-facing when corner1 was captured.
    /// One of "north", "south", "east", "west", "up", "down", or null when the
    /// captured face was vertical / not captured. Drives auto-rotate on paste.
    /// </summary>
    [JsonProperty("anchorFace", NullValueHandling = NullValueHandling.Ignore)]
    public string? AnchorFace { get; set; }

    [JsonProperty("schematic")]
    public JObject? Schematic { get; set; }

    public BlockFacing? AnchorFacingResolved()
    {
        if (string.IsNullOrEmpty(AnchorFace)) return null;
        var f = BlockFacing.FromCode(AnchorFace);
        // Only horizontal faces drive auto-rotation. Vertical captures stay saved
        // for forward-compat but produce no rotation today.
        return f != null && f.IsHorizontal ? f : null;
    }

    public BlockSchematic ToBlockSchematic()
    {
        if (Schematic == null)
        {
            throw new System.InvalidOperationException("BlueprintFile has no schematic payload.");
        }

        string error = string.Empty;
        var schematic = BlockSchematic.LoadFromString(Schematic.ToString(Formatting.None), ref error);

        if (!string.IsNullOrEmpty(error))
        {
            throw new System.InvalidOperationException($"Failed to load schematic: {error}");
        }

        return schematic;
    }

    public Vec3i AnchorOffsetAsVec3i() => new Vec3i(AnchorOffset.X, AnchorOffset.Y, AnchorOffset.Z);

    public static BlueprintFile Wrap(BlockSchematic schematic, Vec3i anchorOffset, BlockFacing? anchorFace = null)
    {
        var json = schematic.ToJson();
        return new BlueprintFile
        {
            Version = "0.1.0",
            AnchorOffset = new Vec3iSerializable { X = anchorOffset.X, Y = anchorOffset.Y, Z = anchorOffset.Z },
            AnchorFace = anchorFace?.Code,
            Schematic = JObject.Parse(json)
        };
    }
}

/// <summary>
/// JSON-friendly Vec3i (Newtonsoft serializes Vintagestory's Vec3i with extra noise
/// because of its dimension-aware coordinate helpers). Plain X/Y/Z keeps the file
/// readable and portable.
/// </summary>
public class Vec3iSerializable
{
    [JsonProperty("x")] public int X { get; set; }
    [JsonProperty("y")] public int Y { get; set; }
    [JsonProperty("z")] public int Z { get; set; }
}
