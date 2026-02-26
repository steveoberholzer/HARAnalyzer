using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HARAnalyzer.Services;

/// <summary>Writes (trimmed) HAR files while preserving all original JSON fields.</summary>
public static class HarWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Reads <paramref name="sourcePath"/>, removes every entry whose
    /// <c>startedDateTime</c> is before <paramref name="keepFrom"/>,
    /// removes pages that are no longer referenced, then writes the result
    /// to <paramref name="destPath"/>.
    /// </summary>
    /// <returns>Number of entries written.</returns>
    public static async Task<int> TrimAsync(string sourcePath, string destPath, DateTime keepFrom)
    {
        var json = await File.ReadAllTextAsync(sourcePath);
        var root = JsonNode.Parse(json)
            ?? throw new InvalidDataException("Could not parse HAR file.");

        var entries = root["log"]?["entries"]?.AsArray()
            ?? throw new InvalidDataException("HAR file has no entries array.");

        // Collect indices to remove (before cutoff); iterate reverse so indices stay valid
        var removeIndices = new List<int>();
        for (var i = 0; i < entries.Count; i++)
        {
            var dtStr = entries[i]?["startedDateTime"]?.GetValue<string>();
            if (dtStr is null) continue;
            if (DateTime.TryParse(dtStr, null, DateTimeStyles.RoundtripKind, out var dt) && dt < keepFrom)
                removeIndices.Add(i);
        }

        for (var i = removeIndices.Count - 1; i >= 0; i--)
            entries.RemoveAt(removeIndices[i]);

        // Remove pages whose id is no longer referenced by any kept entry
        var keptPagerefs = new HashSet<string>(
            entries.Select(e => e?["pageref"]?.GetValue<string>())
                   .Where(p => p is not null)!,
            StringComparer.Ordinal);

        if (keptPagerefs.Count > 0 && root["log"]?["pages"] is JsonArray pages)
        {
            var orphanIndices = new List<int>();
            for (var i = 0; i < pages.Count; i++)
            {
                var id = pages[i]?["id"]?.GetValue<string>();
                if (id is not null && !keptPagerefs.Contains(id))
                    orphanIndices.Add(i);
            }
            for (var i = orphanIndices.Count - 1; i >= 0; i--)
                pages.RemoveAt(orphanIndices[i]);
        }

        await File.WriteAllTextAsync(destPath, root.ToJsonString(WriteOptions));
        return entries.Count;
    }
}
