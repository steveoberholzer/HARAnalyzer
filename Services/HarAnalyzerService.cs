using System.IO;
using HARAnalyzer.Models;

namespace HARAnalyzer.Services;

public static class HarAnalyzerService
{
    private const double SlowThresholdMs = 1_000;

    public static AnalysisResult Analyze(HarFile har, string filePath)
    {
        var log = har.Log;
        var entries = log.Entries
            .Select(ToAnalysisEntry)
            .OrderBy(e => e.Started)
            .ToList();

        var validDates = entries.Where(e => e.Started > DateTime.MinValue).ToList();
        var timeSpan = validDates.Count > 1
            ? validDates.Max(e => e.Started) - validDates.Min(e => e.Started)
            : TimeSpan.Zero;

        return new AnalysisResult
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            CreatorName = log.Creator?.Name ?? "",
            CreatorVersion = log.Creator?.Version ?? "",
            HarVersion = log.Version,
            LoadedAt = DateTime.Now,

            TotalEntries = entries.Count,
            TotalMs = entries.Sum(e => e.TotalMs),
            AvgMs = entries.Count > 0 ? entries.Average(e => e.TotalMs) : 0,
            MaxMs = entries.Count > 0 ? entries.Max(e => e.TotalMs) : 0,
            MaxTtfbMs = entries.Count > 0 ? entries.Max(e => e.TtfbMs) : 0,
            SlowCount = entries.Count(e => e.TotalMs >= SlowThresholdMs),
            ErrorCount = entries.Count(e => e.Status >= 400 || e.Status == 0),
            TimeSpanCovered = timeSpan,

            Entries = entries
        };
    }

    private static AnalysisEntry ToAnalysisEntry(HarEntry e)
    {
        var t = e.Timings;
        var blocked = Pos(t.Blocked);
        var dns     = Pos(t.Dns);
        var connect = Pos(t.Connect);
        var ssl     = Pos(t.Ssl);
        var send    = Pos(t.Send);
        var wait    = Pos(t.Wait);
        var receive = Pos(t.Receive);
        var total   = blocked + dns + connect + ssl + send + wait + receive;

        DateTime.TryParse(e.StartedDateTime, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var started);

        var url = e.Request.Url;
        string host;
        try { host = new Uri(url).Host; }
        catch { host = "unknown"; }

        var mime = e.Response.Content.MimeType;
        if (mime.Length > 60) mime = mime[..60];

        return new AnalysisEntry
        {
            Started         = started,
            Method          = e.Request.Method.ToUpperInvariant(),
            Status          = e.Response.Status,
            StatusText      = e.Response.StatusText,
            TotalMs         = Math.Round(total, 1),
            TtfbMs          = Math.Round(wait, 1),
            ReceiveMs       = Math.Round(receive, 1),
            ConnectMs       = Math.Round(connect, 1),
            SslMs           = Math.Round(ssl, 1),
            BlockedMs       = Math.Round(blocked, 1),
            DnsMs           = Math.Round(dns, 1),
            SendMs          = Math.Round(send, 1),
            MimeType        = mime,
            Url             = url,
            Host            = host,
            RequestBodySize  = Math.Max(0, e.Request.BodySize),
            ResponseBodySize = Math.Max(0, e.Response.BodySize),
        };
    }

    // HAR spec allows -1 to mean "not applicable"
    private static double Pos(double v) => v < 0 ? 0 : v;
}
