using System;
using Vintagestory.API.Common;

namespace Fieldwright;

/// <summary>
/// How strictly the match tracker treats a placed block as "the right block" for a
/// given ghost cell. Defaults to Loose to preserve v0.1.0 behavior and respect
/// external user feedback (KiriRae, ModDB): builders often can't source the exact
/// rock/wood type that was used in creative, and want substitutability by family.
/// </summary>
public enum MatchingMode
{
    /// <summary>block.FirstCodePart() only — "any cobble for any cobble".</summary>
    Loose,

    /// <summary>VS Variant API, stripping rotation/facing/orientation variants only.
    /// Preserves rock type, wood type, condition. Andesite cobble distinct from granite cobble,
    /// but all rotation variants of slanted thatch share a key.</summary>
    Medium,

    /// <summary>Full block code including every variant. Likely unusable in practice
    /// because VS auto-orients blocks at placement, but exposed for completeness.</summary>
    Strict,
}

/// <summary>
/// User-editable mod config. Lives at %APPDATA%/VintagestoryData/ModConfig/Fieldwright.json.
/// Loaded on StartClientSide; created with defaults if missing. New knobs added here
/// should pick conservative defaults that match v0.1.0 behavior so existing users
/// don't see surprise changes after an update.
/// </summary>
public class FieldwrightConfig
{
    /// <summary>Default matching mode for newly pasted ghosts. Overridable per-paste via .fw paste &lt;name&gt; &lt;mode&gt;.</summary>
    public MatchingMode DefaultMatchingMode { get; set; } = MatchingMode.Loose;

    /// <summary>Vertex alpha for the floating ghost mesh. Range 0.0 (invisible) to 1.0 (opaque). Default 0.3.</summary>
    public float GhostAlpha { get; set; } = 0.3f;

    /// <summary>Maximum render distance for the ghost in blocks. Default 256.</summary>
    public int RenderDistanceBlocks { get; set; } = 256;

    /// <summary>Milliseconds the checklist HUD lingers after structure completion before auto-dismissing. Default 5500.</summary>
    public int AutoDismissMs { get; set; } = 5500;

    /// <summary>Top-left pixel offset of the checklist HUD. Defaults (8, 60).</summary>
    public int HudOffsetX { get; set; } = 8;

    /// <summary>Top-left pixel offset of the checklist HUD. Defaults (8, 60).</summary>
    public int HudOffsetY { get; set; } = 60;

    /// <summary>
    /// Load the config from disk, falling back to defaults (and writing them out) if the
    /// file doesn't exist or fails to parse. Never throws.
    /// </summary>
    public static FieldwrightConfig Load(ICoreAPI api)
    {
        const string filename = "Fieldwright.json";
        FieldwrightConfig? config = null;

        try
        {
            config = api.LoadModConfig<FieldwrightConfig>(filename);
        }
        catch (Exception ex)
        {
            FieldwrightLogger.Warn(api, "config",
                $"failed to load {filename}: {ex.Message}. Using defaults; existing file (if any) is not touched.");
            return new FieldwrightConfig();
        }

        if (config == null)
        {
            config = new FieldwrightConfig();
            try
            {
                api.StoreModConfig(config, filename);
                FieldwrightLogger.Info(api, "config",
                    $"created default config at ModConfig/{filename} — edit to customize.");
            }
            catch (Exception ex)
            {
                FieldwrightLogger.Warn(api, "config",
                    $"failed to write default {filename}: {ex.Message}. Defaults still apply in memory.");
            }
        }
        else
        {
            FieldwrightLogger.Info(api, "config",
                $"loaded config: matching={config.DefaultMatchingMode}, alpha={config.GhostAlpha}, " +
                $"renderDist={config.RenderDistanceBlocks}, autoDismiss={config.AutoDismissMs}ms");
        }

        return config;
    }

    /// <summary>
    /// Parse a string into a MatchingMode for the `.fw paste &lt;name&gt; &lt;mode&gt;` arg path.
    /// Returns null if the input doesn't match a known mode (caller surfaces the error).
    /// </summary>
    public static MatchingMode? ParseMatchingMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.ToLowerInvariant() switch
        {
            "loose" => MatchingMode.Loose,
            "medium" => MatchingMode.Medium,
            "strict" => MatchingMode.Strict,
            _ => null,
        };
    }
}
