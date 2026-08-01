using System.Globalization;
using VersaConvert.Core.Models;

namespace VersaConvert.Core.Services;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> Build(
        string inputPath,
        string outputPath,
        ConversionFormat format,
        ConversionOptions options)
    {
        var arguments = new List<string> { "-hide_banner", "-y", "-i", inputPath };
        if (!options.PreserveMetadata)
        {
            arguments.AddRange(["-map_metadata", "-1"]);
        }

        var quality = Math.Clamp(options.Quality, 1, 100);
        var crf = (int)Math.Round(35 - quality * 0.18, MidpointRounding.AwayFromZero);
        var imageQ = (int)Math.Round(31 - quality * 0.29, MidpointRounding.AwayFromZero);
        var bitrate = Math.Clamp(options.AudioBitrateKbps, 64, 320).ToString(CultureInfo.InvariantCulture) + "k";

        switch (format.NormalizedExtension)
        {
            case "mp4":
                arguments.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-c:a", "aac", "-b:a", bitrate, "-movflags", "+faststart"]);
                break;
            case "mkv":
                arguments.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-c:a", "aac", "-b:a", bitrate]);
                break;
            case "webm":
                arguments.AddRange(["-c:v", "libvpx-vp9", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-b:v", "0", "-c:a", "libopus", "-b:a", bitrate]);
                break;
            case "mov":
                arguments.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-c:a", "aac", "-b:a", bitrate, "-movflags", "+faststart"]);
                break;
            case "avi":
                arguments.AddRange(["-c:v", "mpeg4", "-q:v", Math.Max(2, imageQ / 2).ToString(CultureInfo.InvariantCulture), "-c:a", "libmp3lame", "-b:a", bitrate]);
                break;
            case "mp3":
                arguments.AddRange(["-vn", "-c:a", "libmp3lame", "-b:a", bitrate]);
                break;
            case "wav":
                arguments.AddRange(["-vn", "-c:a", "pcm_s16le"]);
                break;
            case "flac":
                arguments.AddRange(["-vn", "-c:a", "flac", "-compression_level", "8"]);
                break;
            case "m4a":
                arguments.AddRange(["-vn", "-c:a", "aac", "-b:a", bitrate]);
                break;
            case "aac":
                arguments.AddRange(["-vn", "-c:a", "aac", "-b:a", bitrate, "-f", "adts"]);
                break;
            case "ogg":
                arguments.AddRange(["-vn", "-c:a", "libvorbis", "-b:a", bitrate]);
                break;
            case "opus":
                arguments.AddRange(["-vn", "-c:a", "libopus", "-b:a", bitrate]);
                break;
            case "gif" when format.DisplayName.Contains("animata", StringComparison.OrdinalIgnoreCase):
                arguments.AddRange(["-vf", "fps=15,scale='min(1280,iw)':-2:flags=lanczos", "-loop", "0"]);
                break;
            case "jpg":
                arguments.AddRange(["-frames:v", "1", "-q:v", imageQ.ToString(CultureInfo.InvariantCulture)]);
                break;
            case "png":
                arguments.AddRange(["-frames:v", "1", "-compression_level", "6"]);
                break;
            case "webp":
                arguments.AddRange(["-frames:v", "1", "-c:v", "libwebp", "-q:v", quality.ToString(CultureInfo.InvariantCulture)]);
                break;
            case "avif":
                arguments.AddRange(["-frames:v", "1", "-c:v", "libsvtav1", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-preset", "8"]);
                break;
            case "bmp":
                arguments.AddRange(["-frames:v", "1", "-c:v", "bmp"]);
                break;
            case "tiff":
                arguments.AddRange(["-frames:v", "1", "-c:v", "tiff", "-compression_algo", "deflate"]);
                break;
            case "gif":
                arguments.AddRange(["-frames:v", "1"]);
                break;
            default:
                throw new NotSupportedException($"Il formato .{format.NormalizedExtension} non è gestito da FFmpeg.");
        }

        arguments.AddRange(["-progress", "pipe:2", "-nostats", outputPath]);
        return arguments;
    }
}
