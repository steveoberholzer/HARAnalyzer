using System.IO;
using ClosedXML.Excel;
using HARAnalyzer.Models;

namespace HARAnalyzer.Services;

/// <summary>
/// Builds a multi-sheet Excel workbook (.xlsx) from a <see cref="CompareResult"/>.
/// Sheets: Summary | Regressions | Improvements | All Differences | Only in A | Only in B | By Domain
/// </summary>
public static class ExcelBuilder
{
    // ── Palette ────────────────────────────────────────────────────────────

    private static readonly XLColor HeaderBg   = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor HeaderFg   = XLColor.White;
    private static readonly XLColor RegBg      = XLColor.FromHtml("#FFD7D7");
    private static readonly XLColor ImpBg      = XLColor.FromHtml("#D7F0D7");
    private static readonly XLColor ZebraLight = XLColor.FromHtml("#F0F4FA");

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Returns the raw bytes of an .xlsx workbook for the given comparison.</summary>
    public static byte[] Build(CompareResult r)
    {
        using var wb = new XLWorkbook();

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
        ws.Column(1).Width  = 5;    // #
        ws.Column(2).Width  = 9;    // Method
        ws.Column(3).AdjustToContents(1, Math.Min(diffs.Count + 1, 300));
        ws.Column(3).Width  = Math.Min(ws.Column(3).Width, 40);
        ws.Column(4).Width  = 60;   // URL — capped
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

            // Color by net direction of delta
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

    // ── Helpers ────────────────────────────────────────────────────────────

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
