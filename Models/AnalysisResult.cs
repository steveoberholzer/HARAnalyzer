namespace HARAnalyzer.Models;

public class AnalysisResult
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public string CreatorVersion { get; set; } = "";
    public string HarVersion { get; set; } = "";
    public DateTime LoadedAt { get; set; }

    public int TotalEntries { get; set; }
    public double TotalMs { get; set; }
    public double AvgMs { get; set; }
    public double MaxMs { get; set; }
    public double MaxTtfbMs { get; set; }
    public int SlowCount { get; set; }    // > 1 000 ms
    public int ErrorCount { get; set; }   // 4xx / 5xx / 0
    public TimeSpan TimeSpanCovered { get; set; }

    public List<AnalysisEntry> Entries { get; set; } = [];
}

public class AnalysisEntry
{
    public DateTime Started { get; set; }
    public string Method { get; set; } = "";
    public int Status { get; set; }
    public string StatusText { get; set; } = "";

    // Timings (ms, negatives clamped to 0)
    public double TotalMs { get; set; }
    public double TtfbMs { get; set; }    // wait
    public double ReceiveMs { get; set; }
    public double ConnectMs { get; set; }
    public double SslMs { get; set; }
    public double BlockedMs { get; set; }
    public double DnsMs { get; set; }
    public double SendMs { get; set; }

    public string MimeType { get; set; } = "";
    public string Url { get; set; } = "";
    public string Host { get; set; } = "";
    public long RequestBodySize { get; set; }
    public long ResponseBodySize { get; set; }
}
