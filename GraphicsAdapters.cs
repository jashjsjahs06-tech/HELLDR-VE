using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HELLDRIVE;

/// <summary>
/// Safe allow-list based INI adapter.
/// It only edits keys explicitly supplied by the game profile.
/// </summary>
public sealed class IniGraphicsAdapter : IGraphicsAdapter
{
    private readonly string _filePath;
    private readonly Dictionary<GraphicsLevel, Dictionary<string, string>> _levels;
    private readonly string _backupPath;

    public IniGraphicsAdapter(
        string filePath,
        Dictionary<GraphicsLevel, Dictionary<string, string>> levels)
    {
        _filePath = filePath;
        _levels = levels;
        _backupPath = filePath + ".helldrve.bak";
    }

    public bool ApplyLevel(GraphicsLevel level)
    {
        if (!File.Exists(_filePath) ||
            !_levels.TryGetValue(level, out var changes))
            return false;

        try
        {
            if (!File.Exists(_backupPath))
                File.Copy(_filePath, _backupPath);

            var lines = File.ReadAllLines(_filePath).ToList();

            foreach (var pair in changes)
                SetIniKey(lines, pair.Key, pair.Value);

            File.WriteAllLines(_filePath, lines);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void RestoreBackup()
    {
        if (!File.Exists(_backupPath))
            return;

        try
        {
            File.Copy(_backupPath, _filePath, overwrite: true);
            File.Delete(_backupPath);
        }
        catch
        {
            // Do not crash game shutdown because a backup was locked.
        }
    }

    private static void SetIniKey(
        List<string> lines,
        string key,
        string value)
    {
        // Supported syntax:
        // Key=Value
        // Section.Key=Value
        //
        // Section-aware arbitrary INI parsing varies heavily by game,
        // so this adapter intentionally uses an explicit key convention.
        string prefix = key + "=";

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = key + "=" + value;
                return;
            }
        }

        lines.Add(key + "=" + value);
    }
}

/// <summary>
/// JSON adapter for games whose graphics settings are exposed as JSON.
/// Paths use dot notation, e.g. "graphics.renderScale".
/// </summary>
public sealed class JsonGraphicsAdapter : IGraphicsAdapter
{
    private readonly string _filePath;
    private readonly Dictionary<GraphicsLevel, Dictionary<string, JsonElement>> _levels;
    private readonly string _backupPath;

    public JsonGraphicsAdapter(
        string filePath,
        Dictionary<GraphicsLevel, Dictionary<string, JsonElement>> levels)
    {
        _filePath = filePath;
        _levels = levels;
        _backupPath = filePath + ".helldrve.bak";
    }

    public bool ApplyLevel(GraphicsLevel level)
    {
        if (!File.Exists(_filePath) ||
            !_levels.TryGetValue(level, out var changes))
            return false;

        try
        {
            if (!File.Exists(_backupPath))
                File.Copy(_filePath, _backupPath);

            using var document =
                JsonDocument.Parse(File.ReadAllText(_filePath));

            var root = document.RootElement.Clone();
            var mutable = JsonNodeHelpers.ToMutable(root);

            foreach (var pair in changes)
                JsonNodeHelpers.SetPath(mutable, pair.Key, pair.Value);

            File.WriteAllText(
                _filePath,
                mutable.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void RestoreBackup()
    {
        if (!File.Exists(_backupPath))
            return;

        try
        {
            File.Copy(_backupPath, _filePath, overwrite: true);
            File.Delete(_backupPath);
        }
        catch { }
    }
}

internal static class JsonNodeHelpers
{
    public static MutableJson ToMutable(JsonElement element)
    {
        return MutableJson.Parse(element.GetRawText());
    }

    public static void SetPath(
        MutableJson root,
        string path,
        JsonElement value)
    {
        root.Set(path, value);
    }
}

/// <summary>
/// Small JSON DOM wrapper implemented without requiring a third-party package.
/// </summary>
internal sealed class MutableJson
{
    private JsonDocument _document;

    private MutableJson(JsonDocument document)
    {
        _document = document;
    }

    public static MutableJson Parse(string json) =>
        new(JsonDocument.Parse(json));

    public void Set(string path, JsonElement value)
    {
        // Rebuild the object tree through Dictionary/List primitives.
        object? obj = JsonSerializer.Deserialize<object>(
            _document.RootElement.GetRawText());

        var parts = path.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries);

        if (obj is not Dictionary<string, object?> root)
            return;

        Dictionary<string, object?> current = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var child) ||
                child is not Dictionary<string, object?> childDict)
            {
                childDict = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

                current[parts[i]] = childDict;
            }

            current = childDict;
        }

        current[parts[^1]] =
            JsonSerializer.Deserialize<object>(value.GetRawText());

        _document.Dispose();

        _document = JsonDocument.Parse(
            JsonSerializer.Serialize(root));
    }

    public string ToJsonString(JsonSerializerOptions options) =>
        JsonSerializer.Serialize(
            JsonSerializer.Deserialize<object>(
                _document.RootElement.GetRawText()),
            options);
}
