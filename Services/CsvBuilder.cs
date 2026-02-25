using System.Text;
using HARAnalyzer.Models;

namespace HARAnalyzer.Services;

/// <summary>Produces RFC-4180 CSV text (UTF-8 with BOM) for each tree-view section.</summary>
public static class CsvBuilder
{
    // ── Public entry points ────────────────────────────────────────────────

    /// <summary>Key-value summary statistics.</summary>
    public static string BuildSummary(AnalysisResult r)
    {
        var sb = new StringBuilder();
        Header(sb, "Metric", "Value");
        Row(sb, "File",                    r.FileName);
        Row(sb, "Path",                    r.FilePath);
        Row(sb, "HAR Version",             r.HarVersion);
        Row(sb, "Creator",                 $"{r.CreatorName} {r.CreatorVersion}".Trim());
        Row(sb, "Loaded At",               r.LoadedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        Row(sb, "Total Requests",          r.TotalEntries.ToString());
        Row(sb, "Total Time (ms)",         r.TotalMs.ToString("F1"));
        Row(sb, "Avg Request (ms)",        r.AvgMs.ToString("F1"));
        Row(sb, "Max Request (ms)",        r.MaxMs.ToString("F1"));
        Row(sb, "Max TTFB (ms)",           r.MaxTtfbMs.ToString("F1"));
        Row(sb, "Slow Requests (>1 000 ms)", r.SlowCount.ToString());
        Row(sb, "Errors (4xx/5xx/0)",      r.ErrorCount.ToString());
        Row(sb, "Recording Duration",      r.TimeSpanCovered.ToString(@"mm\:ss"));

        // Status breakdown
        sb.AppendLine();
        Header(sb, "Status Group", "Count", "% of Total");
        var groups = r.Entries
            .GroupBy(e => e.Status == 0 ? "0 (no response)" : (e.Status / 100) + "xx")
            .OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            var pct = r.TotalEntries > 0 ? g.Count() * 100.0 / r.TotalEntries : 0;
            Row(sb, g.Key, g.Count().ToString(), pct.ToString("F1") + "%");
        }

        return Bom + sb;
    }

    /// <summary>Flat request table — used by Slowest, Errors, Domain Detail, All Requests.</summary>
    public static string BuildEntries(IEnumerable<AnalysisEntry> entries)
    {
        var list = entries.ToList();
        var sb   = new StringBuilder();
        Header(sb,
            "#", "Started", "Method", "Status", "Status Text",
            "Total (ms)", "TTFB (ms)", "Receive (ms)", "Connect (ms)",
            "SSL (ms)", "Blocked (ms)", "DNS (ms)", "Send (ms)",
            "MIME Type", "Host", "URL");

        for (var i = 0; i < list.Count; i++)
        {
            var e = list[i];
            Row(sb,
                (i + 1).ToString(),
                e.Started == DateTime.MinValue ? "" : e.Started.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                e.Method,
                e.Status.ToString(),
                e.StatusText,
                e.TotalMs.ToString("F1"),
                e.TtfbMs.ToString("F1"),
                e.ReceiveMs.ToString("F1"),
                e.ConnectMs.ToString("F1"),
                e.SslMs.ToString("F1"),
                e.BlockedMs.ToString("F1"),
                e.DnsMs.ToString("F1"),
                e.SendMs.ToString("F1"),
                e.MimeType,
                e.Host,
                e.Url);
        }

        return Bom + sb;
    }

    /// <summary>Per-domain aggregates.</summary>
    public static string BuildByDomain(IEnumerable<AnalysisEntry> entries)
    {
        var sb     = new StringBuilder();
        var groups = entries
            .GroupBy(e => e.Host)
            .Select(g => new
            {
                Domain  = g.Key,
                Count   = g.Count(),
                TotalMs = g.Sum(e => e.TotalMs),
                AvgMs   = g.Average(e => e.TotalMs),
                MaxMs   = g.Max(e => e.TotalMs),
                MaxTtfb = g.Max(e => e.TtfbMs),
                Slow    = g.Count(e => e.TotalMs >= 1000),
                Errors  = g.Count(e => e.Status >= 400 || e.Status == 0),
            })
            .OrderByDescending(g => g.TotalMs);

        Header(sb, "Domain", "Requests", "Total Time (ms)", "Avg Time (ms)",
               "Max Time (ms)", "Max TTFB (ms)", "Slow (>1s)", "Errors");

        foreach (var g in groups)
            Row(sb, g.Domain, g.Count.ToString(), g.TotalMs.ToString("F1"),
                g.AvgMs.ToString("F1"), g.MaxMs.ToString("F1"),
                g.MaxTtfb.ToString("F1"), g.Slow.ToString(), g.Errors.ToString());

        return Bom + sb;
    }

    /// <summary>5-second bucket timeline.</summary>
    public static string BuildTimeline(IEnumerable<AnalysisEntry> entries)
    {
        var valid = entries.Where(e => e.Started > DateTime.MinValue).ToList();
        if (valid.Count == 0) return Bom + "No timestamp data available.";

        var origin  = valid.Min(e => e.Started);
        var buckets = valid
            .GroupBy(e => Math.Floor((e.Started - origin).TotalSeconds / 5) * 5)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                OffsetSec = g.Key,
                Label     = $"+{g.Key:0}s",
                Count     = g.Count(),
                TotalMs   = g.Sum(e => e.TotalMs),
                MaxMs     = g.Max(e => e.TotalMs),
                Slow      = g.Count(e => e.TotalMs >= 1000),
                Errors    = g.Count(e => e.Status >= 400 || e.Status == 0),
            });

        var sb = new StringBuilder();
        Header(sb, "Offset (s)", "Label", "Requests", "Total Time (ms)", "Max Time (ms)",
               "Slow (>1s)", "Errors");

        foreach (var b in buckets)
            Row(sb, b.OffsetSec.ToString("F0"), b.Label, b.Count.ToString(),
                b.TotalMs.ToString("F1"), b.MaxMs.ToString("F1"),
                b.Slow.ToString(), b.Errors.ToString());

        return Bom + sb;
    }

    // ── CSV helpers ────────────────────────────────────────────────────────

    // UTF-8 BOM so Excel auto-detects encoding
    private const string Bom = "\uFEFF";

    private static void Header(StringBuilder sb, params string[] cols)
        => sb.AppendLine(string.Join(",", cols.Select(Q)));

    private static void Row(StringBuilder sb, params string[] cells)
        => sb.AppendLine(string.Join(",", cells.Select(Q)));

    /// <summary>Quotes a field per RFC 4180 when it contains commas, quotes, or newlines.</summary>
    private static string Q(string? s)
    {
        if (s is null or "") return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }
}
