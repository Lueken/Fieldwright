using Vintagestory.API.Client;

namespace Fieldwright;

/// <summary>
/// Movable, scrollable dialog showing the active ghost's build progress. Replaces the
/// always-visible HUD overlay. Toggled with Ctrl+Shift+L (or the build-checklist hotkey),
/// the player opens it to check progress / drag it somewhere convenient / copy the list
/// to clipboard, then closes it to keep building with cursor grabbed.
///
/// Materials are grouped by block.FirstCodePart(), e.g. all variants of slanted roofing
/// share one row. Chiseled cells contribute to their substrate's row (oak plank etc.)
/// so phase-1 build counts are accurate without needing to think about chisel detail.
/// </summary>
public class GhostChecklistDialog : GuiDialog
{
    private const string Component = "checklist-ui";
    private const string ComposerKey = "fieldwright-checklist";

    // Dialog geometry. Width is comfortable for material names + counts.
    // Body height covers about 20 rows before scrolling kicks in.
    private const int DialogWidth = 320;
    private const int BodyHeight = 360;
    private const int TitleBarPad = 18;
    private const int ScrollbarWidth = 20;
    private const int FooterHeight = 44;
    private const int RowFontSize = 14;

    private readonly GhostMatchTracker tracker;
    private readonly string blueprintName;
    private readonly int initialOffsetX;
    private readonly int initialOffsetY;

    // Latest known content height in pixels so the scrollbar can scale. Updated
    // every time the body text rebuilds. Zero means "no content yet".
    private float currentContentHeight;

    // Latest scroll value (0 = top), echoed into the body's fixedY offset every
    // time the scrollbar moves so the clip window pans over the text.
    private float currentScroll;

    public override string? ToggleKeyCombinationCode => null;
    public override double DrawOrder => 0.25;

    public GhostChecklistDialog(ICoreClientAPI capi, GhostMatchTracker tracker,
        string blueprintName, int hudOffsetX, int hudOffsetY) : base(capi)
    {
        this.tracker = tracker;
        this.blueprintName = blueprintName;
        this.initialOffsetX = hudOffsetX;
        this.initialOffsetY = hudOffsetY;
        Compose();
        FieldwrightLogger.Info(capi, Component,
            $"checklist dialog ready for '{blueprintName}' at ({hudOffsetX},{hudOffsetY})");
    }

    private void Compose()
    {
        // Discard any prior composer to avoid the Cairo blur crash that hits when the
        // title bar re-renders on a reused composer (same gotcha as SchematicLibraryDialog).
        Composers[ComposerKey]?.Dispose();

        // Dialog positioned at the configured corner offset on first open. After the
        // player drags the title bar, VS updates dialogBounds.fixedX/Y itself; we don't
        // need to track or persist the drag for v0.1.3.
        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.LeftTop)
            .WithFixedAlignmentOffset(initialOffsetX, initialOffsetY);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        int totalH = TitleBarPad + BodyHeight + FooterHeight;
        var contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, totalH);
        bgBounds.WithChildren(contentBounds);

        // Body: a clipped view onto a tall Richtext element. The clipping bounds set
        // the visible window; the Richtext can extend below for scroll. Scrollbar
        // lives to the right of the clip, fixed-width.
        int bodyY = TitleBarPad + 4;
        var clipBounds = ElementBounds.Fixed(0, bodyY, DialogWidth - ScrollbarWidth - 4, BodyHeight);
        var insideClipBounds = ElementBounds.Fixed(0, 0, DialogWidth - ScrollbarWidth - 8, BodyHeight)
            .WithFixedPadding(0, 0);
        var scrollbarBounds = ElementBounds.Fixed(DialogWidth - ScrollbarWidth, bodyY,
            ScrollbarWidth, BodyHeight);

        int footerY = bodyY + BodyHeight + 8;
        var copyBtnBounds = ElementBounds.Fixed(0, footerY, 110, 30);
        var hintBounds = ElementBounds.Fixed(120, footerY + 6, DialogWidth - 130, 24);

        var bodyFont = CairoFont.WhiteSmallText();

        var composer = capi.Gui.CreateCompo(ComposerKey, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar($"Fieldwright: {blueprintName}", OnTitleBarClose)
            .BeginChildElements(contentBounds)
                .BeginClip(clipBounds)
                    .AddRichtext("", bodyFont, insideClipBounds, "body")
                .EndClip()
                .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scroll")
                .AddSmallButton("Copy to clipboard", OnCopyClicked, copyBtnBounds,
                    EnumButtonStyle.Normal, "copyBtn")
                .AddStaticText("Drag title to move", CairoFont.WhiteDetailText(),
                    hintBounds)
            .EndChildElements()
            .Compose();

        Composers[ComposerKey] = composer;

        // Initial scroll state. Sizes get refined the first time UpdateText runs and
        // we know the real Richtext height.
        var scrollbar = composer.GetScrollbar("scroll");
        scrollbar?.SetHeights(BodyHeight, BodyHeight);
    }

    public override void OnRenderGUI(float deltaTime)
    {
        UpdateText();
        base.OnRenderGUI(deltaTime);
    }

    private void UpdateText()
    {
        var composer = Composers[ComposerKey];
        var rich = composer?.GetRichtext("body");
        if (rich == null) return;

        var (vtml, _) = GhostChecklistText.Build(tracker);
        rich.SetNewText(vtml, CairoFont.WhiteSmallText());

        // After SetNewText the Richtext recomputes its inner content height. Push that
        // into the scrollbar so the thumb sizes correctly.
        rich.Bounds.CalcWorldBounds();
        float contentH = (float)rich.Bounds.fixedHeight;
        if (contentH <= 0) contentH = BodyHeight;
        if (contentH != currentContentHeight)
        {
            currentContentHeight = contentH;
            var scrollbar = composer!.GetScrollbar("scroll");
            scrollbar?.SetHeights(BodyHeight, contentH);
        }
    }

    private void OnScroll(float value)
    {
        currentScroll = value;
        var body = Composers[ComposerKey]?.GetRichtext("body");
        if (body == null) return;
        // Scroll by translating the Richtext upward inside the clip. fixedY = -value
        // pans the content up as the scrollbar moves down.
        body.Bounds.fixedY = -value;
        body.Bounds.CalcWorldBounds();
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private bool OnCopyClicked()
    {
        var (_, plain) = GhostChecklistText.Build(tracker);
        var header = $"Fieldwright: {blueprintName}\n";
        capi.Forms.SetClipboardText(header + plain);
        FieldwrightLogger.Info(capi, Component,
            $"copied checklist for '{blueprintName}' to clipboard ({plain.Length} chars)");
        return true;
    }
}
