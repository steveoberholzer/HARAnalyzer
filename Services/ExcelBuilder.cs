using System.IO;
using ClosedXML.Excel;
using HARAnalyzer.Models;

namespace HARAnalyzer.Services;

/// <summary>
/// Builds a multi-sheet Excel workbook (.xlsx) from a <see cref="CompareResult"/>.
/// Sheets: Dashboard | Summary | Regressions | Improvements | All Differences | Only in A | Only in B | By Domain
/// </summary>
public static class ExcelBuilder
{
    // ── Palette ────────────────────────────────────────────────────────────

    private static readonly XLColor HeaderBg   = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor HeaderFg   = XLColor.White;
    private static readonly XLColor RegBg      = XLColor.FromHtml("#FFD7D7");
    private static readonly XLColor ImpBg      = XLColor.FromHtml("#D7F0D7");
    private static readonly XLColor ZebraLight = XLColor.FromHtml("#F0F4FA");

    // Dashboard tile colours
    private static readonly string TileRed      = "#C00000";
    private static readonly string TileGreen    = "#375623";
    private static readonly string TileNeutral  = "#404040";
    private static readonly string TileNavy     = "#1F4E79";
    private static readonly string TileAmber    = "#843C0C";

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Returns the raw bytes of an .xlsx workbook for the given comparison.</summary>
    public static byte[] Build(CompareResult r)
    {
        using var wb = new XLWorkbook();

        AddDashboardSheet(wb, r);           // First tab — visual summary
        AddSummarySheet(wb, r);
        AddDiffSheet(wb, "Regressions",     r.Regressions,  fixedRowColor: RegBg);
        AddDiffSheet(wb, "Improvements",    r.Improvements, fixedRowColor: ImpBg);
        AddDiffSheet(wb, "All Differences", r.Diffs,        fixedRowColor: null);
        AddEntriesSheet(wb, "Only in A",    r.OnlyInA);
        AddEntriesSheet(wb, "Only in B",    r.OnlyInB);
        AddByDomainSheet(wb, r);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Dashboard ─────────────────────────────────────────────────────────
    //
    // Layout (column numbers):
    //   1          = left margin
    //   2-4        = Tile 1: Regressions
    //   5          = gap
    //   6-8        = Tile 2: Improvements
    //   9          = gap
    //   10-12      = Tile 3: Overall Delta
    //   13         = gap
    //   14-16      = Tile 4: Matched
    //   17         = right margin
    //
    // Row layout:
    //   1          = top margin
    //   2          = title
    //   3          = file-A / file-B line
    //   4          = date + total-counts line
    //   5          = spacer
    //   6          = tile label
    //   7          = tile big value
    //   8          = tile sublabel
    //   9          = secondary stats (Only in A / B)
    //   10         = spacer
    //   11+        = Top-10 Regressions table (if any)
    //   ...        = spacer + Top-10 Improvements table (if any)

    private static void AddDashboardSheet(XLWorkbook wb, CompareResult r)
    {
        var ws = wb.AddWorksheet("Dashboard");

        // ── Column widths ──────────────────────────────────────────────────
        ws.Column(1).Width  = 2;    // left margin
        ws.Column(2).Width  = 7;
        ws.Column(3).Width  = 7;
        ws.Column(4).Width  = 7;    // Tile 1
        ws.Column(5).Width  = 2;    // gap
        ws.Column(6).Width  = 7;
        ws.Column(7).Width  = 7;
        ws.Column(8).Width  = 7;    // Tile 2
        ws.Column(9).Width  = 2;    // gap
        ws.Column(10).Width = 7;
        ws.Column(11).Width = 7;
        ws.Column(12).Width = 7;    // Tile 3
        ws.Column(13).Width = 2;    // gap
        ws.Column(14).Width = 7;
        ws.Column(15).Width = 7;
        ws.Column(16).Width = 7;    // Tile 4
        ws.Column(17).Width = 2;    // right margin

        // ── Row heights ────────────────────────────────────────────────────
        ws.Row(1).Height = 6;
        ws.Row(2).Height = 28;
        ws.Row(3).Height = 14;
        ws.Row(4).Height = 14;
        ws.Row(5).Height = 8;
        ws.Row(6).Height = 14;
        ws.Row(7).Height = 44;
        ws.Row(8).Height = 14;
        ws.Row(9).Height = 14;
        ws.Row(10).Height = 10;

        // ── Title ──────────────────────────────────────────────────────────
        var titleRange = ws.Range(2, 2, 2, 16);
        titleRange.Merge();
        titleRange.Style.Font.FontSize = 20;
        titleRange.Style.Font.Bold     = true;
        titleRange.Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ws.Cell(2, 2).Value = "HAR Performance Comparison";

        // ── File info ──────────────────────────────────────────────────────
        var fileRange = ws.Range(3, 2, 3, 16);
        fileRange.Merge();
        fileRange.Style.Font.FontSize  = 10;
        fileRange.Style.Font.FontColor = XLColor.FromHtml("#595959");
        fileRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(3, 2).Value = $"Baseline (A):  {r.FileNameA}     ·     New (B):  {r.FileNameB}";

        var metaRange = ws.Range(4, 2, 4, 16);
        metaRange.Merge();
        metaRange.Style.Font.FontSize  = 10;
        metaRange.Style.Font.FontColor = XLColor.FromHtml("#595959");
        metaRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(4, 2).Value =
            $"Compared: {r.ComparedAt:yyyy-MM-dd HH:mm}" +
            $"     ·     {r.TotalInA:N0} requests in A   /   {r.TotalInB:N0} requests in B";

        // ── KPI Tiles ──────────────────────────────────────────────────────

        // Tile 1 — Regressions
        AddKpiTile(ws, startCol: 2, labelRow: 6,
            label:    "REGRESSIONS",
            value:    r.RegressionCount.ToString("N0"),
            sublabel: r.RegressionCount == 1 ? "endpoint got slower" : "endpoints got slower",
            bgHex:    r.RegressionCount > 0 ? TileRed : TileGreen);

        // Tile 2 — Improvements
        AddKpiTile(ws, startCol: 6, labelRow: 6,
            label:    "IMPROVEMENTS",
            value:    r.ImprovementCount.ToString("N0"),
            sublabel: r.ImprovementCount == 1 ? "endpoint got faster" : "endpoints got faster",
            bgHex:    r.ImprovementCount > 0 ? TileGreen : TileNeutral);

        // Tile 3 — Overall Delta
        var deltaStr  = r.OverallDeltaMs >= 0
            ? $"+{r.OverallDeltaMs:N1} ms"
            : $"{r.OverallDeltaMs:N1} ms";
        var deltaBg   = r.OverallDeltaMs > 100  ? TileAmber
                      : r.OverallDeltaMs < -100 ? TileGreen
                      : TileNeutral;
        var deltaDesc = r.OverallDeltaMs > 0  ? "cumulative slowdown"
                      : r.OverallDeltaMs < 0  ? "cumulative speedup"
                      : "no net change";
        AddKpiTile(ws, startCol: 10, labelRow: 6,
            label:    "OVERALL DELTA",
            value:    deltaStr,
            sublabel: deltaDesc,
            bgHex:    deltaBg);

        // Tile 4 — Matched
        AddKpiTile(ws, startCol: 14, labelRow: 6,
            label:    "MATCHED",
            value:    r.MatchedCount.ToString("N0"),
            sublabel: "endpoints compared",
            bgHex:    TileNavy);

        // ── Secondary stats ────────────────────────────────────────────────
        var onlyRange = ws.Range(9, 2, 9, 16);
        onlyRange.Merge();
        onlyRange.Style.Font.FontSize  = 10;
        onlyRange.Style.Font.FontColor = XLColor.FromHtml("#595959");
        onlyRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(9, 2).Value =
            $"Only in A: {r.OnlyInACount:N0} requests     ·     Only in B: {r.OnlyInBCount:N0} requests";

        // ── Tables ─────────────────────────────────────────────────────────
        int nextRow = 11;

        if (r.Regressions.Count > 0)
            nextRow = AddDashTable(ws, nextRow, r.Regressions, r.RegressionCount,
                label: "Top Regressions", labelColor: TileRed,
                valueColor: "#C00000", zebraColor: "#FFF0F0");

        if (r.Improvements.Count > 0)
            nextRow = AddDashTable(ws, nextRow, r.Improvements, r.ImprovementCount,
                label: "Top Improvements", labelColor: TileGreen,
                valueColor: "#375623", zebraColor: "#F0FFF0");

        if (r.Regressions.Count == 0 && r.Improvements.Count == 0)
        {
            ws.Row(nextRow).Height = 18;
            var noneRange = ws.Range(nextRow, 2, nextRow, 16);
            noneRange.Merge();
            noneRange.Style.Font.FontSize  = 11;
            noneRange.Style.Font.FontColor = XLColor.FromHtml("#375623");
            noneRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            noneRange.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            ws.Cell(nextRow, 2).Value = "No significant regressions or improvements detected.";
        }

    }

    /// <summary>
    /// Renders a top-10 diff table on the Dashboard sheet.
    /// Returns the next available row after the table (including a trailing spacer).
    /// </summary>
    private static int AddDashTable(IXLWorksheet ws, int startRow,
        List<RequestDiff> diffs, int totalCount,
        string label, string labelColor, string valueColor, string zebraColor)
    {
        var topN = diffs.Take(10).ToList();
        int row  = startRow;

        // Section heading
        ws.Row(row).Height = 20;
        var headRange = ws.Range(row, 2, row, 16);
        headRange.Merge();
        headRange.Style.Font.FontSize = 12;
        headRange.Style.Font.Bold     = true;
        headRange.Style.Font.FontColor = XLColor.FromHtml(labelColor);
        headRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell(row, 2).Value = totalCount > 10
            ? $"{label}  (top 10 of {totalCount})"
            : $"{label}  ({totalCount})";
        row++;

        // Column headers
        // Cols 2-9: Endpoint  | 10-11: Baseline | 12-13: New | 14-15: Delta | 16: %
        ws.Row(row).Height = 14;
        DashColHeader(ws, row, 2,  9,  "Endpoint (Method + URL)", left: true);
        DashColHeader(ws, row, 10, 11, "Baseline (ms)");
        DashColHeader(ws, row, 12, 13, "New (ms)");
        DashColHeader(ws, row, 14, 15, "Delta (ms)");
        DashColHeader(ws, row, 16, 16, "% Change");
        row++;

        // Data rows
        var zebra = XLColor.FromHtml(zebraColor);
        var vColor = XLColor.FromHtml(valueColor);

        for (int i = 0; i < topN.Count; i++, row++)
        {
            var d = topN[i];
            ws.Row(row).Height = 14;

            // Endpoint
            var ep = ws.Range(row, 2, row, 9);
            ep.Merge();
            ep.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ep.Style.Font.FontSize = 9;
            var urlDisplay = d.BaseUrl.Length > 58 ? "…" + d.BaseUrl[^56..] : d.BaseUrl;
            ws.Cell(row, 2).Value = $"{d.Method}  {urlDisplay}";

            // Baseline
            var bl = ws.Range(row, 10, row, 11);
            bl.Merge();
            bl.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            bl.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            bl.Style.NumberFormat.Format  = "#,##0.0";
            ws.Cell(row, 10).Value = d.AvgTotalMsA;

            // New
            var nw = ws.Range(row, 12, row, 13);
            nw.Merge();
            nw.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            nw.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            nw.Style.NumberFormat.Format  = "#,##0.0";
            ws.Cell(row, 12).Value = d.AvgTotalMsB;

            // Delta
            var dl = ws.Range(row, 14, row, 15);
            dl.Merge();
            dl.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            dl.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            dl.Style.Font.Bold            = true;
            dl.Style.Font.FontColor       = vColor;
            dl.Style.NumberFormat.Format  = "+#,##0.0;-#,##0.0;0.0";
            ws.Cell(row, 14).Value = d.DeltaMs;

            // % Change
            var pc = ws.Cell(row, 16);
            pc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            pc.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            pc.Style.Font.FontColor       = vColor;
            if (!double.IsNaN(d.PctChange))
            {
                pc.Value = d.PctChange / 100.0;
                pc.Style.NumberFormat.Format = "+0.0%;-0.0%;0.0%";
            }
            else
            {
                pc.Value = "N/A";
            }

            // Zebra shading
            if (i % 2 == 0)
                ws.Range(row, 2, row, 16).Style.Fill.BackgroundColor = zebra;
        }

        return row + 1; // leave one blank spacer row
    }

    // ── Sheet builders ────────────────────────────────────────────────────

    private static void AddSummarySheet(XLWorkbook wb, CompareResult r)
    {
        var ws = wb.AddWorksheet("Summary");

        SetHeader(ws, 1, "Metric", "Value");

        int row = 2;
        void KV(string metric, string value)
        {
            ws.Cell(row, 1).Value = metric;
            ws.Cell(row, 2).Value = value;
            if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = ZebraLight;
            row++;
        }

        KV("File A (Baseline)",   r.FileNameA);
        KV("Path A",              r.FilePathA);
        KV("File B (New)",        r.FileNameB);
        KV("Path B",              r.FilePathB);
        KV("Compared At",         r.ComparedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        KV("Total Requests A",    r.TotalInA.ToString());
        KV("Total Requests B",    r.TotalInB.ToString());
        KV("Matched Requests",    r.MatchedCount.ToString());
        KV("Regressions",         r.RegressionCount.ToString());
        KV("Improvements",        r.ImprovementCount.ToString());
        KV("Only in A",           r.OnlyInACount.ToString());
        KV("Only in B",           r.OnlyInBCount.ToString());
        KV("Overall Delta (ms)",  r.OverallDeltaMs.ToString("F1"));

        ws.Column(1).Width = 26;
        ws.Column(2).AdjustToContents(2, row - 1);
        ws.SheetView.FreezeRows(1);
    }

    /// <param name="fixedRowColor">
    /// When non-null every data row gets this background (used for Regressions/Improvements).
    /// When null, rows are colored per-category in the All Differences sheet.
    /// </param>
    private static void AddDiffSheet(XLWorkbook wb, string sheetName,
        List<RequestDiff> diffs, XLColor? fixedRowColor)
    {
        var ws = wb.AddWorksheet(sheetName);

        SetHeader(ws, 1,
            "#", "Method", "Host", "Base URL",
            "Baseline Avg (ms)", "New Avg (ms)", "Delta (ms)", "% Change",
            "Baseline TTFB (ms)", "New TTFB (ms)",
            "Count A", "Count B", "Category");

        for (int i = 0; i < diffs.Count; i++)
        {
            var d   = diffs[i];
            int row = i + 2;

            ws.Cell(row,  1).Value = i + 1;
            ws.Cell(row,  2).Value = d.Method;
            ws.Cell(row,  3).Value = d.Host;
            ws.Cell(row,  4).Value = d.BaseUrl;
            ws.Cell(row,  5).Value = d.AvgTotalMsA;
            ws.Cell(row,  6).Value = d.AvgTotalMsB;
            ws.Cell(row,  7).Value = d.DeltaMs;
            ws.Cell(row,  9).Value = d.AvgTtfbMsA;
            ws.Cell(row, 10).Value = d.AvgTtfbMsB;
            ws.Cell(row, 11).Value = d.CountA;
            ws.Cell(row, 12).Value = d.CountB;
            ws.Cell(row, 13).Value = d.Category.ToString();

            // % Change — store as decimal fraction so Excel formats it as a %
            if (double.IsNaN(d.PctChange))
                ws.Cell(row, 8).Value = "N/A";
            else
            {
                ws.Cell(row, 8).Value = d.PctChange / 100.0;
                ws.Cell(row, 8).Style.NumberFormat.Format = "+0.0%;-0.0%;0.0%";
            }

            // Number formats for ms columns
            foreach (int c in new[] { 5, 6, 7, 9, 10 })
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.0";

            // Row background
            XLColor bg = fixedRowColor ?? d.Category switch
            {
                DiffCategory.Regression  => RegBg,
                DiffCategory.Improvement => ImpBg,
                _                        => i % 2 == 0 ? ZebraLight : XLColor.White,
            };
            ws.Row(row).Style.Fill.BackgroundColor = bg;
        }

        // Column widths
        ws.Column(1).Width  = 5;
        ws.Column(2).Width  = 9;
        ws.Column(3).AdjustToContents(1, Math.Min(diffs.Count + 1, 300));
        ws.Column(3).Width  = Math.Min(ws.Column(3).Width, 40);
        ws.Column(4).Width  = 60;
        ws.Column(5).Width  = 18;
        ws.Column(6).Width  = 18;
        ws.Column(7).Width  = 14;
        ws.Column(8).Width  = 12;
        ws.Column(9).Width  = 18;
        ws.Column(10).Width = 18;
        ws.Column(11).Width = 10;
        ws.Column(12).Width = 10;
        ws.Column(13).Width = 14;

        ws.SheetView.FreezeRows(1);
        if (diffs.Count > 0)
            ws.RangeUsed()!.SetAutoFilter();
    }

    private static void AddEntriesSheet(XLWorkbook wb, string sheetName, List<AnalysisEntry> entries)
    {
        var ws = wb.AddWorksheet(sheetName);

        SetHeader(ws, 1,
            "#", "Method", "Status", "Total (ms)", "TTFB (ms)",
            "Connect (ms)", "SSL (ms)", "DNS (ms)", "Send (ms)", "Receive (ms)",
            "MIME Type", "Host", "URL");

        for (int i = 0; i < entries.Count; i++)
        {
            var e   = entries[i];
            int row = i + 2;

            ws.Cell(row,  1).Value = i + 1;
            ws.Cell(row,  2).Value = e.Method;
            ws.Cell(row,  3).Value = e.Status;
            ws.Cell(row,  4).Value = e.TotalMs;
            ws.Cell(row,  5).Value = e.TtfbMs;
            ws.Cell(row,  6).Value = e.ConnectMs;
            ws.Cell(row,  7).Value = e.SslMs;
            ws.Cell(row,  8).Value = e.DnsMs;
            ws.Cell(row,  9).Value = e.SendMs;
            ws.Cell(row, 10).Value = e.ReceiveMs;
            ws.Cell(row, 11).Value = e.MimeType;
            ws.Cell(row, 12).Value = e.Host;
            ws.Cell(row, 13).Value = e.Url;

            foreach (int c in new[] { 4, 5, 6, 7, 8, 9, 10 })
                ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.0";

            if (i % 2 == 0)
                ws.Row(row).Style.Fill.BackgroundColor = ZebraLight;
        }

        ws.Column(1).Width  = 5;
        ws.Column(2).Width  = 9;
        ws.Column(3).Width  = 8;
        foreach (int c in new[] { 4, 5, 6, 7, 8, 9, 10 }) ws.Column(c).Width = 14;
        ws.Column(11).Width = 18;
        ws.Column(12).AdjustToContents(1, Math.Min(entries.Count + 1, 300));
        ws.Column(12).Width = Math.Min(ws.Column(12).Width, 36);
        ws.Column(13).Width = 60;

        ws.SheetView.FreezeRows(1);
        if (entries.Count > 0)
            ws.RangeUsed()!.SetAutoFilter();
    }

    private static void AddByDomainSheet(XLWorkbook wb, CompareResult r)
    {
        var ws = wb.AddWorksheet("By Domain");

        SetHeader(ws, 1, "Domain", "Matched Requests",
            "Sum Delta (ms)", "Avg Delta (ms)", "Regressions", "Improvements");

        var groups = r.Diffs
            .GroupBy(d => d.Host)
            .Select(g => new
            {
                Domain       = g.Key,
                Matched      = g.Count(),
                SumDelta     = Math.Round(g.Sum(d => d.DeltaMs),     1),
                AvgDelta     = Math.Round(g.Average(d => d.DeltaMs), 1),
                Regressions  = g.Count(d => d.Category == DiffCategory.Regression),
                Improvements = g.Count(d => d.Category == DiffCategory.Improvement),
            })
            .OrderByDescending(g => Math.Abs(g.SumDelta))
            .ToList();

        for (int i = 0; i < groups.Count; i++)
        {
            var g   = groups[i];
            int row = i + 2;

            ws.Cell(row, 1).Value = g.Domain;
            ws.Cell(row, 2).Value = g.Matched;
            ws.Cell(row, 3).Value = g.SumDelta;
            ws.Cell(row, 4).Value = g.AvgDelta;
            ws.Cell(row, 5).Value = g.Regressions;
            ws.Cell(row, 6).Value = g.Improvements;

            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.0";
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.0";

            XLColor bg = g.SumDelta > 0 ? RegBg
                       : g.SumDelta < 0 ? ImpBg
                       : i % 2 == 0 ? ZebraLight : XLColor.White;
            ws.Row(row).Style.Fill.BackgroundColor = bg;
        }

        ws.Column(1).AdjustToContents(1, Math.Min(groups.Count + 1, 300));
        ws.Column(1).Width = Math.Min(ws.Column(1).Width, 40);
        foreach (int c in new[] { 2, 3, 4, 5, 6 }) ws.Column(c).Width = 20;

        ws.SheetView.FreezeRows(1);
        if (groups.Count > 0)
            ws.RangeUsed()!.SetAutoFilter();
    }

    // ── Dashboard helpers ──────────────────────────────────────────────────

    /// <summary>Renders a 3-row KPI tile (label / big value / sublabel) in the given column block.</summary>
    private static void AddKpiTile(IXLWorksheet ws, int startCol, int labelRow,
        string label, string value, string sublabel, string bgHex)
    {
        int endCol   = startCol + 2;
        var bg       = XLColor.FromHtml(bgHex);
        var white    = XLColor.White;
        var offWhite = XLColor.FromHtml("#E0E0E0");

        // Label row
        var lr = ws.Range(labelRow, startCol, labelRow, endCol);
        lr.Merge();
        lr.Style.Fill.BackgroundColor = bg;
        lr.Style.Font.FontSize        = 9;
        lr.Style.Font.Bold            = true;
        lr.Style.Font.FontColor       = offWhite;
        lr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        lr.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Bottom;
        ws.Cell(labelRow, startCol).Value = label;

        // Big value row
        var vr = ws.Range(labelRow + 1, startCol, labelRow + 1, endCol);
        vr.Merge();
        vr.Style.Fill.BackgroundColor = bg;
        vr.Style.Font.FontSize        = 30;
        vr.Style.Font.Bold            = true;
        vr.Style.Font.FontColor       = white;
        vr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        vr.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ws.Cell(labelRow + 1, startCol).Value = value;

        // Sublabel row
        var sr = ws.Range(labelRow + 2, startCol, labelRow + 2, endCol);
        sr.Merge();
        sr.Style.Fill.BackgroundColor = bg;
        sr.Style.Font.FontSize        = 8;
        sr.Style.Font.FontColor       = offWhite;
        sr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sr.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Top;
        ws.Cell(labelRow + 2, startCol).Value = sublabel;
    }

    /// <summary>Renders a single column-header cell (or merged range) inside the Dashboard tables.</summary>
    private static void DashColHeader(IXLWorksheet ws,
        int row, int startCol, int endCol, string label, bool left = false)
    {
        var range = ws.Range(row, startCol, row, endCol);
        if (startCol != endCol) range.Merge();
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#2E5D8E");
        range.Style.Font.Bold            = true;
        range.Style.Font.FontColor       = XLColor.White;
        range.Style.Font.FontSize        = 9;
        range.Style.Alignment.Horizontal = left
            ? XLAlignmentHorizontalValues.Left
            : XLAlignmentHorizontalValues.Right;
        range.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ws.Cell(row, startCol).Value = label;
    }

    // ── Sheet helpers ──────────────────────────────────────────────────────

    private static void SetHeader(IXLWorksheet ws, int row, params string[] headers)
    {
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold            = true;
            cell.Style.Font.FontColor       = HeaderFg;
            cell.Style.Fill.BackgroundColor = HeaderBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }
}
