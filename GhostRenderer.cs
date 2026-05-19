using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Fieldwright;

/// <summary>
/// Renders an uploaded ghost mesh with alpha blending. Owns its IRenderer
/// lifecycle — register on construction, unregister on Dispose.
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

    /// <summary>True while the ghost follows the crosshair and auto-rotates.</summary>
    public bool IsFloating { get; private set; }

    public double RenderOrder => 0.5;
    public int RenderRange => 256;

    public GhostRenderer(
        ICoreClientAPI capi,
        GhostMesh ghost,
        BlockPos initialOrigin,
        Vec3i anchorOffset,
        BlockFacing? savedAnchorFace,
        bool startFloating)
    {
        this.capi = capi;
        this.ghost = ghost;
        this.Origin = initialOrigin.Copy();
        this.anchorOffset = anchorOffset;
        this.savedAnchorFace = savedAnchorFace;
        this.IsFloating = startFloating;
        this.RotationY = 0f;

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
        FieldwrightLogger.Info(capi, Component, "ghost unplaced — back to floating mode");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!ghost.HasMesh || ghost.MeshRef == null) return;
        if (stage != EnumRenderStage.Opaque) return;

        // Floating mode: chase the crosshair and auto-rotate based on player yaw.
        if (IsFloating)
        {
            var sel = capi.World.Player?.CurrentBlockSelection;
            if (sel?.Position != null && sel.Face != null)
            {
                // Position: anchor lands ADJACENT to the targeted block on the side of
                // the face the player is aiming at — same semantics as placing a real
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
                // point AT the player — i.e., opposite their look direction. This
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

        // Build model matrix:
        //   1. Subtract anchorOffset → anchor cell at mesh origin
        //   2. Recenter horizontally (+0.5, 0, +0.5) so we rotate around the anchor's center
        //   3. RotateY
        //   4. Undo recenter (-0.5, 0, -0.5)
        //   5. Translate to Origin world position (camera-relative)
        // (matrix chain is post-multiply: leftmost ops apply LAST to the vertex)
        prog.ModelMatrix = modelMat
            .Identity()
            .Translate(Origin.X - camPos.X, Origin.Y - camPos.Y, Origin.Z - camPos.Z)
            .Translate(0.5f, 0f, 0.5f)
            .RotateY(RotationY)
            .Translate(-0.5f, 0f, -0.5f)
            .Translate(-anchorOffset.X, -anchorOffset.Y, -anchorOffset.Z)
            .Values;

        try
        {
            rpi.RenderMultiTextureMesh(ghost.MeshRef, "tex");
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
