using System.Text.Json;
using System.Text.Json.Serialization;
using VersaConvert.Core.Models;

namespace VersaConvert.Core.Services;

public sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public UserPreferencesStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VersaConvert",
            "settings.json");
    }

    public UserPreferences Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new UserPreferences();
            var json = File.ReadAllText(_settingsPath);
            var preferences = JsonSerializer.Deserialize<UserPreferences>(json, SerializerOptions);
            return Normalize(preferences ?? new UserPreferences());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Percorso delle preferenze non valido.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Normalize(preferences), SerializerOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static UserPreferences Normalize(UserPreferences preferences) => preferences with
    {
        OutputDirectory = string.IsNullOrWhiteSpace(preferences.OutputDirectory)
            ? null
            : preferences.OutputDirectory.Trim(),
        Preset = Enum.IsDefined(preferences.Preset) ? preferences.Preset : ConversionPreset.Balanced,
        Quality = Math.Clamp(preferences.Quality, 1, 100),
        AudioBitrateKbps = Math.Clamp(preferences.AudioBitrateKbps, 64, 320),
        CollisionBehavior = Enum.IsDefined(preferences.CollisionBehavior)
            ? preferences.CollisionBehavior
            : CollisionBehavior.Rename
    };
}
