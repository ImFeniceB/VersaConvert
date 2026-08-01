namespace VersaConvert.Core.Services;

public sealed class ToolLocator
{
    private readonly string _baseDirectory;

    public ToolLocator(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
    }

    public string? FindFfmpeg() => FindExecutable("ffmpeg.exe");
    public string? FindLibreOffice() => FindExecutable("soffice.exe", GetLibreOfficeCandidates());
    public string? FindImageMagick() => FindExecutable("magick.exe", GetImageMagickCandidates());

    private string? FindExecutable(string fileName, IEnumerable<string>? extraCandidates = null)
    {
        var candidates = new List<string>
        {
            Path.Combine(_baseDirectory, fileName),
            Path.Combine(_baseDirectory, "tools", fileName),
            Path.Combine(_baseDirectory, "vendor", fileName)
        };

        if (extraCandidates is not null)
        {
            candidates.AddRange(extraCandidates);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim(), fileName)));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetLibreOfficeCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe");
        yield return Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.exe");
    }

    private static IEnumerable<string> GetImageMagickCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!Directory.Exists(programFiles))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(programFiles, "ImageMagick-*"))
        {
            yield return Path.Combine(directory, "magick.exe");
        }
    }
}
