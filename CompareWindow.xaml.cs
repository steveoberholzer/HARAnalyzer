using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HARAnalyzer.Models;
using HARAnalyzer.Services;
using Microsoft.Win32;

namespace HARAnalyzer;

public partial class CompareWindow : Window
{
    private readonly CompareResult _result;

    public CompareWindow(CompareResult result)
    {
        InitializeComponent();
        _result = result;
        Title = $"HAR Analyzer — Compare: {result.FileNameA}  vs  {result.FileNameB}";
    }

    // ── Initialisation ────────────────────────────────────────────────────

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ContentBrowser.EnsureCoreWebView2Async(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 runtime not found.\n\n{ex.Message}",
                "WebView2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BuildTree(_result);

        // Auto-select Summary node
        if (CompareTree.Items.Count > 0 &&
            CompareTree.Items[0] is TreeViewItem root &&
            root.Items.Count > 0 &&
            root.Items[0] is TreeViewItem summaryNode)
        {
            summaryNode.IsSelected = true;
        }
    }

    // ── Tree building ──────────────────────────────────────────────────────

    private void BuildTree(CompareResult r)
    {
        CompareTree.Items.Clear();
        var baseName = $"{Path.GetFileNameWithoutExtension(r.FileNameA)}-vs-{Path.GetFileNameWithoutExtension(r.FileNameB)}";

        var root = MakeItem($"🔀 {r.FileNameA}  vs  {r.FileNameB}",
            getHtml: () => HtmlBuilder.BuildCompareSummary(r),
            getCsv:  () => CsvBuilder.BuildCompareSummary(r),
            csvFileName: $"{baseName}-summary.csv",
            bold: true);
        root.IsExpanded = true;

        // 1 — Summary
        root.Items.Add(MakeItem("📊 Summary",
            getHtml: () => HtmlBuilder.BuildCompareSummary(r),
            getCsv:  () => CsvBuilder.BuildCompareSummary(r),
            csvFileName: $"{baseName}-summary.csv"));

        // 2 — All Differences
        root.Items.Add(MakeItem($"📋 All Differences ({r.MatchedCount:N0})",
            getHtml: () => HtmlBuilder.BuildCompareDiffTable("All Differences", r.Diffs),
            getCsv:  () => CsvBuilder.BuildCompareDiffs(r.Diffs),
            csvFileName: $"{baseName}-all-diffs.csv"));

        // 3 — Regressions
        var regLabel = r.RegressionCount > 0
            ? $"❌ Regressions ({r.RegressionCount:N0})"
            : "✅ Regressions (none)";
        root.Items.Add(MakeItem(regLabel,
            getHtml: () => HtmlBuilder.BuildCompareDiffTable("Regressions", r.Regressions),
            getCsv:  () => CsvBuilder.BuildCompareDiffs(r.Regressions),
            csvFileName: $"{baseName}-regressions.csv"));

        // 4 — Improvements
        var impLabel = r.ImprovementCount > 0
            ? $"🚀 Improvements ({r.ImprovementCount:N0})"
            : "— Improvements (none)";
        root.Items.Add(MakeItem(impLabel,
            getHtml: () => HtmlBuilder.BuildCompareDiffTable("Improvements", r.Improvements),
            getCsv:  () => CsvBuilder.BuildCompareDiffs(r.Improvements),
            csvFileName: $"{baseName}-improvements.csv"));

        // 5 — Only in A
        root.Items.Add(MakeItem($"Only in A — {r.FileNameA} ({r.OnlyInACount:N0})",
            getHtml: () => HtmlBuilder.BuildCompareOnlyIn("Only in A", r.FileNameA, r.OnlyInA),
            getCsv:  () => CsvBuilder.BuildEntries(r.OnlyInA),
            csvFileName: $"{baseName}-only-in-a.csv"));

        // 6 — Only in B
        root.Items.Add(MakeItem($"Only in B — {r.FileNameB} ({r.OnlyInBCount:N0})",
            getHtml: () => HtmlBuilder.BuildCompareOnlyIn("Only in B", r.FileNameB, r.OnlyInB),
            getCsv:  () => CsvBuilder.BuildEntries(r.OnlyInB),
            csvFileName: $"{baseName}-only-in-b.csv"));

        // 7 — By Domain
        root.Items.Add(MakeItem("🌐 By Domain",
            getHtml: () => HtmlBuilder.BuildCompareByDomain(r),
            getCsv:  () => CsvBuilder.BuildCompareByDomain(r),
            csvFileName: $"{baseName}-by-domain.csv"));

        CompareTree.Items.Add(root);
        root.IsExpanded = true;
    }

    private static TreeViewItem MakeItem(
        string header,
        Func<string>? getHtml,
        Func<string>? getCsv    = null,
        string csvFileName       = "export.csv",
        bool bold                = false)
    {
        var tb = new TextBlock { Text = header };
        if (bold) tb.FontWeight = FontWeights.SemiBold;

        return new TreeViewItem
        {
            Header = tb,
            Tag    = new TreeNodeData
            {
                GetHtml     = getHtml,
                GetCsv      = getCsv,
                CsvFileName = csvFileName,
            },
        };
    }

    // ── Tree selection ────────────────────────────────────────────────────

    private void CompareTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item) return;
        if (item.Tag is not TreeNodeData { GetHtml: { } getHtml }) return;
        ContentBrowser.CoreWebView2?.NavigateToString(getHtml());
    }

    // ── Context menu ──────────────────────────────────────────────────────

    private void CompareTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item != null) item.IsSelected = true;
    }

    private void TreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var cm         = (ContextMenu)sender;
        var exportItem = (MenuItem)cm.Items[0];
        var copyItem   = (MenuItem)cm.Items[1];

        var hasCsv = CompareTree.SelectedItem is TreeViewItem sel &&
                     sel.Tag is TreeNodeData { GetCsv: not null };

        exportItem.IsEnabled = hasCsv;
        copyItem.IsEnabled   = hasCsv;
    }

    private async void CtxExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCsvData(out var csv, out var suggestedName)) return;

        var dlg = new SaveFileDialog
        {
            Title      = "Export to CSV",
            Filter     = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName   = suggestedName,
            DefaultExt = ".csv",
        };
        if (dlg.ShowDialog() != true) return;

        await File.WriteAllTextAsync(dlg.FileName, csv,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        SetStatus($"Exported → {dlg.FileName}");
    }

    private void CtxCopyCsv_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCsvData(out var csv, out _)) return;
        Clipboard.SetText(csv);
        SetStatus("CSV copied to clipboard.");
    }

    private bool TryGetCsvData(out string csv, out string fileName)
    {
        csv      = "";
        fileName = "export.csv";

        if (CompareTree.SelectedItem is not TreeViewItem item) return false;
        if (item.Tag is not TreeNodeData { GetCsv: { } getCsv } data) return false;

        csv      = getCsv();
        fileName = data.CsvFileName;
        return true;
    }

    // ── Export menu ────────────────────────────────────────────────────────

    private async void MenuExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var baseName = $"{Path.GetFileNameWithoutExtension(_result.FileNameA)}" +
                       $"-vs-{Path.GetFileNameWithoutExtension(_result.FileNameB)}";

        var dlg = new SaveFileDialog
        {
            Title      = "Export Comparison to Excel",
            Filter     = "Excel Workbook (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
            FileName   = $"{baseName}-comparison.xlsx",
            DefaultExt = ".xlsx",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            SetStatus("Generating Excel workbook…");
            var bytes = await Task.Run(() => HARAnalyzer.Services.ExcelBuilder.Build(_result));
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            SetStatus($"Exported → {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to export Excel workbook.\n\n{ex.Message}",
                "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Export failed.");
        }
    }

    // ── View menu ──────────────────────────────────────────────────────────

    private void MenuExpandAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in CompareTree.Items.OfType<TreeViewItem>())
            SetExpanded(item, true);
    }

    private void MenuCollapseAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in CompareTree.Items.OfType<TreeViewItem>())
            SetExpanded(item, false);
    }

    private static void SetExpanded(TreeViewItem item, bool expanded)
    {
        item.IsExpanded = expanded;
        foreach (var child in item.Items.OfType<TreeViewItem>())
            SetExpanded(child, expanded);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SetStatus(string text) => StatusText.Text = text;

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
