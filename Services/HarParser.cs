using System.IO;
using System.Text.Json;
using HARAnalyzer.Models;

namespace HARAnalyzer.Services;

public static class HarParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static async Task<HarFile> ParseAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var result = await JsonSerializer.DeserializeAsync<HarFile>(stream, Options);
        return result ?? throw new InvalidDataException("File could not be parsed as a valid HAR archive.");
    }
}
