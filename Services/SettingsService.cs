using System.IO;
using System.Text.Json;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Persists application settings as a flat JSON dictionary in
/// <c>%AppData%\GHSMarkdownEditor\settings.json</c>. Values are stored as raw
/// <see cref="JsonElement"/> entries so heterogeneous types (strings, ints, enums, lists)
/// can coexist in a single file without a typed schema. All read/write failures are
/// swallowed silently so a corrupt or missing settings file never prevents the app from
/// starting — everything falls back to the caller-supplied default.
/// </summary>
public class SettingsService
{
    /// <summary>
    /// Full path to the settings file inside the user's roaming AppData folder.
    /// The directory is created on first save if it does not yet exist.
    /// </summary>
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GHSMarkdownEditor",
        "settings.json");

    private Dictionary<string, JsonElement> _store = new();

    /// <summary>Loads persisted settings immediately on construction.</summary>
    public SettingsService()
    {
        Load();
    }

    /// <summary>
    /// Returns the stored value for <paramref name="key"/> deserialised as <typeparamref name="T"/>,
    /// or <paramref name="defaultValue"/> if the key is absent or deserialisation fails.
    /// </summary>
    public T Get<T>(string key, T defaultValue)
    {
        if (_store.TryGetValue(key, out var element))
        {
            try { return element.Deserialize<T>()!; }
            catch { /* fall through to default */ }
        }
        return defaultValue;
    }

    /// <summary>
    /// Serialises <paramref name="value"/> and stores it under <paramref name="key"/>,
    /// then flushes the entire store to disk immediately so settings survive crashes.
    /// </summary>
    public void Set<T>(string key, T value)
    {
        _store[key] = JsonSerializer.SerializeToElement(value);
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _store = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            }
        }
        catch { _store = new(); }
    }

    /// <summary>
    /// Writes the full settings dictionary to disk as indented JSON.
    /// Failures are ignored — settings persistence is best-effort.
    /// </summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* best effort */ }
    }
}
