using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Builds a combined translucent MeshData from every block in a schematic.
/// Owns the uploaded MultiTextureMeshRef and disposes it when no longer needed.
///
/// Phase 2a: ghost geometry only — no per-block-entity state rendering yet
/// (chests render as default closed shells, chiseled blocks render as their
/// raw shape). That comes in Phase 4.
/// </summary>
public class GhostMesh : IDisposable
{
    private const string Component = "ghost-mesh";

    // BlockSchematic position bit-packing — see BlockSchematic.PosBitMask + Pack().
    private const uint PosBitMask = 0x3ff;

    private readonly ICoreClientAPI capi;
    private MultiTextureMeshRef? meshRef;

    public Vec3i Size { get; }
    public Vec3i AnchorOffset { get; }
    public int BlockCount { get; private set; }
    public int SkippedCount { get; private set; }

    public MultiTextureMeshRef? MeshRef => meshRef;
    public bool HasMesh => meshRef != null && meshRef.Initialized;

    public GhostMesh(ICoreClientAPI capi, BlockSchematic schematic, Vec3i anchorOffset, float alpha = 0.3f)
    {
        this.capi = capi;
        this.Size = new Vec3i(schematic.SizeX, schematic.SizeY, schematic.SizeZ);
        this.AnchorOffset = anchorOffset;

        BuildMesh(schematic, alpha);
    }

    private void BuildMesh(BlockSchematic schematic, float alpha)
    {
        var combined = new MeshData(1024, 2048);
        combined.WithColorMaps();
        combined.WithXyzFaces();
        combined.WithRenderpasses();

        byte alphaByte = (byte)Math.Clamp((int)(alpha * 255), 0, 255);

        int built = 0;
        int skipped = 0;

        for (int i = 0; i < schematic.Indices.Count; i++)
        {
            uint encoded = schematic.Indices[i];
            int dx = (int)(encoded & PosBitMask);
            int dy = (int)((encoded >> 20) & PosBitMask);
            int dz = (int)((encoded >> 10) & PosBitMask);

            int storedId = schematic.BlockIds[i];
            if (!schematic.BlockCodes.TryGetValue(storedId, out var assetLoc))
            {
                skipped++;
                continue;
            }

            var block = capi.World.GetBlock(assetLoc);
            if (block == null || block.Id == 0)
            {
                skipped++;
                continue;
            }

            MeshData blockMesh;
            try
            {
                capi.Tesselator.TesselateBlock(block, out blockMesh);
            }
            catch (Exception ex)
            {
                FieldwrightLogger.Warn(capi, Component,
                    $"failed to tessellate {assetLoc}: {ex.Message}");
                skipped++;
                continue;
            }

            if (blockMesh == null || blockMesh.VerticesCount == 0)
            {
                skipped++;
                continue;
            }

            ApplyAlpha(blockMesh, alphaByte);

            combined.AddMeshData(blockMesh, dx, dy, dz);
            built++;
        }

        BlockCount = built;
        SkippedCount = skipped;

        if (combined.VerticesCount == 0)
        {
            FieldwrightLogger.Warn(capi, Component, "ghost mesh has zero vertices — nothing to render");
            return;
        }

        meshRef = capi.Render.UploadMultiTextureMesh(combined);

        FieldwrightLogger.Info(capi, Component,
            $"built ghost mesh: {built} blocks, {skipped} skipped, " +
            $"{combined.VerticesCount} verts, {combined.IndicesCount} indices, size {Size.X}×{Size.Y}×{Size.Z}");
    }

    private static void ApplyAlpha(MeshData mesh, byte alphaByte)
    {
        if (mesh.Rgba == null) return;
        // Rgba layout: 4 bytes per vertex (R, G, B, A). Multiply existing alpha by
        // (alphaByte / 255) so already-translucent blocks (glass, etc.) stay
        // proportionally see-through.
        for (int i = 3; i < mesh.Rgba.Length; i += 4)
        {
            int existing = mesh.Rgba[i];
            mesh.Rgba[i] = (byte)((existing * alphaByte) / 255);
        }
    }

    public void Dispose()
    {
        meshRef?.Dispose();
        meshRef = null;
    }
}
