using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Fieldwright;

/// <summary>
/// Always-on HUD overlay showing build progress. Inherits from HudElement so the cursor
/// stays grabbed (the player keeps camera control while glancing at counts). One of three
/// view states the player cycles through with Ctrl+Shift+L:
///   HUD (default on place) -&gt; Modal (movable / scrollable / copyable) -&gt; Hidden.
/// </summary>
public class GhostChecklistHud : HudElement
{
    private const string Component = "checklist-hud";
    private const string ComposerKey = "fieldwright-checklist-hud";

    private readonly GhostMatchTracker tracker;
    private readonly string blueprintName;
    private readonly int hudOffsetX;
    private readonly int hudOffsetY;

    // Same input-suppression posture as the original HUD: don't grab focus, don't take
    // keyboard, don't react to clicks. Camera/movement stays uninterrupted.
    public override double DrawOrder => 0.2;
    public override bool Focusable => false;
    public override bool ShouldReceiveKeyboardEvents() => false;

    public override void OnMouseDown(MouseEvent args) { }
    public override void OnMouseUp(MouseEvent args) { }
    public override void OnMouseMove(MouseEvent args) { }
    protected override void OnFocusChanged(bool on) { }

    public GhostChecklistHud(ICoreClientAPI capi, GhostMatchTracker tracker,
        string blueprintName, int hudOffsetX, int hudOffsetY) : base(capi)
    {
        this.tracker = tracker;
        this.blueprintName = blueprintName;
        this.hudOffsetX = hudOffsetX;
        this.hudOffsetY = hudOffsetY;
        Compose();
    }

    private void Compose()
    {
        var dialogBounds = ElementBounds.Fixed(hudOffsetX, hudOffsetY, 260, 280)
            .WithAlignment(EnumDialogArea.LeftTop);
        var bgBounds = ElementBounds.Fill.WithFixedPadding(8);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(dialogBounds);

        var titleFont = CairoFont.WhiteDetailText();
        var bodyFont = CairoFont.WhiteSmallText();

        // Fully transparent backdrop, the player sees the world behind the HUD.
        Composers[ComposerKey] = capi.Gui.CreateCompo(ComposerKey, bgBounds)
            .BeginChildElements(dialogBounds)
                .AddStaticText($"Fieldwright: {blueprintName}", titleFont,
                    ElementBounds.Fixed(0, 0, 240, 20))
                .AddRichtext("", bodyFont,
                    ElementBounds.Fixed(0, 24, 240, 256), "body")
            .EndChildElements()
            .Compose();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        var rich = Composers[ComposerKey]?.GetRichtext("body");
        if (rich != null)
        {
            var (vtml, _) = GhostChecklistText.Build(tracker);
            rich.SetNewText(vtml, CairoFont.WhiteSmallText());
        }
        base.OnRenderGUI(deltaTime);
    }
}
