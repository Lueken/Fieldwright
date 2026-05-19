using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Fieldwright;

/// <summary>
/// Read/write blueprint files in %APPDATA%/VintagestoryData/Blueprints/. Falls back to
/// reading bare BlockSchematic JSON files with an implicit (0,0,0) anchor for
/// compatibility with WorldEdit / BetterRuins schematics.
/// </summary>
public static class BlueprintStore
{
    private const string Component = "store";
    private const string BlueprintsFolderName = "Blueprints";

    public static string GetBlueprintsDirectory(ICoreAPI api)
    {
        var dir = Path.Combine(GamePaths.DataPath, BlueprintsFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetBlueprintPath(ICoreAPI api, string name)
    {
        var sanitized = SanitizeName(name);
        return Path.Combine(GetBlueprintsDirectory(api), sanitized + ".json");
    }

    public static string GetBackupPath(ICoreAPI api, string name)
    {
        var sanitized = SanitizeName(name);
        return Path.Combine(GetBlueprintsDirectory(api), sanitized + ".bak.json");
    }

    public static bool Exists(ICoreAPI api, string name)
    {
        return File.Exists(GetBlueprintPath(api, name));
    }

    /// <summary>
    /// If a blueprint with this name exists on disk, copy it to {name}.bak.json
    /// (single rolling backup — previous .bak is overwritten). Returns the backup
    /// path if a backup was made, null otherwise.
    /// </summary>
    public static string? BackupExisting(ICoreAPI api, string name)
    {
        var src = GetBlueprintPath(api, name);
        if (!File.Exists(src)) return null;

        var dst = GetBackupPath(api, name);
        File.Copy(src, dst, overwrite: true);
        FieldwrightLogger.Info(api, Component, $"backed up '{name}' → {dst}");
        return dst;
    }

    public static void Save(ICoreAPI api, string name, BlueprintFile blueprint)
    {
        var path = GetBlueprintPath(api, name);
        var json = JsonConvert.SerializeObject(blueprint, Formatting.Indented);
        File.WriteAllText(path, json);
        FieldwrightLogger.Info(api, Component, $"saved blueprint '{name}' → {path} ({json.Length} bytes)");
    }

    public static BlueprintFile Load(ICoreAPI api, string name)
    {
        var path = GetBlueprintPath(api, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Blueprint not found: {path}");
        }

        var text = File.ReadAllText(path);
        var parsed = JObject.Parse(text);

        // Detect bare BlockSchematic (no version / anchorOffset) vs our wrapper.
        if (parsed["schematic"] == null && parsed["anchorOffset"] == null)
        {
            FieldwrightLogger.Info(api, Component,
                $"loaded bare schematic '{name}' (no wrapper) — implicit anchor (0,0,0)");
            return new BlueprintFile
            {
                Version = "compat-bare",
                AnchorOffset = new Vec3iSerializable(),
                Schematic = parsed
            };
        }

        var wrapper = parsed.ToObject<BlueprintFile>();
        if (wrapper == null)
        {
            throw new System.InvalidOperationException($"Failed to deserialize blueprint wrapper at {path}");
        }

        FieldwrightLogger.Info(api, Component,
            $"loaded blueprint '{name}' (version={wrapper.Version}, anchor=({wrapper.AnchorOffset.X},{wrapper.AnchorOffset.Y},{wrapper.AnchorOffset.Z}))");
        return wrapper;
    }

    public static List<string> List(ICoreAPI api)
    {
        var dir = GetBlueprintsDirectory(api);
        var files = Directory.GetFiles(dir, "*.json");
        var names = new List<string>();
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            // Skip rolling-backup files ({name}.bak.json → stem is "{name}.bak").
            if (stem.EndsWith(".bak", System.StringComparison.OrdinalIgnoreCase)) continue;
            names.Add(stem);
        }
        names.Sort();
        return names;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            foreach (var bad in invalid)
            {
                if (chars[i] == bad) chars[i] = '_';
            }
        }
        var result = new string(chars).Trim();
        if (string.IsNullOrEmpty(result)) result = "unnamed";
        return result;
    }
}
