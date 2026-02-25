using System.IO;
using System.Text.Json;

namespace HARAnalyzer.Services;

public class MruService
{
    private const int MaxItems = 10;

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HARAnalyzer");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "mru.json");

    private List<string> _items = [];

    public IReadOnlyList<string> Items => _items.AsReadOnly();

    public void Load()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var json = File.ReadAllText(SettingsPath);
            _items = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            // Remove paths that no longer exist
            _items = [.. _items.Where(File.Exists)];
        }
        catch { _items = []; }
    }

    public void Add(string path)
    {
        _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, path);
        if (_items.Count > MaxItems)
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_items));
        }
        catch { /* non-critical */ }
    }
}
