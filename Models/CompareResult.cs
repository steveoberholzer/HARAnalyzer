namespace HARAnalyzer.Models;

public class CompareResult
{
    public string FileNameA { get; set; } = "";
    public string FilePathA { get; set; } = "";
    public string FileNameB { get; set; } = "";
    public string FilePathB { get; set; } = "";
    public DateTime ComparedAt { get; set; }
    public int TotalInA { get; set; }
    public int TotalInB { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInACount { get; set; }
    public int OnlyInBCount { get; set; }
    public int RegressionCount { get; set; }
    public int ImprovementCount { get; set; }
    public double OverallDeltaMs { get; set; }
    public List<RequestDiff> Diffs { get; set; } = [];
    public List<RequestDiff> Regressions { get; set; } = [];
    public List<RequestDiff> Improvements { get; set; } = [];
    public List<AnalysisEntry> OnlyInA { get; set; } = [];
    public List<AnalysisEntry> OnlyInB { get; set; } = [];
}

public class RequestDiff
{
    public string Method { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Host { get; set; } = "";
    public double AvgTotalMsA { get; set; }
    public double AvgTtfbMsA { get; set; }
    public double AvgTotalMsB { get; set; }
    public double AvgTtfbMsB { get; set; }
    public double DeltaMs { get; set; }
    public double DeltaTtfbMs { get; set; }
    public double PctChange { get; set; }    // NaN when AvgTotalMsA == 0
    public int CountA { get; set; }
    public int CountB { get; set; }
    public DiffCategory Category { get; set; }
}

public enum DiffCategory { Negligible, Regression, Improvement }
