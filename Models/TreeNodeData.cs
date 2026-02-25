namespace HARAnalyzer.Models;

/// <summary>
/// Payload stored in every TreeViewItem.Tag.
/// Holds lazy generators for the HTML report and the CSV export.
/// </summary>
public sealed class TreeNodeData
{
    /// <summary>Returns a complete HTML document for the right-hand pane. Null = folder/group node.</summary>
    public Func<string>? GetHtml { get; init; }

    /// <summary>Returns CSV text ready to save or copy. Null = no export available for this node.</summary>
    public Func<string>? GetCsv { get; init; }

    /// <summary>Default filename suggested in the Save dialog.</summary>
    public string CsvFileName { get; init; } = "export.csv";
}
