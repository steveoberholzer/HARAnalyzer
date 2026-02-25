using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HARAnalyzer.Models;
using HARAnalyzer.Services;
using Microsoft.Win32;

namespace HARAnalyzer;

public partial class MainWindow : Window
{
    private readonly MruService _mru = new();
    private AnalysisResult? _currentResult;
    private string? _currentFilePath;

    // ── Initialisation ────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        _mru.Load();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ContentBrowser.EnsureCoreWebView2Async(null);
            ContentBrowser.CoreWebView2.NavigateToString(HtmlBuilder.BuildWelcome());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 runtime not found.\n\n{ex.Message}\n\nPlease install the Microsoft Edge WebView2 Runtime from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/",
                "WebView2 Required", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RebuildMruMenu();
    }

    // Called from App.xaml.cs when a .har is passed on the command line
    public void OpenFileOnStartup(string path)
    {
        Loaded += async (_, _) => await LoadFileAsync(path);
    }

    // ── File loading ──────────────────────────────────────────────────────

    private async void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Open HAR File",
            Filter      = "HAR Files (*.har)|*.har|All Files (*.*)|*.*",
            Multiselect = false,
        };
        if (dlg.ShowDialog() != true) return;
        await LoadFileAsync(dlg.FileName);
    }

    private async void MenuReload_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath != null)
            await LoadFileAsync(_currentFilePath);
    }

    private async Task LoadFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show($"File not found:\n{path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusy(true, $"Loading {Path.GetFileName(path)}…");

        try
        {
            var har    = await HarParser.ParseAsync(path);
            var result = await Task.Run(() => HarAnalyzerService.Analyze(har, path));

            _currentResult   = result;
            _currentFilePath = path;

            _mru.Add(path);
            RebuildMruMenu();
            BuildTree(result);

            Title = $"HAR Analyzer — {result.FileName}";
            MenuReload.IsEnabled = true;
            SetStatus($"Loaded {result.FileName}  ·  {result.TotalEntries:N0} requests  ·  {result.TimeSpanCovered:mm\\:ss} duration");

            // Auto-select the Summary node
            if (MainTree.Items.Count > 0 &&
                MainTree.Items[0] is TreeViewItem root &&
                root.Items.Count > 0 &&
                root.Items[0] is TreeViewItem summaryNode)
            {
                summaryNode.IsSelected = true;
            }
        }
        catch (Exception ex)
        {
            SetStatus("Failed to load file.");
            MessageBox.Show($"Could not parse HAR file:\n\n{ex.Message}", "Parse Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── Tree building ──────────────────────────────────────────────────────

    private void BuildTree(AnalysisResult r)
    {
        MainTree.Items.Clear();
        var baseName = Path.GetFileNameWithoutExtension(r.FileName);

        var root = MakeItem($"📄 {r.FileName}",
            getHtml: () => HtmlBuilder.BuildSummary(r),
            getCsv:  () => CsvBuilder.BuildSummary(r),
            csvFileName: $"{baseName}-summary.csv",
            bold: true);
        root.IsExpanded = true;

        // Summary
        root.Items.Add(MakeItem("📊 Summary",
            getHtml: () => HtmlBuilder.BuildSummary(r),
            getCsv:  () => CsvBuilder.BuildSummary(r),
            csvFileName: $"{baseName}-summary.csv"));

        // Slowest
        var slowNode = MakeItem("🐌 Slowest Requests", null);
        slowNode.Items.Add(MakeItem("By Total Time (top 30)",
            getHtml: () => HtmlBuilder.BuildSlowestByTotal(r.Entries),
            getCsv:  () => CsvBuilder.BuildEntries(r.Entries.OrderByDescending(e => e.TotalMs).Take(30)),
            csvFileName: $"{baseName}-slowest-total.csv"));
        slowNode.Items.Add(MakeItem("By TTFB / Server Think Time (top 20)",
            getHtml: () => HtmlBuilder.BuildSlowestByTtfb(r.Entries),
            getCsv:  () => CsvBuilder.BuildEntries(r.Entries.OrderByDescending(e => e.TtfbMs).Take(20)),
            csvFileName: $"{baseName}-slowest-ttfb.csv"));
        slowNode.IsExpanded = true;
        root.Items.Add(slowNode);

        // Errors
        var errLabel = r.ErrorCount > 0 ? $"❌ Errors ({r.ErrorCount:N0})" : "✅ Errors (none)";
        var errEntries = r.Entries.Where(e => e.Status >= 400 || e.Status == 0).ToList();
        root.Items.Add(MakeItem(errLabel,
            getHtml: () => HtmlBuilder.BuildErrors(r.Entries),
            getCsv:  () => CsvBuilder.BuildEntries(errEntries.OrderBy(e => e.Status).ThenByDescending(e => e.TotalMs)),
            csvFileName: $"{baseName}-errors.csv"));

        // By Domain
        var domainNode = MakeItem("🌐 By Domain",
            getHtml: () => HtmlBuilder.BuildByDomain(r.Entries),
            getCsv:  () => CsvBuilder.BuildByDomain(r.Entries),
            csvFileName: $"{baseName}-by-domain.csv");

        var domains = r.Entries
            .GroupBy(e => e.Host)
            .OrderByDescending(g => g.Sum(e => e.TotalMs))
            .ToList();
        foreach (var g in domains)
        {
            var dom   = g.Key;
            var count = g.Count();
            var errs  = g.Count(e => e.Status >= 400 || e.Status == 0);
            var label = errs > 0 ? $"{dom}  ({count} req, {errs} err)" : $"{dom}  ({count})";
            // capture dom for the closures
            var domCopy = dom;
            domainNode.Items.Add(MakeItem(label,
                getHtml: () => HtmlBuilder.BuildDomainDetail(domCopy, r.Entries),
                getCsv:  () => CsvBuilder.BuildEntries(
                                   r.Entries.Where(e => e.Host == domCopy)
                                            .OrderByDescending(e => e.TotalMs)),
                csvFileName: $"{baseName}-{domCopy}.csv"));
        }
        domainNode.IsExpanded = false;
        root.Items.Add(domainNode);

        // Timeline
        root.Items.Add(MakeItem("📅 Timeline",
            getHtml: () => HtmlBuilder.BuildTimeline(r.Entries),
            getCsv:  () => CsvBuilder.BuildTimeline(r.Entries),
            csvFileName: $"{baseName}-timeline.csv"));

        // All Requests
        root.Items.Add(MakeItem($"📋 All Requests ({r.TotalEntries:N0})",
            getHtml: () => HtmlBuilder.BuildAllRequests(r.Entries),
            getCsv:  () => CsvBuilder.BuildEntries(r.Entries),
            csvFileName: $"{baseName}-all-requests.csv"));

        MainTree.Items.Add(root);
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

    private void MainTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item) return;
        if (item.Tag is not TreeNodeData { GetHtml: { } getHtml }) return;
        ContentBrowser.CoreWebView2?.NavigateToString(getHtml());
    }

    // ── Context menu — right-click selection + CSV export ─────────────────

    private void MainTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ensure the item under the cursor becomes selected before the menu opens
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item != null) item.IsSelected = true;
    }

    private void TreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var cm         = (ContextMenu)sender;
        var exportItem = (MenuItem)cm.Items[0];   // "Export to CSV…"
        var copyItem   = (MenuItem)cm.Items[1];   // "Copy as CSV"

        var hasCsv = MainTree.SelectedItem is TreeViewItem sel &&
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

        if (MainTree.SelectedItem is not TreeViewItem item) return false;
        if (item.Tag is not TreeNodeData { GetCsv: { } getCsv } data) return false;

        csv      = getCsv();
        fileName = data.CsvFileName;
        return true;
    }

    // ── Drag-and-drop .har files ──────────────────────────────────────────

    private void MainTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var har = files.FirstOrDefault(f => f.EndsWith(".har", StringComparison.OrdinalIgnoreCase));
        if (har != null) await LoadFileAsync(har);
    }

    // ── MRU menu ──────────────────────────────────────────────────────────

    private void RebuildMruMenu()
    {
        // Remove old dynamically-inserted MRU menu items
        var toRemove = FileMenu.Items.OfType<MenuItem>()
            .Where(m => m.Tag is string t && t == "mru")
            .ToList();
        foreach (var m in toRemove) FileMenu.Items.Remove(m);

        var items = _mru.Items;
        SepMru.Visibility       = items.Count > 0 ? Visibility.Visible  : Visibility.Collapsed;
        MenuClearMru.Visibility = items.Count > 0 ? Visibility.Visible  : Visibility.Collapsed;

        // Insert new MRU items just before SepMru
        var sepIdx = FileMenu.Items.IndexOf(SepMru);
        for (var i = 0; i < items.Count; i++)
        {
            var path = items[i];   // capture for lambda
            var mi   = new MenuItem
            {
                Header  = $"_{i + 1}  {TruncatePath(path, 60)}",
                ToolTip = path,
                Tag     = "mru",
            };
            mi.Click += async (_, _) => await LoadFileAsync(path);
            FileMenu.Items.Insert(sepIdx + i, mi);
        }
    }

    private void MenuClearMru_Click(object sender, RoutedEventArgs e)
    {
        _mru.Clear();
        RebuildMruMenu();
    }

    // ── View menu ──────────────────────────────────────────────────────────

    private void MenuExpandAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in MainTree.Items.OfType<TreeViewItem>())
            SetExpanded(item, true);
    }

    private void MenuCollapseAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in MainTree.Items.OfType<TreeViewItem>())
            SetExpanded(item, false);
    }

    private static void SetExpanded(TreeViewItem item, bool expanded)
    {
        item.IsExpanded = expanded;
        foreach (var child in item.Items.OfType<TreeViewItem>())
            SetExpanded(child, expanded);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SetBusy(bool busy, string? message = null)
    {
        LoadProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (message != null) SetStatus(message);
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private static string TruncatePath(string path, int max)
    {
        if (path.Length <= max) return path;
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length >= 2)
            return $"…\\{parts[^2]}\\{parts[^1]}";
        return "…" + path[^max..];
    }

    /// <summary>Walks up the WPF visual tree to find the nearest ancestor of type T.</summary>
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
