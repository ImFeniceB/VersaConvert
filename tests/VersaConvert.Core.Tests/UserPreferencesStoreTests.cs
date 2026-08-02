using VersaConvert.Core.Models;
using VersaConvert.Core.Services;

namespace VersaConvert.Core.Tests;

public sealed class UserPreferencesStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"versaconvert-preferences-{Guid.NewGuid():N}");

    [Fact]
    public void SaveAndLoad_RoundTripsPreferences()
    {
        var store = new UserPreferencesStore(Path.Combine(_testDirectory, "settings.json"));
        var expected = new UserPreferences
        {
            OutputDirectory = Path.Combine(_testDirectory, "output"),
            Preset = ConversionPreset.Custom,
            Quality = 84,
            AudioBitrateKbps = 256,
            CollisionBehavior = CollisionBehavior.Skip,
            PreserveMetadata = false,
            OpenOutputOnCompletion = true
        };

        store.Save(expected);
        var actual = store.Load();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Load_WithInvalidJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(_testDirectory);
        var settingsPath = Path.Combine(_testDirectory, "settings.json");
        File.WriteAllText(settingsPath, "not-json");

        var actual = new UserPreferencesStore(settingsPath).Load();

        Assert.Equal(new UserPreferences(), actual);
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        Directory.CreateDirectory(_testDirectory);
        var settingsPath = Path.Combine(_testDirectory, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "quality": 140,
              "audioBitrateKbps": 20,
              "preset": 99,
              "collisionBehavior": 99
            }
            """);

        var actual = new UserPreferencesStore(settingsPath).Load();

        Assert.Equal(100, actual.Quality);
        Assert.Equal(64, actual.AudioBitrateKbps);
        Assert.Equal(ConversionPreset.Balanced, actual.Preset);
        Assert.Equal(CollisionBehavior.Rename, actual.CollisionBehavior);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }
}
