using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HELLDRIVE;

public sealed class GameProfilePackage
{
    public string FormatVersion { get; set; } = "1.0";
    public string GameName { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Preset { get; set; } = "Balanced";
    public Dictionary<string, string?> Settings { get; set; } = new();
    public DateTime ExportedAt { get; set; } = DateTime.Now;
}

public static class ProfileExchange
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static void Export(GameProfilePackage profile, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(profile, Options));
    }

    public static GameProfilePackage Import(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Profile file not found.", path);

        var result = JsonSerializer.Deserialize<GameProfilePackage>(
            File.ReadAllText(path), Options);

        return result ?? throw new InvalidDataException("Invalid HELLDRIVE profile.");
    }
}