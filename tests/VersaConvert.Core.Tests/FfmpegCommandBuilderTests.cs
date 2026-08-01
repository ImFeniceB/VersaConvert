using VersaConvert.Core.Models;
using VersaConvert.Core.Services;

namespace VersaConvert.Core.Tests;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void Mp3ExtractionUsesAudioOnlyCodecAndSelectedBitrate()
    {
        var format = new ConversionFormat("mp3", "MP3", FormatFamily.Audio, string.Empty);
        var options = new ConversionOptions { AudioBitrateKbps = 256 };

        var arguments = FfmpegCommandBuilder.Build("video con spazi.mp4", "uscita.mp3", format, options);

        Assert.Contains("-vn", arguments);
        Assert.Contains("libmp3lame", arguments);
        Assert.Contains("256k", arguments);
        Assert.Contains("video con spazi.mp4", arguments);
        Assert.Equal("uscita.mp3", arguments[^1]);
    }

    [Fact]
    public void JpegConversionWritesExactlyOneFrame()
    {
        var format = new ConversionFormat("jpg", "JPEG", FormatFamily.Image, string.Empty);

        var arguments = FfmpegCommandBuilder.Build("input.png", "output.jpg", format, new ConversionOptions());

        var frameOptionIndex = arguments.IndexOf("-frames:v");
        Assert.True(frameOptionIndex >= 0);
        Assert.Equal("1", arguments[frameOptionIndex + 1]);
    }

    [Fact]
    public void MetadataCanBeRemoved()
    {
        var format = new ConversionFormat("wav", "WAV", FormatFamily.Audio, string.Empty);

        var arguments = FfmpegCommandBuilder.Build(
            "input.mp3",
            "output.wav",
            format,
            new ConversionOptions { PreserveMetadata = false });

        Assert.Contains("-map_metadata", arguments);
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> source, T value) where T : notnull
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(source[index], value)) return index;
        }

        return -1;
    }
}
