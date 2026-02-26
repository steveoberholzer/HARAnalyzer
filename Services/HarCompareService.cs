using HARAnalyzer.Models;

namespace HARAnalyzer.Services;

public static class HarCompareService
{
    private const double DeltaThresholdMs  = 50.0;
    private const double DeltaThresholdPct = 10.0;

    public static CompareResult Compare(AnalysisResult a, AnalysisResult b)
    {
        var mapA = BuildMap(a.Entries);
        var mapB = BuildMap(b.Entries);

        var allKeys = new HashSet<MatchKey>(mapA.Keys.Concat(mapB.Keys));

        var diffs    = new List<RequestDiff>();
        var onlyInA  = new List<AnalysisEntry>();
        var onlyInB  = new List<AnalysisEntry>();

        foreach (var key in allKeys)
        {
            var inA = mapA.TryGetValue(key, out var listA);
            var inB = mapB.TryGetValue(key, out var listB);

            if (inA && inB)
            {
                var avgTotalA = listA!.Average(e => e.TotalMs);
                var avgTtfbA  = listA!.Average(e => e.TtfbMs);
                var avgTotalB = listB!.Average(e => e.TotalMs);
                var avgTtfbB  = listB!.Average(e => e.TtfbMs);

                var delta     = avgTotalB - avgTotalA;
                var deltaTtfb = avgTtfbB  - avgTtfbA;
                var pct       = avgTotalA == 0 ? double.NaN : (delta / avgTotalA) * 100.0;

                // Derive host from the first entry in either list
                var host = listA!.FirstOrDefault()?.Host ?? listB!.FirstOrDefault()?.Host ?? "";

                diffs.Add(new RequestDiff
                {
                    Method       = key.Method,
                    BaseUrl      = key.Url,
                    Host         = host,
                    AvgTotalMsA  = Math.Round(avgTotalA, 1),
                    AvgTtfbMsA   = Math.Round(avgTtfbA,  1),
                    AvgTotalMsB  = Math.Round(avgTotalB, 1),
                    AvgTtfbMsB   = Math.Round(avgTtfbB,  1),
                    DeltaMs      = Math.Round(delta,     1),
                    DeltaTtfbMs  = Math.Round(deltaTtfb, 1),
                    PctChange    = double.IsNaN(pct) ? pct : Math.Round(pct, 1),
                    CountA       = listA!.Count,
                    CountB       = listB!.Count,
                    Category     = ClassifyDiff(delta, pct),
                });
            }
            else if (inA)
            {
                onlyInA.AddRange(listA!);
            }
            else
            {
                onlyInB.AddRange(listB!);
            }
        }

        // Sort diffs by absolute delta descending
        diffs.Sort((x, y) => Math.Abs(y.DeltaMs).CompareTo(Math.Abs(x.DeltaMs)));

        var regressions  = diffs.Where(d => d.Category == DiffCategory.Regression).ToList();
        var improvements = diffs.Where(d => d.Category == DiffCategory.Improvement).ToList();

        // Sort only-in lists by TotalMs descending for readability
        onlyInA.Sort((x, y) => y.TotalMs.CompareTo(x.TotalMs));
        onlyInB.Sort((x, y) => y.TotalMs.CompareTo(x.TotalMs));

        var overallDelta = diffs.Count > 0 ? diffs.Sum(d => d.DeltaMs) : 0;

        return new CompareResult
        {
            FileNameA        = a.FileName,
            FilePathA        = a.FilePath,
            FileNameB        = b.FileName,
            FilePathB        = b.FilePath,
            ComparedAt       = DateTime.Now,
            TotalInA         = a.TotalEntries,
            TotalInB         = b.TotalEntries,
            MatchedCount     = diffs.Count,
            OnlyInACount     = onlyInA.Count,
            OnlyInBCount     = onlyInB.Count,
            RegressionCount  = regressions.Count,
            ImprovementCount = improvements.Count,
            OverallDeltaMs   = Math.Round(overallDelta, 1),
            Diffs            = diffs,
            Regressions      = regressions,
            Improvements     = improvements,
            OnlyInA          = onlyInA,
            OnlyInB          = onlyInB,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Dictionary<MatchKey, List<AnalysisEntry>> BuildMap(List<AnalysisEntry> entries)
    {
        var map = new Dictionary<MatchKey, List<AnalysisEntry>>();
        foreach (var e in entries)
        {
            var key = new MatchKey(e.Method, StripQuery(e.Url));
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }
            list.Add(e);
        }
        return map;
    }

    private static string StripQuery(string url)
    {
        try
        {
            return new Uri(url).GetLeftPart(UriPartial.Path);
        }
        catch
        {
            var q = url.IndexOf('?');
            return q >= 0 ? url[..q] : url;
        }
    }

    private static DiffCategory ClassifyDiff(double deltaMs, double pct)
    {
        if (double.IsNaN(pct))        return DiffCategory.Negligible;
        if (deltaMs >  DeltaThresholdMs && pct >  DeltaThresholdPct) return DiffCategory.Regression;
        if (deltaMs < -DeltaThresholdMs && pct < -DeltaThresholdPct) return DiffCategory.Improvement;
        return DiffCategory.Negligible;
    }

    private record MatchKey(string Method, string Url);
}
