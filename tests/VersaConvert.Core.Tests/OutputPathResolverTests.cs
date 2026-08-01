using VersaConvert.Core.Models;
using VersaConvert.Core.Services;

namespace VersaConvert.Core.Tests;

public sealed class OutputPathResolverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"versaconvert-tests-{Guid.NewGuid():N}");

    public OutputPathResolverTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void RenameAddsNumberWhenTargetExists()
    {
        var source = Path.Combine(_directory, "clip.mp4");
        var existing = Path.Combine(_directory, "clip.mp3");
        File.WriteAllText(source, "source");
        File.WriteAllText(existing, "existing");

        var result = OutputPathResolver.Resolve(source, _directory, "mp3", CollisionBehavior.Rename);

        Assert.Equal(Path.Combine(_directory, "clip (2).mp3"), result);
    }

    [Fact]
    public void SameInputAndOutputGetsConvertedSuffix()
    {
        var source = Path.Combine(_directory, "photo.png");
        File.WriteAllText(source, "source");

        var result = OutputPathResolver.Resolve(source, _directory, "png", CollisionBehavior.Overwrite);

        Assert.Equal(Path.Combine(_directory, "photo_convertito.png"), result);
    }

    [Fact]
    public void SkipReturnsNullWhenTargetExists()
    {
        var source = Path.Combine(_directory, "audio.wav");
        var existing = Path.Combine(_directory, "audio.mp3");
        File.WriteAllText(source, "source");
        File.WriteAllText(existing, "existing");

        Assert.Null(OutputPathResolver.Resolve(source, _directory, "mp3", CollisionBehavior.Skip));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
