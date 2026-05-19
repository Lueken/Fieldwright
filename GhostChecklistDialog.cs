using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Fieldwright;

/// <summary>
/// HUD overlay showing the active ghost's build progress. Inherits from HudElement
/// so it doesn't grab focus / lock the cursor. Top-left, compact, semi-transparent.
///
/// Materials are grouped by block.FirstCodePart() — e.g., all variants of slanted
/// roofing share one row. Coarser than per-variant tracking but matches builder
/// intuition ("how many of THIS thing do I need").
/// </summary>
public class GhostChecklistDialog : HudElement
{
    private const string Component = "checklist-hud";
    private const int MaxRows = 12;

    private readonly GhostMatchTracker tracker;
    private readonly string blueprintName;

    // Lean on HudElement defaults for input behavior — those defaults (null toggle
    // code, PrefersUngrabbedMouse=false, mouse-look stays grabbed) match the
    // vanilla boss-health-bar pattern. Earlier overrides of PrefersUngrabbedMouse
    // and DisableMouseGrab were releasing the cursor on open, which broke camera
    // look. Only override what's needed to make the dialog non-interactive.
    public override double DrawOrder => 0.2;
    public override bool Focusable => false;
    public override bool ShouldReceiveKeyboardEvents() => false;

    public override void OnMouseDown(MouseEvent args) { /* HUD is non-interactive — let game handle clicks */ }
    public override void OnMouseUp(MouseEvent args) { }
    public override void OnMouseMove(MouseEvent args) { }
    protected override void OnFocusChanged(bool on) { }

    public GhostChecklistDialog(ICoreClientAPI capi, GhostMatchTracker tracker, string blueprintName) : base(capi)
    {
        this.tracker = tracker;
        this.blueprintName = blueprintName;
        Compose();
        FieldwrightLogger.Info(capi, Component, $"checklist HUD opened for '{blueprintName}'");
    }

    private void Compose()
    {
        // Compact panel — top-left, ~260px wide. RichText handles VTML markup
        // (<font color>...) so we get inline color-coding for free.
        var dialogBounds = ElementBounds.Fixed(8, 60, 260, 280)
            .WithAlignment(EnumDialogArea.LeftTop);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(8);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(dialogBounds);

        var titleFont = CairoFont.WhiteDetailText();
        var bodyFont = CairoFont.WhiteSmallText();

        // No AddDialogBG → fully transparent backdrop. Text renders directly. If
        // legibility suffers against bright skies, swap in AddInset for a faint
        // dark wash. (AddShadedDialogBG is too opaque per user feedback.)
        Composers["fieldwright-checklist"] =
            capi.Gui.CreateCompo("fieldwright-checklist", bgBounds)
                .BeginChildElements(dialogBounds)
                    .AddStaticText($"Fieldwright — {blueprintName}", titleFont,
                        ElementBounds.Fixed(0, 0, 240, 20))
                    .AddRichtext("", bodyFont,
                        ElementBounds.Fixed(0, 24, 240, 256), "body")
                .EndChildElements()
                .Compose();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        UpdateText();
        base.OnRenderGUI(deltaTime);
    }

    private void UpdateText()
    {
        var rich = Composers["fieldwright-checklist"]?.GetRichtext("body");
        if (rich == null) return;

        var sb = new StringBuilder();

        // Progress summary
        int totalCells = tracker.TotalBlockCells + tracker.TotalAirCells;
        int doneCells = tracker.MatchedBlocks + (tracker.TotalAirCells - tracker.AirViolationCount);
        float pct = totalCells > 0 ? (doneCells * 100f / totalCells) : 100f;
        sb.AppendLine($"<font color=\"#aaffaa\">Progress: {doneCells} / {totalCells} ({pct:F0}%)</font>");

        // Tracker already aggregates by group key (e.g. "slantedroofing"), so the
        // HUD just renders each entry directly. Inventory counts come from the
        // tracker for the same reason — matching and display share the group key.
        var needs = tracker.GetMaterialNeeds();
        if (needs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"<font color=\"#ffffff\">Materials needed:</font>");

            // Sort by group name for stable display order.
            var sorted = new List<KeyValuePair<string, int>>(needs);
            sorted.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.Ordinal));

            int rowsShown = 0;
            foreach (var kvp in sorted)
            {
                if (rowsShown >= MaxRows) break;
                int have = tracker.CountInInventory(kvp.Key);
                int need = kvp.Value;
                string color = have >= need ? "#aaffaa" : "#ffaaaa";
                sb.AppendLine($"<font color=\"{color}\">  {kvp.Key}: {have} / {need}</font>");
                rowsShown++;
            }
            if (sorted.Count > MaxRows)
            {
                sb.AppendLine($"<font color=\"#aaaaaa\">  …and {sorted.Count - MaxRows} more</font>");
            }
        }

        if (tracker.AirViolationCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"<font color=\"#ff8888\">Blocks to remove: {tracker.AirViolationCount}</font>");
        }

        if (tracker.IsComplete)
        {
            sb.AppendLine();
            sb.AppendLine("<font color=\"#aaffaa\">Structure complete!</font>");
        }

        rich.SetNewText(sb.ToString(), CairoFont.WhiteSmallText());
    }

}
