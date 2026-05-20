using Vintagestory.API.Client;

namespace Fieldwright;

/// <summary>
/// Static reference card showing every Fieldwright hotkey and what it does. Opened
/// from the library dialog's "Hotkeys" button. Read-only: title bar X to close.
/// </summary>
public class HotkeyHelpDialog : GuiDialog
{
    private const string ComposerKey = "fieldwright-hotkeys";
    private const int DialogWidth = 520;
    private const int RowHeight = 26;
    private const int TitleBarPad = 18;

    private static readonly (string keys, string description)[] Hotkeys = new[]
    {
        ("Ctrl+Shift+B", "Set corner 1 (placement anchor + face)"),
        ("Ctrl+Shift+N", "Set corner 2 (opposite cuboid corner)"),
        ("Ctrl+Shift+P", "Place / unplace the active ghost"),
        ("Ctrl+Shift+M", "Cycle ghost mirror axis (None / X / Y / Z)"),
        ("Ctrl+Shift+X", "Cancel: dismiss active ghost + clear selection"),
        ("Ctrl+Shift+L", "Toggle the build checklist HUD"),
        ("Ctrl+Shift+K", "Open the blueprint library"),
        ("PgUp / PgDn", "Restore / peel layers off the top of the ghost"),
    };

    public override string? ToggleKeyCombinationCode => null;
    public override double DrawOrder => 0.26;

    public HotkeyHelpDialog(ICoreClientAPI capi) : base(capi)
    {
        Compose();
    }

    private void Compose()
    {
        Composers[ComposerKey]?.Dispose();

        int contentH = TitleBarPad + 30 + Hotkeys.Length * RowHeight + 20;

        var dialogBounds = ElementStdBounds.AutosizedMainDialog;
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var contentBounds = ElementBounds.Fixed(0, 0, DialogWidth, contentH);
        bgBounds.WithChildren(contentBounds);

        var headerFont = CairoFont.WhiteSmallishText();
        var keyFont = CairoFont.WhiteSmallText().WithColor(new double[] { 0.85, 0.85, 1.0, 1.0 });
        var descFont = CairoFont.WhiteSmallText();

        var composer = capi.Gui.CreateCompo(ComposerKey, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Fieldwright — Hotkeys", () => TryClose())
            .BeginChildElements(contentBounds);

        composer.AddStaticText("Active anywhere in-world while Fieldwright is loaded:", headerFont,
            ElementBounds.Fixed(0, TitleBarPad, DialogWidth, 24));

        for (int i = 0; i < Hotkeys.Length; i++)
        {
            int rowY = TitleBarPad + 30 + i * RowHeight;
            var (keys, desc) = Hotkeys[i];

            composer
                .AddStaticText(keys, keyFont, ElementBounds.Fixed(0, rowY, 160, 22))
                .AddStaticText(desc, descFont, ElementBounds.Fixed(170, rowY, DialogWidth - 170, 22));
        }

        composer.EndChildElements();
        Composers[ComposerKey] = composer.Compose();
    }
}
