using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Active mirror axis for a floating or placed ghost. Cycles via Ctrl+Shift+M.
/// Mirror is applied in the schematic's anchor-block-centered local frame BEFORE
/// rotation, so the player sees a mirror-around-anchor flip regardless of how the
/// ghost is rotated. The tracker uses the same axis to compute expected positions
/// on confirmation, so match detection stays in sync.
/// </summary>
public enum MirrorAxis
{
    None,
    X,
    Y,
    Z,
}

/// <summary>
/// Renders an uploaded ghost mesh with alpha blending. Owns its IRenderer
/// lifecycle, register on construction, unregister on Dispose.
///
/// Two modes:
///   - Floating: ghost follows the player's look-target each frame. The Y
///     rotation auto-aligns so the anchor's saved player-facing face matches
///     whichever block face the crosshair is on. Used right after .fw paste.
///   - Placed: position + rotation locked. Set via .fw place / Ctrl+Shift+P.
///
/// Rotation pivots at the center of the anchor cell (in mesh-local space).
/// Vertical-anchor-face captures don't trigger rotation; ghost renders at 0°.
/// </summary>
public class GhostRenderer : IRenderer
{
    private const string Component = "ghost-render";

    private readonly ICoreClientAPI capi;
    private readonly GhostMesh ghost;
    private readonly Matrixf modelMat = new Matrixf();
    private readonly Vec3i anchorOffset;
    private readonly BlockFacing? savedAnchorFace;

    /// <summary>World-space block position where the anchor cell should land.</summary>
    public BlockPos Origin { get; private set; }

    /// <summary>Y rotation in radians. 0 when no auto-rotate is active.</summary>
    public float RotationY { get; private set; }

    /// <summary>Active mirror axis. Cycled via Ctrl+Shift+M.</summary>
    public MirrorAxis CurrentMirror { get; private set; } = MirrorAxis.None;

    /// <summary>
    /// How many bottom layers of the ghost mesh are rendered. Range [0, ghost.LayerCount].
    /// PgDn decreases (peels layers off the top), PgUp increases (restores). Doesn't affect
    /// match detection or air-violation tracking, purely a visual filter so the player can
    /// see interior layers of tall builds.
    /// </summary>
    public int VisibleLayers { get; private set; }

    /// <summary>True while the ghost follows the crosshair and auto-rotates.</summary>
    public bool IsFloating { get; private set; }

    public double RenderOrder => 0.5;
    public int RenderRange { get; }

    public GhostRenderer(
        ICoreClientAPI capi,
        GhostMesh ghost,
        BlockPos initialOrigin,
        Vec3i anchorOffset,
        BlockFacing? savedAnchorFace,
        bool startFloating,
        int renderRange)
    {
        this.capi = capi;
        this.ghost = ghost;
        this.Origin = initialOrigin.Copy();
        this.anchorOffset = anchorOffset;
        this.savedAnchorFace = savedAnchorFace;
        this.IsFloating = startFloating;
        this.RotationY = 0f;
        this.RenderRange = renderRange;
        this.VisibleLayers = ghost.LayerCount;

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "fieldwright-ghost");
        FieldwrightLogger.Info(capi, Component,
            $"registered ghost renderer at ({initialOrigin.X}, {initialOrigin.Y}, {initialOrigin.Z}); " +
            $"floating={startFloating}, savedFace={savedAnchorFace?.Code ?? "none"}, " +
            $"anchorOffset=({anchorOffset.X},{anchorOffset.Y},{anchorOffset.Z})");
    }

    /// <summary>Lock the current position + rotation; stop following the crosshair.</summary>
    public void Place()
    {
        IsFloating = false;
        FieldwrightLogger.Info(capi, Component,
            $"ghost placed at ({Origin.X}, {Origin.Y}, {Origin.Z}), rotationY={MathF.Round(RotationY * 180f / MathF.PI)}°");
    }

    /// <summary>Re-enter floating mode; ghost resumes following the crosshair.</summary>
    public void Unplace()
    {
        IsFloating = true;
        FieldwrightLogger.Info(capi, Component, "ghost unplaced, back to floating mode");
    }

    /// <summary>
    /// Advance the mirror state through the cycle: None → X → Y → Z → None. Allowed in
    /// both floating and placed modes, but in placed mode the caller (FieldwrightModSystem)
    /// is responsible for rebuilding the match tracker so expected positions follow.
    /// </summary>
    public void CycleMirror()
    {
        CurrentMirror = CurrentMirror switch
        {
            MirrorAxis.None => MirrorAxis.X,
            MirrorAxis.X => MirrorAxis.Y,
            MirrorAxis.Y => MirrorAxis.Z,
            MirrorAxis.Z => MirrorAxis.None,
            _ => MirrorAxis.None,
        };
        FieldwrightLogger.Info(capi, Component, $"mirror cycled to {CurrentMirror}");
    }

    /// <summary>Peel one layer off the top of the ghost. Bottoms out at 0 (nothing visible).</summary>
    public void DecreaseVisibleLayers()
    {
        if (VisibleLayers <= 0) return;
        VisibleLayers--;
        FieldwrightLogger.Debug(capi, Component, $"visible layers: {VisibleLayers}/{ghost.LayerCount}");
    }

    /// <summary>Add a layer back to the top. Caps at LayerCount (full ghost visible).</summary>
    public void IncreaseVisibleLayers()
    {
        if (VisibleLayers >= ghost.LayerCount) return;
        VisibleLayers++;
        FieldwrightLogger.Debug(capi, Component, $"visible layers: {VisibleLayers}/{ghost.LayerCount}");
    }

    /// <summary>Restore the full ghost (all layers visible).</summary>
    public void ShowAllLayers()
    {
        VisibleLayers = ghost.LayerCount;
        FieldwrightLogger.Debug(capi, Component, $"visible layers reset to {VisibleLayers}");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!ghost.HasMesh) return;
        if (stage != EnumRenderStage.Opaque) return;
        if (VisibleLayers <= 0) return;

        // Floating mode: chase the crosshair and auto-rotate based on player yaw.
        if (IsFloating)
        {
            var sel = capi.World.Player?.CurrentBlockSelection;
            if (sel?.Position != null && sel.Face != null)
            {
                // Position: anchor lands ADJACENT to the targeted block on the side of
                // the face the player is aiming at, same semantics as placing a real
                // block. Aim at top of ground → ghost sits on top (Y+1). Aim at a
                // wall's south face → ghost sits south of that wall (Z+1).
                var n = sel.Face.Normali;
                Origin = new BlockPos(
                    sel.Position.X + n.X,
                    sel.Position.Y + n.Y,
                    sel.Position.Z + n.Z,
                    sel.Position.dimension);

                // Rotation: derived from player yaw (where the player is facing),
                // not the targeted block face. The anchor's saved front face should
                // point AT the player, i.e., opposite their look direction. This
                // works for aiming at horizontal walls AND at vertical surfaces like
                // the top of a ground block, because we don't depend on the block face.
                if (savedAnchorFace != null && savedAnchorFace.IsHorizontal)
                {
                    var playerEntityPos = capi.World.Player?.Entity?.Pos;
                    if (playerEntityPos != null)
                    {
                        var playerLook = BlockFacing.HorizontalFromYaw(playerEntityPos.Yaw);
                        var towardPlayer = playerLook.Opposite;
                        int delta = towardPlayer.HorizontalAngleIndex - savedAnchorFace.HorizontalAngleIndex;
                        RotationY = delta * (MathF.PI / 2f);
                    }
                }
                else
                {
                    RotationY = 0f;
                }
            }
            // No look-target: keep last Origin + RotationY so the ghost doesn't snap to nothing.
        }

        var rpi = capi.Render;
        var player = capi.World.Player;
        if (player?.Entity == null) return;
        var camPos = player.Entity.CameraPos;

        rpi.GlDisableCullFace();
        rpi.GlToggleBlend(true);

        var prog = rpi.PreparedStandardShader(Origin.X, Origin.Y, Origin.Z);

        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

        // Build model matrix (post-multiply chain: lines below execute on the vertex first):
        //   1. T_anchor: subtract anchorOffset → anchor cell at mesh origin
        //   2. T_mirror_center (-0.5,-0.5,-0.5): anchor BLOCK CENTER at origin
        //   3. Scale by mirror axis (±1 each component), mirror in anchor-centered local frame
        //   4. T_mirror_uncenter (+0.5,+0.5,+0.5): restore anchor cell to [0,1]^3
        //   5. T_rot_center (-0.5, 0, -0.5): rotation pivot at anchor XZ center
        //   6. RotateY
        //   7. T_rot_uncenter (+0.5, 0, +0.5)
        //   8. T_world: translate to world (camera-relative)
        // For Mirror=None, steps 2-4 collapse to a no-op (Scale 1,1,1).
        // Cull-face is already disabled (line below), so negative scale flipping winding
        // order doesn't break rendering.
        float mx = CurrentMirror == MirrorAxis.X ? -1f : 1f;
        float my = CurrentMirror == MirrorAxis.Y ? -1f : 1f;
        float mz = CurrentMirror == MirrorAxis.Z ? -1f : 1f;

        prog.ModelMatrix = modelMat
            .Identity()
            .Translate(Origin.X - camPos.X, Origin.Y - camPos.Y, Origin.Z - camPos.Z)
            .Translate(0.5f, 0f, 0.5f)
            .RotateY(RotationY)
            .Translate(-0.5f, 0f, -0.5f)
            .Translate(0.5f, 0.5f, 0.5f)
            .Scale(mx, my, mz)
            .Translate(-0.5f, -0.5f, -0.5f)
            .Translate(-anchorOffset.X, -anchorOffset.Y, -anchorOffset.Z)
            .Values;

        try
        {
            // Draw one mesh per visible Y layer, bottom-up. Same model matrix applies to all
            // because each layer's vertices already carry their schematic-local dy offset.
            for (int y = 0; y < VisibleLayers; y++)
            {
                var layerMesh = ghost.GetLayerMesh(y);
                if (layerMesh == null || !layerMesh.Initialized) continue;
                rpi.RenderMultiTextureMesh(layerMesh, "tex");
            }
        }
        catch (Exception ex)
        {
            FieldwrightLogger.Error(capi, Component, $"render call failed: {ex.Message}");
        }

        prog.Stop();
        rpi.GlEnableCullFace();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        FieldwrightLogger.Info(capi, Component, "unregistered ghost renderer");
    }
}
