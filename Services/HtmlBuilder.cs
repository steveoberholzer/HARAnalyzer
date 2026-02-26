using System.Text;
using HARAnalyzer.Models;

namespace HARAnalyzer.Services;

/// <summary>Generates self-contained HTML reports for each tree-view section.</summary>
public static class HtmlBuilder
{
    // ── Public entry points ────────────────────────────────────────────────

    public static string BuildWelcome() =>
        Page("Welcome", "<div class='empty'><h2>HAR Analyzer</h2><p>Open a .HAR file via <strong>File → Open</strong> (Ctrl+O) to begin.</p></div>");

    public static string BuildSummary(AnalysisResult r)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Summary</h1>");

        // Stat cards
        sb.Append("<div class='cards'>");
        Card(sb, "Total Requests",   r.TotalEntries.ToString("N0"), "");
        Card(sb, "Total Time",       FormatMs(r.TotalMs),            "");
        Card(sb, "Avg Request",      FormatMs(r.AvgMs),              "");
        Card(sb, "Slowest Request",  FormatMs(r.MaxMs),              r.MaxMs >= 3000 ? "warn" : "");
        Card(sb, "Max TTFB",         FormatMs(r.MaxTtfbMs),          r.MaxTtfbMs >= 1000 ? "warn" : "");
        Card(sb, "Slow (>1 s)",      r.SlowCount.ToString("N0"),     r.SlowCount > 0 ? "warn" : "ok");
        Card(sb, "Errors",           r.ErrorCount.ToString("N0"),    r.ErrorCount > 0 ? "error" : "ok");
        Card(sb, "Duration",         r.TimeSpanCovered.TotalSeconds >= 1
                                        ? $"{r.TimeSpanCovered:mm\\:ss}" : "&lt;1 s", "");
        sb.Append("</div>");

        // File info table
        sb.Append("<h2>File Information</h2><table><tbody>");
        InfoRow(sb, "File",      H(r.FileName));
        InfoRow(sb, "Path",      $"<code>{H(r.FilePath)}</code>");
        InfoRow(sb, "Loaded",    r.LoadedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        InfoRow(sb, "Creator",   H($"{r.CreatorName} {r.CreatorVersion}".Trim()));
        InfoRow(sb, "HAR ver.",  H(r.HarVersion));
        sb.Append("</tbody></table>");

        // Status breakdown
        var groups = r.Entries
            .GroupBy(e => e.Status == 0 ? "0 (no response)" : (e.Status / 100) + "xx")
            .OrderBy(g => g.Key)
            .ToList();

        sb.Append("<h2>Status Breakdown</h2><table><thead><tr><th>Status Group</th><th>Count</th><th>% of Total</th></tr></thead><tbody>");
        foreach (var g in groups)
        {
            var pct = r.TotalEntries > 0 ? (g.Count() * 100.0 / r.TotalEntries) : 0;
            sb.Append($"<tr><td>{H(g.Key)}</td><td class='num'>{g.Count():N0}</td><td class='num'>{pct:F1}%</td></tr>");
        }
        sb.Append("</tbody></table>");

        // Top domains quick view
        var topDomains = r.Entries
            .GroupBy(e => e.Host)
            .Select(g => new { Domain = g.Key, Count = g.Count(), TotalMs = g.Sum(x => x.TotalMs) })
            .OrderByDescending(x => x.TotalMs)
            .Take(10)
            .ToList();

        sb.Append("<h2>Top Domains by Total Time</h2><table><thead><tr><th>Domain</th><th>Requests</th><th>Total Time</th><th>Avg Time</th></tr></thead><tbody>");
        foreach (var d in topDomains)
        {
            var avg = d.Count > 0 ? d.TotalMs / d.Count : 0;
            sb.Append($"<tr><td><code>{H(d.Domain)}</code></td><td class='num'>{d.Count:N0}</td><td class='num'>{FormatMs(d.TotalMs)}</td><td class='num'>{FormatMs(avg)}</td></tr>");
        }
        sb.Append("</tbody></table>");

        return Page("Summary", sb.ToString());
    }

    public static string BuildSlowestByTotal(List<AnalysisEntry> entries)
    {
        var rows = entries.OrderByDescending(e => e.TotalMs).Take(30).ToList();
        var sb = new StringBuilder();
        sb.Append("<h1>Slowest Requests — by Total Time (top 30)</h1>");
        sb.Append(TimingTable(rows));
        return Page("Slowest by Total", sb.ToString());
    }

    public static string BuildSlowestByTtfb(List<AnalysisEntry> entries)
    {
        var rows = entries.OrderByDescending(e => e.TtfbMs).Take(20).ToList();
        var sb = new StringBuilder();
        sb.Append("<h1>Slowest Requests — by TTFB / Server Think Time (top 20)</h1>");
        sb.Append(TimingTable(rows, highlightTtfb: true));
        return Page("Slowest by TTFB", sb.ToString());
    }

    public static string BuildErrors(List<AnalysisEntry> entries)
    {
        var rows = entries.Where(e => e.Status >= 400 || e.Status == 0)
                          .OrderBy(e => e.Status).ThenByDescending(e => e.TotalMs).ToList();
        var sb = new StringBuilder();
        sb.Append($"<h1>Errors — 4xx / 5xx / No Response ({rows.Count})</h1>");
        if (rows.Count == 0)
        {
            sb.Append("<div class='empty'><p>No errors found — great!</p></div>");
        }
        else
        {
            sb.Append(TimingTable(rows));
        }
        return Page("Errors", sb.ToString());
    }

    public static string BuildByDomain(List<AnalysisEntry> entries)
    {
        var groups = entries
            .GroupBy(e => e.Host)
            .Select(g => new
            {
                Domain   = g.Key,
                Count    = g.Count(),
                TotalMs  = g.Sum(e => e.TotalMs),
                AvgMs    = g.Average(e => e.TotalMs),
                MaxMs    = g.Max(e => e.TotalMs),
                MaxTtfb  = g.Max(e => e.TtfbMs),
                Errors   = g.Count(e => e.Status >= 400 || e.Status == 0),
                Slow     = g.Count(e => e.TotalMs >= 1000),
            })
            .OrderByDescending(g => g.TotalMs)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("<h1>Requests by Domain</h1>");
        sb.Append("<table><thead><tr>");
        sb.Append("<th>Domain</th><th>Requests</th><th>Total Time</th><th>Avg</th><th>Max</th><th>Max TTFB</th><th>Slow (&gt;1s)</th><th>Errors</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var g in groups)
        {
            sb.Append("<tr>");
            sb.Append($"<td><code>{H(g.Domain)}</code></td>");
            sb.Append($"<td class='num'>{g.Count:N0}</td>");
            sb.Append($"<td class='num'>{FormatMs(g.TotalMs)}</td>");
            sb.Append($"<td class='num'>{FormatMs(g.AvgMs)}</td>");
            sb.Append($"<td class='num {SpeedClass(g.MaxMs)}'>{FormatMs(g.MaxMs)}</td>");
            sb.Append($"<td class='num {SpeedClass(g.MaxTtfb)}'>{FormatMs(g.MaxTtfb)}</td>");
            sb.Append($"<td class='num{(g.Slow > 0 ? " warn" : "")}'>{g.Slow:N0}</td>");
            sb.Append($"<td class='num{(g.Errors > 0 ? " error" : "")}'>{g.Errors:N0}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return Page("By Domain", sb.ToString());
    }

    public static string BuildDomainDetail(string domain, List<AnalysisEntry> entries)
    {
        var rows = entries.Where(e => e.Host == domain)
                          .OrderByDescending(e => e.TotalMs).ToList();
        var sb = new StringBuilder();
        sb.Append($"<h1><code>{H(domain)}</code> — {rows.Count} request{(rows.Count == 1 ? "" : "s")}</h1>");
        sb.Append(TimingTable(rows));
        return Page(domain, sb.ToString());
    }

    public static string BuildTimeline(List<AnalysisEntry> entries)
    {
        var valid = entries.Where(e => e.Started > DateTime.MinValue).ToList();
        if (valid.Count == 0)
            return Page("Timeline", "<div class='empty'><p>No timestamp data available.</p></div>");

        var origin = valid.Min(e => e.Started);
        var buckets = valid
            .GroupBy(e => Math.Floor((e.Started - origin).TotalSeconds / 5) * 5)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Offset   = g.Key,
                Label    = $"+{g.Key:0}s",
                Count    = g.Count(),
                TotalMs  = g.Sum(e => e.TotalMs),
                MaxMs    = g.Max(e => e.TotalMs),
                Slow     = g.Count(e => e.TotalMs >= 1000),
                Errors   = g.Count(e => e.Status >= 400 || e.Status == 0),
            })
            .ToList();

        var maxCount = buckets.Max(b => b.Count);

        var sb = new StringBuilder();
        sb.Append("<h1>Timeline — Requests per 5-second Bucket</h1>");
        sb.Append($"<p style='margin-bottom:12px;color:#666;'>Recording started: {origin:yyyy-MM-dd HH:mm:ss} · Total duration: {(valid.Max(e => e.Started) - origin):mm\\:ss}</p>");

        // Mini sparkline bar chart
        sb.Append("<div class='spark-wrap'>");
        foreach (var b in buckets)
        {
            var h = maxCount > 0 ? (int)Math.Max(2, b.Count * 80.0 / maxCount) : 2;
            var cls = b.Errors > 0 ? "error" : (b.Slow > 0 ? "warn" : "");
            sb.Append($"<div class='spark-col' title='{b.Label}: {b.Count} requests'><div class='spark-bar {cls}' style='height:{h}px'></div></div>");
        }
        sb.Append("</div>");

        sb.Append("<table><thead><tr><th>Offset</th><th>Requests</th><th>Total Time</th><th>Max Time</th><th>Slow (&gt;1s)</th><th>Errors</th></tr></thead><tbody>");
        foreach (var b in buckets)
        {
            sb.Append("<tr>");
            sb.Append($"<td><code>{H(b.Label)}</code></td>");
            sb.Append($"<td class='num'>{b.Count:N0}</td>");
            sb.Append($"<td class='num'>{FormatMs(b.TotalMs)}</td>");
            sb.Append($"<td class='num {SpeedClass(b.MaxMs)}'>{FormatMs(b.MaxMs)}</td>");
            sb.Append($"<td class='num{(b.Slow > 0 ? " warn" : "")}'>{b.Slow:N0}</td>");
            sb.Append($"<td class='num{(b.Errors > 0 ? " error" : "")}'>{b.Errors:N0}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return Page("Timeline", sb.ToString());
    }

    public static string BuildAllRequests(List<AnalysisEntry> entries)
    {
        const int limit = 500;
        var rows = entries.Take(limit).ToList();
        var sb = new StringBuilder();
        sb.Append($"<h1>All Requests ({entries.Count:N0})</h1>");
        if (entries.Count > limit)
            sb.Append($"<p class='note'>Showing first {limit:N0} of {entries.Count:N0} requests. Use domain or filter sections for targeted views.</p>");
        sb.Append(TimingTable(rows));
        return Page("All Requests", sb.ToString());
    }

    // ── Compare entry points ───────────────────────────────────────────────

    public static string BuildCompareSummary(CompareResult r)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Comparison Summary</h1>");

        sb.Append("<div class='cards'>");
        Card(sb, "Matched",       r.MatchedCount.ToString("N0"),     "");
        Card(sb, "Regressions",   r.RegressionCount.ToString("N0"),  r.RegressionCount > 0 ? "error" : "ok");
        Card(sb, "Improvements",  r.ImprovementCount.ToString("N0"), r.ImprovementCount > 0 ? "ok" : "");
        Card(sb, "Only in A",     r.OnlyInACount.ToString("N0"),     "");
        Card(sb, "Only in B",     r.OnlyInBCount.ToString("N0"),     "");
        Card(sb, "Sum Δ (ms)",    DeltaBadge(r.OverallDeltaMs),      "");
        sb.Append("</div>");

        sb.Append("<h2>Files Compared</h2><table><thead><tr><th>File</th><th>A (Baseline)</th><th>B (New)</th></tr></thead><tbody>");
        sb.Append($"<tr><th>Name</th><td>{H(r.FileNameA)}</td><td>{H(r.FileNameB)}</td></tr>");
        sb.Append($"<tr><th>Path</th><td><code>{H(r.FilePathA)}</code></td><td><code>{H(r.FilePathB)}</code></td></tr>");
        sb.Append($"<tr><th>Total Requests</th><td class='num'>{r.TotalInA:N0}</td><td class='num'>{r.TotalInB:N0}</td></tr>");
        sb.Append($"<tr><th>Compared At</th><td colspan='2'>{r.ComparedAt:yyyy-MM-dd HH:mm:ss}</td></tr>");
        sb.Append("</tbody></table>");

        return Page("Compare Summary", sb.ToString());
    }

    public static string BuildCompareDiffTable(string title, List<RequestDiff> diffs)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>{H(title)} ({diffs.Count:N0})</h1>");

        if (diffs.Count == 0)
        {
            sb.Append("<div class='empty'><p>No data.</p></div>");
            return Page(title, sb.ToString());
        }

        sb.Append("<table><thead><tr>");
        sb.Append("<th>#</th><th>Method</th><th>A (ms)</th><th>B (ms)</th><th>Delta</th><th>% Change</th><th>URL</th>");
        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < diffs.Count; i++)
        {
            var d = diffs[i];
            var rowClass = d.Category == DiffCategory.Regression  ? " class='row-5xx'" :
                           d.Category == DiffCategory.Improvement ? " class='row-improved'" : "";

            var countHintA = d.CountA > 1 ? $" <span class='dim'>(avg {d.CountA})</span>" : "";
            var countHintB = d.CountB > 1 ? $" <span class='dim'>(avg {d.CountB})</span>" : "";

            sb.Append($"<tr{rowClass}>");
            sb.Append($"<td class='num dim'>{i + 1}</td>");
            sb.Append($"<td>{MethodBadge(d.Method)}</td>");
            sb.Append($"<td class='num'>{FormatMs(d.AvgTotalMsA)}{countHintA}</td>");
            sb.Append($"<td class='num'>{FormatMs(d.AvgTotalMsB)}{countHintB}</td>");
            sb.Append($"<td class='num'>{DeltaBadge(d.DeltaMs)}</td>");
            sb.Append($"<td class='num'>{PctBadge(d.PctChange)}</td>");
            sb.Append($"<td class='url-cell'><span class='url' title='{H(d.BaseUrl)}'>{H(Truncate(d.BaseUrl, 90))}</span></td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return Page(title, sb.ToString());
    }

    public static string BuildCompareOnlyIn(string title, string fileName, List<AnalysisEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>{H(title)} — {H(fileName)} ({entries.Count:N0})</h1>");
        sb.Append(TimingTable(entries));
        return Page(title, sb.ToString());
    }

    public static string BuildCompareByDomain(CompareResult r)
    {
        var groups = r.Diffs
            .GroupBy(d => d.Host)
            .Select(g => new
            {
                Domain       = g.Key,
                Matched      = g.Count(),
                SumDelta     = g.Sum(d => d.DeltaMs),
                AvgDelta     = g.Average(d => d.DeltaMs),
                Regressions  = g.Count(d => d.Category == DiffCategory.Regression),
                Improvements = g.Count(d => d.Category == DiffCategory.Improvement),
            })
            .OrderByDescending(g => Math.Abs(g.SumDelta))
            .ToList();

        var sb = new StringBuilder();
        sb.Append("<h1>Compare — By Domain</h1>");

        if (groups.Count == 0)
        {
            sb.Append("<div class='empty'><p>No matched requests to group by domain.</p></div>");
            return Page("Compare By Domain", sb.ToString());
        }

        sb.Append("<table><thead><tr>");
        sb.Append("<th>Domain</th><th>Matched</th><th>Sum Δ (ms)</th><th>Avg Δ (ms)</th><th>Regressions</th><th>Improvements</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var g in groups)
        {
            sb.Append("<tr>");
            sb.Append($"<td><code>{H(g.Domain)}</code></td>");
            sb.Append($"<td class='num'>{g.Matched:N0}</td>");
            sb.Append($"<td class='num'>{DeltaBadge(g.SumDelta)}</td>");
            sb.Append($"<td class='num'>{DeltaBadge(Math.Round(g.AvgDelta, 1))}</td>");
            sb.Append($"<td class='num{(g.Regressions > 0 ? " error" : "")}'>{g.Regressions:N0}</td>");
            sb.Append($"<td class='num{(g.Improvements > 0 ? " ok" : "")}'>{g.Improvements:N0}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        return Page("Compare By Domain", sb.ToString());
    }

    // ── Compare helpers ───────────────────────────────────────────────────

    private static string DeltaBadge(double deltaMs)
    {
        if (Math.Abs(deltaMs) < 0.05) return "<span class='dim'>—</span>";
        var cls   = deltaMs > 0 ? "delta-reg" : "delta-imp";
        var sign  = deltaMs > 0 ? "+" : "";
        return $"<span class='{cls}'>{sign}{FormatMs(deltaMs)}</span>";
    }

    private static string PctBadge(double pct)
    {
        if (double.IsNaN(pct)) return "<span class='dim'>N/A</span>";
        if (Math.Abs(pct) < 0.05) return "<span class='dim'>—</span>";
        var cls  = pct > 0 ? "delta-reg" : "delta-imp";
        var sign = pct > 0 ? "+" : "";
        return $"<span class='{cls}'>{sign}{pct:F1}%</span>";
    }

    // ── Shared table builder ───────────────────────────────────────────────

    private static string TimingTable(List<AnalysisEntry> rows, bool highlightTtfb = false)
    {
        if (rows.Count == 0) return "<div class='empty'><p>No data.</p></div>";
        var maxMs = rows.Max(r => r.TotalMs);

        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>");
        sb.Append("<th>#</th><th>Method</th><th>Status</th><th>Total</th><th>TTFB</th><th>Recv</th><th>Connect</th><th>SSL</th><th>Mime Type</th><th>URL</th>");
        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < rows.Count; i++)
        {
            var e = rows[i];
            var rowClass = e.Status >= 500 ? " class='row-5xx'" :
                           e.Status >= 400 ? " class='row-4xx'" :
                           e.Status == 0   ? " class='row-0'"   : "";

            sb.Append($"<tr{rowClass}>");
            sb.Append($"<td class='num dim'>{i + 1}</td>");
            sb.Append($"<td>{MethodBadge(e.Method)}</td>");
            sb.Append($"<td>{StatusBadge(e.Status, e.StatusText)}</td>");
            sb.Append($"<td class='num {(highlightTtfb ? "" : SpeedClass(e.TotalMs))}'>{FormatMs(e.TotalMs)}</td>");
            sb.Append($"<td class='num {(highlightTtfb ? SpeedClass(e.TtfbMs) : "")}'>{FormatMs(e.TtfbMs)}</td>");
            sb.Append($"<td class='num'>{FormatMs(e.ReceiveMs)}</td>");
            sb.Append($"<td class='num'>{FormatMs(e.ConnectMs)}</td>");
            sb.Append($"<td class='num'>{FormatMs(e.SslMs)}</td>");
            sb.Append($"<td class='mime'>{H(e.MimeType)}</td>");
            sb.Append($"<td class='url-cell'><span class='url' title='{H(e.Url)}'>{H(Truncate(e.Url, 90))}</span></td>");
            sb.Append("</tr>");

            // Timing bar row
            if (maxMs > 0)
            {
                sb.Append("<tr class='bar-row'><td colspan='3'></td><td colspan='7'>");
                sb.Append(TimingBar(e, maxMs));
                sb.Append("</td></tr>");
            }
        }

        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string TimingBar(AnalysisEntry e, double maxMs)
    {
        double total = e.BlockedMs + e.DnsMs + e.ConnectMs + e.SslMs + e.SendMs + e.TtfbMs + e.ReceiveMs;
        if (total <= 0) return "";

        string Seg(string cls, double ms) =>
            ms > 0 ? $"<div class='t-{cls}' style='flex:{ms / maxMs:F4}' title='{cls}: {FormatMs(ms)}'></div>" : "";

        return $"<div class='tbar'>{Seg("blocked", e.BlockedMs)}{Seg("dns", e.DnsMs)}{Seg("connect", e.ConnectMs)}{Seg("ssl", e.SslMs)}{Seg("send", e.SendMs)}{Seg("wait", e.TtfbMs)}{Seg("recv", e.ReceiveMs)}</div>";
    }

    // ── HTML helpers ───────────────────────────────────────────────────────

    private static string Page(string title, string body) =>
        $"<!DOCTYPE html><html><head><meta charset='utf-8'/><title>{H(title)}</title><style>{Css}</style></head><body>{body}</body></html>";

    private static void Card(StringBuilder sb, string label, string value, string cls)
    {
        var clsAttr = string.IsNullOrEmpty(cls) ? "" : $" class='cv-{cls}'";
        sb.Append($"<div class='card'><div class='cl'>{H(label)}</div><div class='cv'{clsAttr}>{value}</div></div>");
    }

    private static void InfoRow(StringBuilder sb, string label, string value)
    {
        sb.Append($"<tr><th>{H(label)}</th><td>{value}</td></tr>");
    }

    private static string StatusBadge(int status, string text = "")
    {
        var cls = status == 0 ? "s0" : status < 300 ? "s2" : status < 400 ? "s3" : status < 500 ? "s4" : "s5";
        var label = status == 0 ? "—" : status.ToString();
        var tip = string.IsNullOrEmpty(text) ? "" : $" title='{H(text)}'";
        return $"<span class='badge {cls}'{tip}>{label}</span>";
    }

    private static string MethodBadge(string method) =>
        $"<span class='meth m-{method.ToLowerInvariant()}'>{H(method)}</span>";

    private static string SpeedClass(double ms) =>
        ms >= 3000 ? "very-slow" : ms >= 1000 ? "slow" : "";

    private static string FormatMs(double ms)
    {
        if (ms >= 1000) return $"{ms / 1000:F2} s";
        return $"{ms:F0} ms";
    }

    private static string Truncate(string s, int max) =>
        s.Length > max ? s[..max] + "…" : s;

    private static string H(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // ── CSS ───────────────────────────────────────────────────────────────

    private const string Css = @"
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:'Segoe UI',Tahoma,sans-serif;font-size:13px;color:#1f1f1f;background:#f3f3f3;padding:18px;line-height:1.4}
h1{font-size:17px;font-weight:600;color:#1a1a2e;margin-bottom:16px;padding-bottom:8px;border-bottom:2px solid #0078d4}
h2{font-size:14px;font-weight:600;color:#333;margin:20px 0 8px}
code{font-size:12px;font-family:Consolas,monospace;background:#e8e8e8;padding:1px 4px;border-radius:3px}
p{margin-bottom:8px}
.note{background:#fff4ce;border-left:3px solid #ffd700;padding:8px 12px;border-radius:0 4px 4px 0;margin-bottom:14px;font-size:12px}
.empty{text-align:center;padding:60px 20px;color:#888}
.empty h2{color:#555;margin-bottom:8px}

/* Cards */
.cards{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:22px}
.card{background:#fff;border-radius:6px;padding:12px 18px;box-shadow:0 1px 3px rgba(0,0,0,.12);min-width:130px}
.cl{font-size:11px;color:#666;text-transform:uppercase;letter-spacing:.4px;margin-bottom:5px}
.cv{font-size:22px;font-weight:700;color:#0078d4}
.cv-warn{color:#ca5010}
.cv-error{color:#d13438}
.cv-ok{color:#107c10}

/* Tables */
table{width:100%;border-collapse:collapse;background:#fff;border-radius:6px;box-shadow:0 1px 3px rgba(0,0,0,.1);overflow:hidden;margin-bottom:20px;font-size:12px}
thead th{background:#0078d4;color:#fff;padding:9px 10px;text-align:left;font-weight:600;font-size:11px;white-space:nowrap}
tbody tr:nth-child(4n+1),tbody tr:nth-child(4n+2){background:#fafafa}
tbody tr.bar-row{background:transparent!important}
tbody tr:hover td{background:#deeaf8!important}
tbody tr.bar-row:hover td{background:transparent!important}
tbody td,tbody th{padding:6px 10px;border-bottom:1px solid #efefef;vertical-align:middle}
tbody th{font-weight:600;white-space:nowrap;background:#f5f5f5;width:90px}

/* Row highlights for errors */
tr.row-4xx td{background:#fff3f3!important}
tr.row-5xx td{background:#ffe8e8!important}
tr.row-0   td{background:#f5f5f5!important}

/* Badges */
.badge{display:inline-block;padding:2px 7px;border-radius:10px;font-size:11px;font-weight:600;font-family:Consolas,monospace}
.s0{background:#f0f0f0;color:#666}
.s2{background:#dff6dd;color:#0a6b0a}
.s3{background:#fff4ce;color:#7a4f01}
.s4{background:#fde7e9;color:#a00e17}
.s5{background:#fde7e9;color:#a00e17}

/* Method badges */
.meth{display:inline-block;padding:2px 5px;border-radius:3px;font-size:10px;font-weight:700;font-family:Consolas,monospace;min-width:40px;text-align:center}
.m-get{background:#e6f4ea;color:#1e7e34}
.m-post{background:#fff3e0;color:#c85000}
.m-put{background:#e3f2fd;color:#0d47a1}
.m-delete{background:#fce4ec;color:#880e4f}
.m-patch{background:#f3e5f5;color:#6a1b9a}
.m-head,.m-options,.m-connect,.m-trace{background:#f5f5f5;color:#424242}

/* Timing */
.num{text-align:right;font-family:Consolas,monospace;white-space:nowrap}
.dim{color:#bbb}
.slow{color:#ca5010;font-weight:600}
.very-slow{color:#d13438;font-weight:700}
.warn{color:#ca5010}
.error{color:#d13438}

.tbar{display:flex;height:5px;margin:1px 0 4px;border-radius:3px;overflow:hidden;min-width:60px}
.t-blocked{background:#bdbdbd}
.t-dns{background:#64b5f6}
.t-connect{background:#f06292}
.t-ssl{background:#ba68c8}
.t-send{background:#ffd54f}
.t-wait{background:#4dd0e1}
.t-recv{background:#81c784}

/* URL cell */
.url-cell{max-width:0;width:40%}
.url{display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-family:Consolas,monospace;font-size:11px;max-width:100%}
.mime{font-size:11px;color:#555;white-space:nowrap}

/* Sparkline */
.spark-wrap{display:flex;align-items:flex-end;gap:2px;height:90px;background:#fff;border-radius:6px;padding:8px 10px;box-shadow:0 1px 3px rgba(0,0,0,.1);margin-bottom:18px}
.spark-col{display:flex;align-items:flex-end}
.spark-bar{width:10px;border-radius:2px 2px 0 0;background:#0078d4;min-height:2px}
.spark-bar.warn{background:#ca5010}
.spark-bar.error{background:#d13438}

/* Compare */
tr.row-improved td{background:#efffef!important}
.delta-reg{color:#d13438;font-weight:600;font-family:Consolas,monospace}
.delta-imp{color:#107c10;font-weight:600;font-family:Consolas,monospace}
.ok{color:#107c10}
";
}
