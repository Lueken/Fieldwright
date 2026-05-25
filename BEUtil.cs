using System;
using System.IO;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Block-entity decode helpers shared by the ghost mesh builder and the match tracker.
/// Both pipelines need to peek at the saved BE tree for a cell, mostly to read the
/// chiseled substrate so chisel cells render and count as their underlying material
/// during the build phase.
/// </summary>
public static class BEUtil
{
    private const string Component = "be-util";

    /// <summary>
    /// Decode an Ascii85-encoded TreeAttribute payload from BlockSchematic.BlockEntities.
    /// Returns null on any failure; callers treat null as "no BE data, render as plain".
    /// </summary>
    public static ITreeAttribute? DecodeBETree(ICoreClientAPI capi, string ascii85)
    {
        if (string.IsNullOrEmpty(ascii85)) return null;
        try
        {
            byte[] bytes = Ascii85.Decode(ascii85);
            var tree = new TreeAttribute();
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);
            tree.FromBytes(br);
            return tree;
        }
        catch (Exception ex)
        {
            FieldwrightLogger.Warn(capi, Component, $"failed to decode BE tree: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolve the substrate block of a chiseled cell from its BE tree. Substrate is the
    /// material the chisel was carved from (the first entry in BlockEntityMicroBlock's
    /// materials list). Returns null if no substrate can be resolved.
    /// </summary>
    public static Block? TryGetChiselSubstrate(ICoreClientAPI capi, ITreeAttribute? beTree)
    {
        if (beTree == null) return null;

        var ids = ResolveMicroBlockMaterialIds(capi, beTree);
        if (ids == null || ids.Length == 0) return null;

        int substrateId = ids[0];
        if (substrateId <= 0) return null;

        var substrate = capi.World.GetBlock(substrateId);
        return substrate != null && substrate.Id != 0 ? substrate : null;
    }

    // Cache the resolved type + method after first lookup so the assembly sweep only
    // happens once per session.
    private static MethodInfo? cachedMaterialIdsMethod;
    private static bool materialIdsResolved;

    private static int[]? ResolveMicroBlockMaterialIds(ICoreClientAPI capi, ITreeAttribute tree)
    {
        try
        {
            if (!materialIdsResolved)
            {
                materialIdsResolved = true;
                Type? t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        t = asm.GetType("Vintagestory.GameContent.BlockEntityMicroBlock");
                        if (t != null) break;
                    }
                    catch { /* skip dynamic assemblies */ }
                }
                if (t == null)
                {
                    FieldwrightLogger.Warn(capi, Component,
                        "BlockEntityMicroBlock type not found in any loaded assembly");
                    return null;
                }
                cachedMaterialIdsMethod = t.GetMethod("MaterialIdsFromAttributes",
                    BindingFlags.Public | BindingFlags.Static);
                if (cachedMaterialIdsMethod == null)
                {
                    FieldwrightLogger.Warn(capi, Component,
                        $"BlockEntityMicroBlock.MaterialIdsFromAttributes not found in {t.Assembly.GetName().Name}");
                    return null;
                }
            }

            if (cachedMaterialIdsMethod == null) return null;
            return cachedMaterialIdsMethod.Invoke(null, new object[] { tree, capi.World }) as int[];
        }
        catch (Exception ex)
        {
            FieldwrightLogger.Warn(capi, Component,
                $"MaterialIdsFromAttributes reflection failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
