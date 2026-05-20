using System;
using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Fieldwright;

/// <summary>
/// Library browser for saved blueprints. Lists every `.json` blueprint in the user's
/// Blueprints directory with size, block count, anchor face, and modified time.
/// Per-row Paste and Delete actions; matching-mode dropdown at the top sets the mode
/// used by the Paste action (defaults to the user's config setting).
///
/// Pagination instead of scrollbar to keep the composer-bounds tree simple, VS GUI
/// recompose is finicky when many child bounds change at once.
/// </summary>
public class SchematicLibraryDialog : GuiDialog
{
    private const string Component = "library-ui";
    private const string ComposerKey = "fieldwright-library";
    private const int RowsPerPage = 6;
    private const int RowHeight = 56;
    private const int DialogWidth = 640;
    private const int TitleBarPad = 18;
    private const int HeaderHeight = 70 + TitleBarPad;
    private const int FooterHeight = 50;
    private const int ContentPad = 8;

    private readonly FieldwrightModSystem owner;

    private List<BlueprintEntry> entries = new();
    private int pageIndex = 0;
    private MatchingMode chosenMode;
    /// <summary>Entry index awaiting a second Delete click. -1 = none. Cleared on
    /// page nav, refresh, or any non-delete click on the same row.</summary>
    private int pendingDeleteIndex = -1;
    private HotkeyHelpDialog? helpDialog;

    public override string? ToggleKeyCombinationCode => null;
    public override double DrawOrder => 0.25;

    public SchematicLibraryDialog(ICoreClientAPI capi, FieldwrightModSystem owner) : base(capi)
    {
        this.owner = owner;
        this.chosenMode = owner.GetDefaultMatchingMode();
        Refresh();
    }

    /// <summary>True if either the library dialog OR its hotkey help modal is open.</summary>
    public bool IsAnythingOpen()
    {
        return IsOpened() || (helpDialog != null && helpDialog.IsOpened());
    }

    /// <summary>Close the library + the hotkey help modal if either is open.</summary>
    public void CloseAll()
    {
        if (helpDialog != null && helpDialog.IsOpened()) helpDialog.TryClose();
        if (IsOpened()) TryClose();
    }

    /// <summary>Re-scan the blueprints directory and rebuild the composer.</summary>
    public void Refresh()
    {
        entries = BlueprintStore.ListWithMetadata(capi);
        if (entries.Count == 0) pageIndex = 0;
        else if (pageIndex * RowsPerPage >= entries.Count) pageIndex = (entries.Count - 1) / RowsPerPage;
        pendingDeleteIndex = -1;
        Compose();
    }

    private int PageCount => Math.Max(1, (entries.Count + RowsPerPage - 1) / RowsPerPage);

    private void Compose()
    {
        // Dispose stale composer before rebuilding. Reusing a composer across Compose()
        // calls corrupts internal bounds state and causes a Cairo blur crash on the
        // title-bar text the second time the dialog opens.
        Composers[ComposerKey]?.Dispose();

        int rowsHeight = RowsPerPage * RowHeight;
        int contentH = HeaderHeight + rowsHeight + FooterHeight;

        var dialogBounds = ElementStdBounds.AutosizedMainDialog;
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        // Single fixed-size content area; everything else is parented to it. Child
        // bounds with Fixed(x, y) are interpreted relative to contentBounds origin.
        var contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, contentH);
        bgBounds.WithChildren(contentBounds);

        var titleFont = CairoFont.WhiteSmallishText();
        var rowFont = CairoFont.WhiteSmallText();
        var dimFont = CairoFont.WhiteDetailText();

        // Header row, TitleBarPad keeps content from overlapping the dialog title bar.
        var modeLabelBounds = ElementBounds.Fixed(0, TitleBarPad + 14, 220, 24);
        var modeDropdownBounds = ElementBounds.Fixed(225, TitleBarPad + 10, 230, 30);
        var countLabelBounds = ElementBounds.Fixed(470, TitleBarPad + 14, 170, 24);

        var composer = capi.Gui.CreateCompo(ComposerKey, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Fieldwright, Blueprint Library", OnTitleBarClose)
            .BeginChildElements(contentBounds)
                .AddStaticText("Default matching mode:", titleFont, modeLabelBounds)
                .AddDropDown(
                    new[] { "loose", "medium", "strict" },
                    new[] { "Loose (family)", "Medium (variant-aware)", "Strict (exact)" },
                    Math.Clamp((int)chosenMode, 0, 2),
                    OnMatchingModeChanged,
                    modeDropdownBounds,
                    "modeDropdown")
                .AddStaticText(
                    entries.Count == 0
                        ? "No blueprints saved yet."
                        : $"{entries.Count} saved",
                    rowFont, countLabelBounds);

        // Per-row entries for the current page.
        int firstRow = pageIndex * RowsPerPage;
        int lastRow = Math.Min(firstRow + RowsPerPage, entries.Count);

        for (int r = 0; r < RowsPerPage; r++)
        {
            int entryIndex = firstRow + r;
            if (entryIndex >= lastRow) break;

            int rowY = HeaderHeight + r * RowHeight;
            var entry = entries[entryIndex];

            var nameBounds = ElementBounds.Fixed(0, rowY, 320, 22);
            var detailBounds = ElementBounds.Fixed(0, rowY + 24, 460, 20);
            var dateBounds = ElementBounds.Fixed(330, rowY, 130, 22);
            var pasteBtnBounds = ElementBounds.Fixed(480, rowY + 8, 70, 32);
            var deleteBtnBounds = ElementBounds.Fixed(560, rowY + 8, 70, 32);

            string detailLine = $"{entry.SizeX}x{entry.SizeY}x{entry.SizeZ}  ·  {entry.BlockCount} blocks  ·  anchor {entry.AnchorFaceLabel}"
                + (entry.HasBackup ? "  ·  has backup" : string.Empty);
            string dateLine = entry.ModifiedAt.ToString("yyyy-MM-dd HH:mm");

            string capturedName = entry.Name;
            int rowKey = entryIndex;
            bool isPendingDelete = pendingDeleteIndex == entryIndex;

            composer
                .AddStaticText(entry.Name, rowFont, nameBounds)
                .AddStaticText(detailLine, dimFont, detailBounds)
                .AddStaticText(dateLine, dimFont, dateBounds)
                .AddSmallButton("Paste", () => OnPasteClicked(capturedName), pasteBtnBounds, EnumButtonStyle.Normal, "paste-" + rowKey);

            if (isPendingDelete)
            {
                // Red-text "Confirm?" button. CairoFont.ButtonText() is the large default font
                // and overflows the small-button bounds, so drop font size to ~14 to match
                // AddSmallButton's text sizing.
                var dangerFont = CairoFont.ButtonText()
                    .WithColor(new double[] { 1.0, 0.5, 0.5, 1.0 })
                    .WithFontSize(14);
                composer.AddButton("Confirm?", () => OnDeleteClicked(capturedName, rowKey), deleteBtnBounds, dangerFont, EnumButtonStyle.Normal, "delete-" + rowKey);
            }
            else
            {
                composer.AddSmallButton("Delete", () => OnDeleteClicked(capturedName, rowKey), deleteBtnBounds, EnumButtonStyle.Normal, "delete-" + rowKey);
            }
        }

        // Footer, pagination + hotkeys + reload.
        int footerY = HeaderHeight + rowsHeight + 10;
        var prevBtnBounds = ElementBounds.Fixed(0, footerY, 90, 32);
        var pageLabelBounds = ElementBounds.Fixed(100, footerY + 6, 100, 24);
        var nextBtnBounds = ElementBounds.Fixed(210, footerY, 90, 32);
        var hotkeysBtnBounds = ElementBounds.Fixed(420, footerY, 110, 32);
        var reloadBtnBounds = ElementBounds.Fixed(540, footerY, 90, 32);

        composer
            .AddSmallButton("< Prev", OnPrevPage, prevBtnBounds, EnumButtonStyle.Small, "prevPage")
            .AddStaticText($"Page {pageIndex + 1} / {PageCount}", rowFont, pageLabelBounds)
            .AddSmallButton("Next >", OnNextPage, nextBtnBounds, EnumButtonStyle.Small, "nextPage")
            .AddSmallButton("Hotkeys", OnHotkeysClicked, hotkeysBtnBounds, EnumButtonStyle.Small, "hotkeysBtn")
            .AddSmallButton("Reload", OnReload, reloadBtnBounds, EnumButtonStyle.Small, "reloadBtn")
            .EndChildElements();

        Composers[ComposerKey] = composer.Compose();
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private void OnMatchingModeChanged(string code, bool selected)
    {
        if (!selected) return;
        var parsed = FieldwrightConfig.ParseMatchingMode(code);
        if (parsed.HasValue)
        {
            chosenMode = parsed.Value;
            // Persist to the user's Fieldwright.json so the choice survives the session.
            owner.SetDefaultMatchingMode(chosenMode);
            FieldwrightLogger.Debug(capi, Component, $"library matching mode set to {chosenMode}");
        }
    }

    private bool OnPasteClicked(string name)
    {
        FieldwrightLogger.Info(capi, Component, $"library paste '{name}' (mode={chosenMode})");
        // Cancel any pending delete on a different row when the user pastes instead.
        pendingDeleteIndex = -1;
        TryClose();
        owner.PasteFromLibrary(name, chosenMode);
        return true;
    }

    private bool OnDeleteClicked(string name, int rowIndex)
    {
        // Two-step delete: first click arms the row, second click on the same row confirms.
        if (pendingDeleteIndex != rowIndex)
        {
            pendingDeleteIndex = rowIndex;
            FieldwrightLogger.Debug(capi, Component, $"delete pending for '{name}' (row {rowIndex})");
            Compose();
            return true;
        }

        FieldwrightLogger.Info(capi, Component, $"library delete confirmed '{name}'");
        owner.DeleteFromLibrary(name);
        Refresh();
        return true;
    }

    private bool OnPrevPage()
    {
        if (pageIndex == 0) return true;
        pageIndex--;
        pendingDeleteIndex = -1;
        Compose();
        return true;
    }

    private bool OnNextPage()
    {
        if (pageIndex >= PageCount - 1) return true;
        pageIndex++;
        pendingDeleteIndex = -1;
        Compose();
        return true;
    }

    private bool OnReload()
    {
        Refresh();
        return true;
    }

    private bool OnHotkeysClicked()
    {
        helpDialog ??= new HotkeyHelpDialog(capi);
        if (helpDialog.IsOpened()) helpDialog.TryClose();
        else helpDialog.TryOpen();
        return true;
    }
}
