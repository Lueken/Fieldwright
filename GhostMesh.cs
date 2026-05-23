using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Builds a translucent ghost mesh for a schematic, split into one MultiTextureMeshRef
/// per Y layer so the renderer can hide layers from the top down (PgUp / PgDn) without
/// rebuilding the upload. Layer N contains every block whose dy == N in the schematic.
/// </summary>
public class GhostMesh : IDisposable
{
    private const string Component = "ghost-mesh";

    // BlockSchematic position bit-packing, see BlockSchematic.PosBitMask + Pack().
    private const uint PosBitMask = 0x3ff;

    private readonly ICoreClientAPI capi;
    private MultiTextureMeshRef?[] layerMeshes;

    public Vec3i Size { get; }
    public Vec3i AnchorOffset { get; }
    public int BlockCount { get; private set; }
    public int SkippedCount { get; private set; }

    public int LayerCount => layerMeshes?.Length ?? 0;
    public MultiTextureMeshRef? GetLayerMesh(int layer) =>
        (layer >= 0 && layer < layerMeshes.Length) ? layerMeshes[layer] : null;

    public bool HasMesh
    {
        get
        {
            if (layerMeshes == null) return false;
            foreach (var m in layerMeshes)
                if (m != null && m.Initialized) return true;
            return false;
        }
    }

    public GhostMesh(ICoreClientAPI capi, BlockSchematic schematic, Vec3i anchorOffset, float alpha = 0.3f)
    {
        this.capi = capi;
        this.Size = new Vec3i(schematic.SizeX, schematic.SizeY, schematic.SizeZ);
        this.AnchorOffset = anchorOffset;
        this.layerMeshes = new MultiTextureMeshRef?[Math.Max(1, schematic.SizeY)];

        BuildMesh(schematic, alpha);
    }

    private void BuildMesh(BlockSchematic schematic, float alpha)
    {
        // One MeshData per Y layer; each accumulates that layer's blocks before upload.
        var perLayer = new MeshData[schematic.SizeY];
        for (int y = 0; y < schematic.SizeY; y++)
        {
            perLayer[y] = new MeshData(256, 512);
            perLayer[y].WithColorMaps();
            perLayer[y].WithXyzFaces();
            perLayer[y].WithRenderpasses();
        }

        byte alphaByte = (byte)Math.Clamp((int)(alpha * 255), 0, 255);

        int built = 0;
        int skipped = 0;

        for (int i = 0; i < schematic.Indices.Count; i++)
        {
            uint encoded = schematic.Indices[i];
            int dx = (int)(encoded & PosBitMask);
            int dy = (int)((encoded >> 20) & PosBitMask);
            int dz = (int)((encoded >> 10) & PosBitMask);

            if (dy < 0 || dy >= schematic.SizeY)
            {
                skipped++;
                continue;
            }

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

            // Keep dy in the per-block offset so the model matrix in GhostRenderer
            // continues to position each block at its absolute schematic-local Y.
            // Each layer's mesh has vertices only within Y in [dy, dy+1].
            perLayer[dy].AddMeshData(blockMesh, dx, dy, dz);
            built++;
        }

        BlockCount = built;
        SkippedCount = skipped;

        int uploadedLayers = 0;
        for (int y = 0; y < schematic.SizeY; y++)
        {
            if (perLayer[y].VerticesCount == 0) continue;
            layerMeshes[y] = capi.Render.UploadMultiTextureMesh(perLayer[y]);
            uploadedLayers++;
        }

        if (uploadedLayers == 0)
        {
            FieldwrightLogger.Warn(capi, Component, "ghost mesh has zero vertices, nothing to render");
            return;
        }

        FieldwrightLogger.Info(capi, Component,
            $"built ghost mesh: {built} blocks, {skipped} skipped across {uploadedLayers} non-empty Y layers, " +
            $"size {Size.X}×{Size.Y}×{Size.Z}");
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
        if (layerMeshes != null)
        {
            for (int i = 0; i < layerMeshes.Length; i++)
            {
                layerMeshes[i]?.Dispose();
                layerMeshes[i] = null;
            }
        }
    }
}
