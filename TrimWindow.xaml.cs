using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HARAnalyzer.Models;
using HARAnalyzer.Services;
using Microsoft.Win32;

namespace HARAnalyzer;

public partial class TrimWindow : Window
{
    private readonly string _sourcePath;
    private readonly List<TrimBucket> _buckets;
    private readonly int _totalEntries;

    public TrimWindow(AnalysisResult result, string sourcePath)
    {
        InitializeComponent();
        _sourcePath   = sourcePath;
        _totalEntries = result.TotalEntries;
        Title         = $"Trim HAR — {result.FileName}";

        _buckets = BuildBuckets(result.Entries);
        BucketsGrid.ItemsSource = _buckets;
    }

    // ── Bucket construction ───────────────────────────────────────────────

    private List<TrimBucket> BuildBuckets(List<AnalysisEntry> entries)
    {
        var valid = entries.Where(e => e.Started > DateTime.MinValue).ToList();
        if (valid.Count == 0) return [];

        var origin = valid.Min(e => e.Started);

        return valid
            .GroupBy(e => Math.Floor((e.Started - origin).TotalSeconds / 5) * 5)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var absStart = origin.AddSeconds(g.Key);
                return new TrimBucket
                {
                    Offset        = $"+{g.Key:0}s",
                    StartTime     = absStart.ToString("HH:mm:ss"),
                    Requests      = g.Count(),
                    TotalTime     = FormatMs(g.Sum(e => e.TotalMs)),
                    WillKeep      = valid.Count(e => e.Started >= absStart),
                    AbsoluteStart = absStart,
                };
            })
            .ToList();
    }

    // ── Grid selection ────────────────────────────────────────────────────

    private void BucketsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BucketsGrid.SelectedItem is not TrimBucket selected)
        {
            UpdateRowStyles(-1);
            StatusLabel.Text  = "Select a row to preview the trim.";
            BtnSave.IsEnabled = false;
            return;
        }

        var idx         = _buckets.IndexOf(selected);
        var keepCount   = selected.WillKeep;
        var removeCount = _totalEntries - keepCount;

        UpdateRowStyles(idx);
        StatusLabel.Text  = $"Keeping {keepCount:N0} request{(keepCount == 1 ? "" : "s")}  ·  " +
                            $"Removing {removeCount:N0} request{(removeCount == 1 ? "" : "s")}  ·  " +
                            $"From {selected.StartTime} onwards";
        BtnSave.IsEnabled = true;
    }

    private void UpdateRowStyles(int selectedIdx)
    {
        for (var i = 0; i < _buckets.Count; i++)
            _buckets[i].IsRemoved = selectedIdx >= 0 && i < selectedIdx;
    }

    // ── Save ──────────────────────────────────────────────────────────────

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (BucketsGrid.SelectedItem is not TrimBucket selected) return;

        var suggested = Path.GetFileNameWithoutExtension(_sourcePath) + "-trimmed.har";
        var dlg = new SaveFileDialog
        {
            Title      = "Save Trimmed HAR",
            Filter     = "HAR Files (*.har)|*.har|All Files (*.*)|*.*",
            FileName   = suggested,
            DefaultExt = ".har",
        };
        if (dlg.ShowDialog() != true) return;

        BtnSave.IsEnabled = false;
        try
        {
            var kept = await HarWriter.TrimAsync(_sourcePath, dlg.FileName, selected.AbsoluteStart);
            MessageBox.Show(
                $"Saved {kept:N0} request{(kept == 1 ? "" : "s")} to:\n{dlg.FileName}",
                "Trim Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save trimmed file:\n\n{ex.Message}",
                "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            BtnSave.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string FormatMs(double ms) =>
        ms >= 1000 ? $"{ms / 1000:F2} s" : $"{ms:F0} ms";

    // ── Bucket model ──────────────────────────────────────────────────────

    private sealed class TrimBucket : INotifyPropertyChanged
    {
        private bool _isRemoved;

        public bool IsRemoved
        {
            get => _isRemoved;
            set
            {
                if (_isRemoved == value) return;
                _isRemoved = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRemoved)));
            }
        }

        public string   Offset        { get; init; } = "";
        public string   StartTime     { get; init; } = "";
        public int      Requests      { get; init; }
        public string   TotalTime     { get; init; } = "";
        public int      WillKeep      { get; init; }
        public DateTime AbsoluteStart { get; init; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
