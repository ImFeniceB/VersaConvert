using System.Diagnostics;
using VersaConvert.Core.Services;

namespace VersaConvert.Core.Tests;

public sealed class FfmpegIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"versaconvert-media-{Guid.NewGuid():N}");

    public FfmpegIntegrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Mp4CanBeConvertedToMp3WhenFfmpegIsAvailable()
    {
        var repositoryFfmpeg = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "vendor",
            "ffmpeg.exe"));
        var toolLocator = File.Exists(repositoryFfmpeg)
            ? new ToolLocator(Path.GetDirectoryName(repositoryFfmpeg))
            : new ToolLocator();
        var ffmpeg = toolLocator.FindFfmpeg();
        if (ffmpeg is null)
        {
            return;
        }

        var input = Path.Combine(_directory, "sample.mp4");
        var output = Path.Combine(_directory, "sample.mp3");
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y",
                     "-f", "lavfi", "-i", "color=c=blue:s=320x180:d=1",
                     "-f", "lavfi", "-i", "sine=frequency=1000:duration=1",
                     "-shortest", "-c:v", "mpeg4", "-c:a", "aac", input
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using (var generator = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpeg non si è avviato."))
        {
            var error = await generator.StandardError.ReadToEndAsync();
            await generator.WaitForExitAsync();
            Assert.True(generator.ExitCode == 0, error);
        }

        var catalog = new FormatCatalog();
        var mp3 = catalog.GetCompatibleOutputs(input).Single(format => format.NormalizedExtension == "mp3");
        var service = new ConversionService(catalog, toolLocator);

        await service.ConvertAsync(input, output, mp3, new(), null, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 1_000);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
