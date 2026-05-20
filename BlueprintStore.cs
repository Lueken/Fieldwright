using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Lightweight blueprint summary for the library UI. Derived from the on-disk file
/// at scan time without keeping the whole schematic in memory.
/// </summary>
public class BlueprintEntry
{
    public string Name = string.Empty;
    public int SizeX, SizeY, SizeZ;
    public int BlockCount;
    public string AnchorFaceLabel = string.Empty;
    public DateTime ModifiedAt;
    public bool HasBackup;
}

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
    /// (single rolling backup. Previous .bak is overwritten.) Returns the backup
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
                $"loaded bare schematic '{name}' (no wrapper, implicit anchor (0,0,0))");
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

    /// <summary>
    /// Swap a blueprint with its rolling backup. After the swap, `.bak.json` holds the
    /// previously-active file and `.json` holds the previously-backed-up content.
    /// Running restore twice in a row returns the original state, so this is a
    /// reversible "undo last overwrite" rather than a one-shot rollback.
    ///
    /// Edge case: if the main file is gone (user deleted it) but the backup remains,
    /// promote backup → main without a swap.
    /// </summary>
    public static RestoreResult Restore(ICoreAPI api, string name)
    {
        var main = GetBlueprintPath(api, name);
        var backup = GetBackupPath(api, name);

        if (!File.Exists(backup))
        {
            FieldwrightLogger.Warn(api, Component, $"restore '{name}' failed: no backup at {backup}");
            return RestoreResult.NoBackup;
        }

        if (!File.Exists(main))
        {
            File.Move(backup, main);
            FieldwrightLogger.Info(api, Component, $"restored '{name}' from backup (main was missing)");
            return RestoreResult.PromotedFromBackup;
        }

        // Both exist. Three-step swap via a temp file for atomicity on Windows.
        var temp = main + ".swap";
        if (File.Exists(temp)) File.Delete(temp);
        File.Move(main, temp);
        File.Move(backup, main);
        File.Move(temp, backup);
        FieldwrightLogger.Info(api, Component, $"swapped '{name}' with its backup");
        return RestoreResult.Swapped;
    }

    public enum RestoreResult
    {
        NoBackup,
        PromotedFromBackup,
        Swapped,
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

    /// <summary>
    /// List blueprints with size + block-count metadata for the library UI. Each call
    /// re-parses every blueprint file, which is O(n) in disk and CPU but matches user
    /// expectations after editing files externally. Failed entries are skipped with a
    /// warning rather than aborting the whole list.
    /// </summary>
    public static List<BlueprintEntry> ListWithMetadata(ICoreAPI api)
    {
        var dir = GetBlueprintsDirectory(api);
        var files = Directory.GetFiles(dir, "*.json");
        var entries = new List<BlueprintEntry>();
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.EndsWith(".bak", System.StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var blueprint = Load(api, stem);
                var sch = blueprint.ToBlockSchematic();
                var face = blueprint.AnchorFacingResolved();
                entries.Add(new BlueprintEntry
                {
                    Name = stem,
                    SizeX = sch.SizeX,
                    SizeY = sch.SizeY,
                    SizeZ = sch.SizeZ,
                    BlockCount = sch.Indices?.Count ?? 0,
                    AnchorFaceLabel = face?.Code ?? "-",
                    ModifiedAt = File.GetLastWriteTime(file),
                    HasBackup = File.Exists(GetBackupPath(api, stem)),
                });
            }
            catch (System.Exception ex)
            {
                FieldwrightLogger.Warn(api, Component, $"failed to read metadata for '{stem}': {ex.Message}");
            }
        }
        entries.Sort((a, b) => b.ModifiedAt.CompareTo(a.ModifiedAt));
        return entries;
    }

    /// <summary>
    /// Delete a blueprint and its rolling backup. Throws FileNotFoundException if neither
    /// the main file nor a backup exists. Returns silently if only one of the two exists
    /// (deletes whatever is there).
    /// </summary>
    public static void Delete(ICoreAPI api, string name)
    {
        var main = GetBlueprintPath(api, name);
        var backup = GetBackupPath(api, name);
        bool mainExisted = File.Exists(main);
        bool backupExisted = File.Exists(backup);

        if (!mainExisted && !backupExisted)
        {
            throw new FileNotFoundException($"Blueprint not found: {name}");
        }
        if (mainExisted) File.Delete(main);
        if (backupExisted) File.Delete(backup);

        FieldwrightLogger.Info(api, Component,
            $"deleted '{name}' (main={mainExisted}, backup={backupExisted})");
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
