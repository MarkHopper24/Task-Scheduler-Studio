using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tasker_App.Services;

/// <summary>
/// Persists the user's own saved scripts as JSON at <c>%LOCALAPPDATA%\WindowsTasker\script-library.json</c>
/// and exposes the combined library (read-only built-ins + the user's scripts). Built-ins live in code
/// (<see cref="ScriptLibrary"/>); only user scripts are stored here.
/// </summary>
public static class ScriptLibraryStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsTasker", "script-library.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A serialized user script (built-ins are not persisted; they have no <see cref="ScriptTemplate.Body"/>).</summary>
    private sealed class UserScriptDto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ScriptCategory Category { get; set; } = ScriptCategory.System;
        public string Glyph { get; set; } = "\uE756";
        public bool RequiresAdmin { get; set; }
        public bool Background { get; set; } = true;
        public string Body { get; set; } = string.Empty;
    }

    /// <summary>Loads the user's saved scripts (empty on first run or any read error).</summary>
    public static List<ScriptTemplate> LoadUser()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var dtos = JsonSerializer.Deserialize<List<UserScriptDto>>(File.ReadAllText(FilePath), JsonOpts) ?? new();
            return dtos.Select(FromDto).ToList();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>The full library shown to the user: built-in templates first, then the user's own.</summary>
    public static List<ScriptTemplate> All() => ScriptLibrary.BuiltIn.Concat(LoadUser()).ToList();

    /// <summary>Adds or replaces a user script (matched by Id) and persists. Returns the saved template.</summary>
    public static ScriptTemplate Save(ScriptTemplate script)
    {
        lock (_lock)
        {
            var users = LoadUser();
            var id = string.IsNullOrEmpty(script.Id) ? Guid.NewGuid().ToString("N") : script.Id;
            var saved = new ScriptTemplate
            {
                Id = id,
                Title = script.Title,
                Description = script.Description,
                Category = script.Category,
                Glyph = script.Glyph,
                RequiresAdmin = script.RequiresAdmin,
                Background = script.Background,
                Body = script.Body ?? string.Empty,
            };
            var idx = users.FindIndex(s => s.Id == id);
            if (idx >= 0) users[idx] = saved; else users.Add(saved);
            Persist(users);
            return saved;
        }
    }

    /// <summary>Deletes a user script by Id (built-in ids are ignored) and persists.</summary>
    public static void Delete(string id)
    {
        lock (_lock)
        {
            var users = LoadUser();
            if (users.RemoveAll(s => s.Id == id) > 0) Persist(users);
        }
    }

    /// <summary>True if the given id belongs to a user (deletable/editable) script.</summary>
    public static bool IsUserScript(string id) => LoadUser().Any(s => s.Id == id);

    // Serialises read-modify-write cycles so two rapid Save/Delete calls can't clobber each other.
    private static readonly object _lock = new();

    private static void Persist(IEnumerable<ScriptTemplate> users)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var dtos = users.Select(ToDto).ToList();
        var json = JsonSerializer.Serialize(dtos, JsonOpts);
        // Write-then-rename so a crash or full disk mid-write can't leave a truncated/partial
        // script-library.json (which LoadUser would silently treat as "no scripts", losing them all).
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static ScriptTemplate FromDto(UserScriptDto d) => new()
    {
        Id = d.Id,
        Title = d.Title,
        Description = d.Description,
        Category = d.Category,
        Glyph = string.IsNullOrEmpty(d.Glyph) ? "\uE756" : d.Glyph,
        RequiresAdmin = d.RequiresAdmin,
        Background = d.Background,
        Body = d.Body,
    };

    private static UserScriptDto ToDto(ScriptTemplate s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        Category = s.Category,
        Glyph = s.Glyph,
        RequiresAdmin = s.RequiresAdmin,
        Background = s.Background,
        Body = s.Body ?? string.Empty,
    };
}
