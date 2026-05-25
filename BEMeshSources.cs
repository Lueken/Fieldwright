using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Builds a ghost-mesh for a single schematic cell, optionally consulting its block-entity
/// tree. Each source handles one block family. Sources are stateless; the registry picks
/// one per cell by prefix, then we call Provide once.
///
/// Returning null means "I can't render this cell, fall through to the next source or the
/// default tessellate path." Returning an empty mesh means "render nothing here."
/// </summary>
public interface IBEMeshSource
{
    string Prefix { get; }
    MeshData? Provide(ICoreClientAPI capi, Block block, ITreeAttribute? beTree);
}

/// <summary>
/// Default mesh source: just tessellate the block and apply meshAngle from the BE tree
/// if present. Used for blocks whose own tessellation produces a real mesh (chests,
/// labeledchests, querns) and whose only BE-driven state is rotation. The block's
/// orientation variant in its code (chest-east, chest-north) already gives us the base
/// orientation; meshAngle adds fine rotation for askew placements.
/// </summary>
public class TesselateAndRotateMeshSource : IBEMeshSource
{
    public string Prefix { get; }
    private static readonly Vec3f BlockCenter = new Vec3f(0.5f, 0.5f, 0.5f);

    public TesselateAndRotateMeshSource(string prefix) { Prefix = prefix; }

    public MeshData? Provide(ICoreClientAPI capi, Block block, ITreeAttribute? beTree)
    {
        capi.Tesselator.TesselateBlock(block, out var mesh);
        if (mesh == null || mesh.VerticesCount == 0) return null;
        if (beTree != null)
        {
            float angle = beTree.GetFloat("meshAngle", 0f);
            if (angle != 0f) mesh.Rotate(BlockCenter, 0f, angle, 0f);
        }
        return mesh;
    }
}

/// <summary>
/// Temp-BE mesh source: spin up a throwaway BlockEntity instance, feed it the schematic's
/// BE tree, let it build its own mesh, then steal the result via reflection. Used for
/// families whose block tessellation produces a zero-vertex mesh because all geometry
/// comes from BE state, e.g. crate (BlockEntityCrate.ownMesh), stationarybasket, and
/// labeled containers.
///
/// The catches we work around:
/// - BlockEntity.Block has a protected setter, so we set it via reflection
/// - BlockEntity.Api is set inside Initialize(api), so we let Initialize handle it
/// - Initialize chains into FromTreeAttributes -&gt; loadOrCreateMesh / GenMesh /
///   genLabelMesh which populate the private mesh field
/// - We then pull the mesh out via a list of well-known field names (ownMesh, mesh,
///   Mesh) since each BE family uses a different one
/// </summary>
public class TempBEMeshSource : IBEMeshSource
{
    public string Prefix { get; }
    private static readonly Vec3f BlockCenter = new Vec3f(0.5f, 0.5f, 0.5f);

    // Likely-private field names that hold the cached own-mesh on each BE family we
    // care about. Ordered most-specific to most-generic so BlockEntityCrate.ownMesh
    // is preferred over a hypothetical superclass mesh.
    private static readonly string[] OwnMeshFieldNames = { "ownMesh", "Mesh", "mesh" };

    // We never want to AddMeshData our own ghost into a real chunk, so we use a stub
    // BlockPos. (0, 1, 0) avoids the (0,0,0) sentinel some BEs check against.
    private static readonly BlockPos StubPos = new BlockPos(0, 1, 0);

    public TempBEMeshSource(string prefix) { Prefix = prefix; }

    public MeshData? Provide(ICoreClientAPI capi, Block block, ITreeAttribute? beTree)
    {
        if (beTree == null) return null;
        if (string.IsNullOrEmpty(block.EntityClass)) return null;

        BlockEntity? be;
        try
        {
            be = capi.World.ClassRegistry.CreateBlockEntity(block.EntityClass);
        }
        catch (Exception ex)
        {
            FieldwrightLogger.Warn(capi, "be-mesh",
                $"CreateBlockEntity('{block.EntityClass}') threw: {ex.Message}");
            return null;
        }
        if (be == null) return null;

        // BlockEntity.Block has a protected setter; set it via reflection so the BE's
        // mesh-build code can read block.Shape, block.Code, block.Variant, etc.
        SetBlockProperty(be, block);
        be.Pos = StubPos.Copy();

        try { be.FromTreeAttributes(beTree, capi.World); }
        catch (Exception ex)
        {
            FieldwrightLogger.Debug(capi, "be-mesh",
                $"FromTreeAttributes for '{block.Code}' threw: {ex.GetType().Name}: {ex.Message}");
        }
        try { be.Initialize(capi); }
        catch (Exception ex)
        {
            FieldwrightLogger.Debug(capi, "be-mesh",
                $"Initialize for '{block.Code}' threw: {ex.GetType().Name}: {ex.Message}");
        }

        // After Initialize, the BE's cached mesh field is usually still null because
        // crate/container Initialize only triggers mesh build on client side via an
        // OnReceivedClientPacket / chunk-event path that doesn't fire for our temp BE.
        // Try the private mesh-builder methods directly. Each family uses a different
        // name and signature; cascade through them.
        var mesh = TryBuildMeshDirectly(capi, be);
        if (mesh == null)
        {
            // Last resort: maybe Initialize did happen to populate it.
            mesh = ExtractOwnMesh(be);
        }
        if (mesh == null) return null;

        // Clone before returning, the BE's cached field is shared and we'll be
        // mutating (rotating, alpha-multiplying, accumulating into a per-layer mesh).
        var result = mesh.Clone();

        // meshAngle: every Container / Crate family writes this on the BE. The BE's
        // own OnTesselation typically applies the rotation at draw time rather than
        // baking it into the cached mesh, so we apply it here.
        float angle = beTree.GetFloat("meshAngle", 0f);
        if (angle != 0f) result.Rotate(BlockCenter, 0f, angle, 0f);

        return result;
    }

    private static void SetBlockProperty(BlockEntity be, Block block)
    {
        var prop = typeof(BlockEntity).GetProperty("Block",
            BindingFlags.Public | BindingFlags.Instance);
        prop?.SetValue(be, block);
    }

    private static MeshData? ExtractOwnMesh(BlockEntity be)
    {
        foreach (var f in OwnMeshFieldsFor(be.GetType()))
        {
            var v = f.GetValue(be) as MeshData;
            if (v != null && v.VerticesCount > 0) return v;
        }
        return null;
    }

    /// <summary>
    /// Walk the BE's class hierarchy looking for the private mesh-builder method this
    /// family uses, then invoke it. Vanilla VS doesn't have a uniform interface, so
    /// we try the known names: GenMesh(tesselator) returns a mesh, loadOrCreateMesh()
    /// writes to ownMesh. Reflection lookups are cached per BE type so the price is
    /// paid once per family, not per cell.
    /// </summary>
    private static MeshData? TryBuildMeshDirectly(ICoreClientAPI capi, BlockEntity be)
    {
        var (genMesh, loadOrCreate) = MeshBuildersFor(be.GetType());

        if (genMesh != null)
        {
            try
            {
                var m = genMesh.Invoke(be, new object[] { capi.Tesselator }) as MeshData;
                if (m != null && m.VerticesCount > 0) return m;
            }
            catch (Exception ex)
            {
                FieldwrightLogger.Debug(capi, "be-mesh",
                    $"GenMesh(tesselator) on {genMesh.DeclaringType?.Name} threw: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        if (loadOrCreate != null)
        {
            try
            {
                loadOrCreate.Invoke(be, null);
                return ExtractOwnMesh(be);
            }
            catch (Exception ex)
            {
                FieldwrightLogger.Debug(capi, "be-mesh",
                    $"loadOrCreateMesh on {loadOrCreate.DeclaringType?.Name} threw: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        return null;
    }

    // Cache of per-BE-Type reflection lookups so the assembly walk runs once per family.
    private static readonly Dictionary<Type, (MethodInfo? genMesh, MethodInfo? loadOrCreate)> meshBuilderCache = new();
    private static readonly Dictionary<Type, List<FieldInfo>> ownMeshFieldCache = new();

    private static (MethodInfo? genMesh, MethodInfo? loadOrCreate) MeshBuildersFor(Type beType)
    {
        if (meshBuilderCache.TryGetValue(beType, out var cached)) return cached;

        MethodInfo? genMesh = null;
        MethodInfo? loadOrCreate = null;
        var t = beType;
        while (t != null && (genMesh == null || loadOrCreate == null))
        {
            if (genMesh == null)
            {
                var m = t.GetMethod("GenMesh",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null, types: new[] { typeof(ITesselatorAPI) }, modifiers: null);
                if (m != null && m.ReturnType == typeof(MeshData)) genMesh = m;
            }
            if (loadOrCreate == null)
            {
                loadOrCreate = t.GetMethod("loadOrCreateMesh",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null, types: Type.EmptyTypes, modifiers: null);
            }
            t = t.BaseType;
        }

        meshBuilderCache[beType] = (genMesh, loadOrCreate);
        return (genMesh, loadOrCreate);
    }

    private static List<FieldInfo> OwnMeshFieldsFor(Type beType)
    {
        if (ownMeshFieldCache.TryGetValue(beType, out var cached)) return cached;

        var fields = new List<FieldInfo>();
        var t = beType;
        while (t != null)
        {
            foreach (var name in OwnMeshFieldNames)
            {
                var f = t.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(MeshData) && f.DeclaringType == t)
                {
                    fields.Add(f);
                }
            }
            t = t.BaseType;
        }
        ownMeshFieldCache[beType] = fields;
        return fields;
    }
}

/// <summary>
/// Chisel / microblock mesh source for v0.1.3: render the substrate block (the first
/// material in the chisel's materials list) as a full cube. Phase 4 of the build-along
/// flow will swap this for the full per-voxel mesh via BlockEntityMicroBlock.CreateMesh.
///
/// We use the public static MaterialIdsFromAttributes helper to read substrate IDs
/// without instantiating a BE, then resolve to a Block via World.Blocks.
/// </summary>
public class ChiselSubstrateMeshSource : IBEMeshSource
{
    public string Prefix { get; }

    public ChiselSubstrateMeshSource(string prefix) { Prefix = prefix; }

    public MeshData? Provide(ICoreClientAPI capi, Block block, ITreeAttribute? beTree)
    {
        if (beTree == null) return null;

        var ids = ResolveMaterialIds(capi, beTree);
        if (ids == null || ids.Length == 0) return null;

        // ids[0] is the primary material the chisel was carved from. Resolve to a
        // Block; if the ID lookup fails, fall through to whatever default the caller
        // wants. Block.Id == 0 means "air", which means the chisel is empty / invalid;
        // skip in that case.
        int substrateId = ids[0];
        if (substrateId <= 0) return null;

        var substrate = capi.World.GetBlock(substrateId);
        if (substrate == null || substrate.Id == 0) return null;

        capi.Tesselator.TesselateBlock(substrate, out var mesh);
        if (mesh == null || mesh.VerticesCount == 0) return null;

        return mesh;
    }

    /// <summary>
    /// Mirrors BlockEntityMicroBlock.MaterialIdsFromAttributes via reflection so we
    /// don't take a hard dependency on VSSurvivalMod at compile time. Returns null
    /// if the helper isn't present (game version mismatch) so callers fall back
    /// gracefully instead of throwing.
    /// </summary>
    // Cache the resolved type + method after first lookup, both to skip the
    // assembly sweep on every cell and to leave an obvious diagnostic if the
    // type ever vanishes between game versions.
    private static System.Reflection.MethodInfo? cachedMaterialIdsMethod;
    private static bool materialIdsResolved;

    private static int[]? ResolveMaterialIds(ICoreClientAPI capi, ITreeAttribute tree)
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
                    catch { /* dynamic assemblies can throw on GetType; skip them */ }
                }
                if (t == null)
                {
                    FieldwrightLogger.Warn(capi, "be-mesh",
                        "BlockEntityMicroBlock type not found in any loaded assembly");
                    return null;
                }
                cachedMaterialIdsMethod = t.GetMethod("MaterialIdsFromAttributes",
                    BindingFlags.Public | BindingFlags.Static);
                if (cachedMaterialIdsMethod == null)
                {
                    FieldwrightLogger.Warn(capi, "be-mesh",
                        $"BlockEntityMicroBlock.MaterialIdsFromAttributes not found in {t.Assembly.GetName().Name}");
                    return null;
                }
            }

            if (cachedMaterialIdsMethod == null) return null;
            return cachedMaterialIdsMethod.Invoke(null, new object[] { tree, capi.World }) as int[];
        }
        catch (Exception ex)
        {
            FieldwrightLogger.Warn(capi, "be-mesh",
                $"MaterialIdsFromAttributes reflection failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Central lookup. Ordered list (NOT a dictionary) so longest-prefix-wins resolution is
/// explicit, e.g. "labeledchest" beats "chest". Block-code paths are matched with
/// StartsWith, so include enough of the prefix to disambiguate the family.
/// </summary>
public static class BEMeshSourceRegistry
{
    private static readonly List<IBEMeshSource> Sources = new List<IBEMeshSource>
    {
        // Microblock / chisel: substrate cube. Voxel-level rendering lands with v0.2.
        new ChiselSubstrateMeshSource("chiseledblock"),
        new ChiselSubstrateMeshSource("microblock"),

        // BE-only-mesh families: TesselateBlock returns zero verts so we have to
        // spin up a temp BE to get the real geometry.
        new TempBEMeshSource("crate"),
        new TempBEMeshSource("stationarybasket"),
        new TempBEMeshSource("genericcontainer"),

        // Real-tessellation families: TesselateBlock works, BE provides meshAngle.
        // Labeled chests come before plain chests so "labeledchest" wins the
        // longest-prefix race.
        new TesselateAndRotateMeshSource("labeledchest"),
        new TesselateAndRotateMeshSource("chest"),
        new TesselateAndRotateMeshSource("quern"),
        new TesselateAndRotateMeshSource("bed"),
    };

    public static IBEMeshSource? Lookup(string blockCodePath)
    {
        if (string.IsNullOrEmpty(blockCodePath)) return null;
        foreach (var s in Sources)
        {
            if (blockCodePath.StartsWith(s.Prefix, StringComparison.OrdinalIgnoreCase)) return s;
        }
        return null;
    }
}
