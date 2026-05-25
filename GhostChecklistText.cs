using System.Collections.Generic;
using System.Text;

namespace Fieldwright;

/// <summary>
/// Shared builder for the checklist body. Two surfaces consume the same content:
/// the always-on HUD overlay (VTML for color coding) and the togglable modal
/// (VTML for display, plain text for clipboard copy). Keeping the formatting in
/// one place keeps the two views consistent as we tweak rows / progress lines.
/// </summary>
public static class GhostChecklistText
{
    public static (string vtml, string plain) Build(GhostMatchTracker tracker)
    {
        var vtml = new StringBuilder();
        var plain = new StringBuilder();

        int totalCells = tracker.TotalBlockCells + tracker.TotalAirCells;
        int doneCells = tracker.MatchedBlocks + (tracker.TotalAirCells - tracker.AirViolationCount);
        float pct = totalCells > 0 ? (doneCells * 100f / totalCells) : 100f;
        vtml.AppendLine($"<font color=\"#aaffaa\">Progress: {doneCells} / {totalCells} ({pct:F0}%)</font>");
        plain.AppendLine($"Progress: {doneCells} / {totalCells} ({pct:F0}%)");

        var needs = tracker.GetMaterialNeeds();
        if (needs.Count > 0)
        {
            vtml.AppendLine();
            vtml.AppendLine("<font color=\"#ffffff\">Materials needed:</font>");
            plain.AppendLine();
            plain.AppendLine("Materials needed:");

            var sorted = new List<KeyValuePair<string, int>>(needs);
            sorted.Sort((a, b) => string.Compare(a.Key, b.Key, System.StringComparison.Ordinal));

            foreach (var kvp in sorted)
            {
                int have = tracker.CountInInventory(kvp.Key);
                int need = kvp.Value;
                string color = have >= need ? "#aaffaa" : "#ffaaaa";
                vtml.AppendLine($"<font color=\"{color}\">  {kvp.Key}: {have} / {need}</font>");
                plain.AppendLine($"  {kvp.Key}: {have} / {need}");
            }
        }

        if (tracker.BlocksToRemoveCount > 0)
        {
            vtml.AppendLine();
            vtml.AppendLine($"<font color=\"#ff8888\">Blocks to remove: {tracker.BlocksToRemoveCount}</font>");
            plain.AppendLine();
            plain.AppendLine($"Blocks to remove: {tracker.BlocksToRemoveCount}");
        }

        if (tracker.IsComplete)
        {
            vtml.AppendLine();
            vtml.AppendLine("<font color=\"#aaffaa\">Structure complete!</font>");
            plain.AppendLine();
            plain.AppendLine("Structure complete!");
        }

        return (vtml.ToString(), plain.ToString());
    }
}
