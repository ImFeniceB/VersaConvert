using VersaConvert.Core.Models;

namespace VersaConvert.Core.Services;

public sealed class ConversionService
{
    private readonly FormatCatalog _catalog;
    private readonly ToolLocator _tools;
    private readonly ProcessRunner _processRunner;
    private readonly TextConversionService _textConverter;

    public ConversionService(
        FormatCatalog? catalog = null,
        ToolLocator? tools = null,
        ProcessRunner? processRunner = null,
        TextConversionService? textConverter = null)
    {
        _catalog = catalog ?? new FormatCatalog();
        _tools = tools ?? new ToolLocator();
        _processRunner = processRunner ?? new ProcessRunner();
        _textConverter = textConverter ?? new TextConversionService();
    }

    public ToolStatus GetToolStatus() => new(
        _tools.FindFfmpeg() is not null,
        _tools.FindLibreOffice() is not null,
        _tools.FindImageMagick() is not null);

    public async Task ConvertAsync(
        string inputPath,
        string outputPath,
        ConversionFormat format,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Il file sorgente non esiste.", inputPath);
        }

        if (!_catalog.IsCompatible(inputPath, format))
        {
            throw new NotSupportedException($".{format.NormalizedExtension} non è compatibile con {Path.GetExtension(inputPath)}.");
        }

        var inputFamily = _catalog.GetInputFamily(inputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Percorso di output non valido."));

        if (inputFamily is FormatFamily.Video or FormatFamily.Audio or FormatFamily.Image)
        {
            await ConvertWithFfmpegAsync(inputPath, outputPath, format, options, progress, cancellationToken);
            return;
        }

        if (inputFamily == FormatFamily.Text)
        {
            await _textConverter.ConvertAsync(inputPath, outputPath, cancellationToken);
            progress?.Report(100);
            return;
        }

        await ConvertWithLibreOfficeAsync(inputPath, outputPath, format, progress, cancellationToken);
    }

    private async Task ConvertWithFfmpegAsync(
        string inputPath,
        string outputPath,
        ConversionFormat format,
        ConversionOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var ffmpeg = _tools.FindFfmpeg() ?? throw new ToolMissingException(
            "FFmpeg non è disponibile. Usa la build completa di VersaConvert oppure installa FFmpeg e riavvia l’app.");
        try
        {
            var arguments = FfmpegCommandBuilder.Build(inputPath, outputPath, format, options);
            var result = await _processRunner.RunFfmpegAsync(ffmpeg, arguments, progress, cancellationToken);
            if (!result.Succeeded)
            {
                DeletePartialFile(outputPath);
                throw new ConversionFailedException(CreateDiagnosticMessage("FFmpeg", result.Diagnostics));
            }

            progress?.Report(100);
        }
        catch (OperationCanceledException)
        {
            DeletePartialFile(outputPath);
            throw;
        }
    }

    private async Task ConvertWithLibreOfficeAsync(
        string inputPath,
        string outputPath,
        ConversionFormat format,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var libreOffice = _tools.FindLibreOffice() ?? throw new ToolMissingException(
            "Per i documenti è necessario LibreOffice. Installalo gratuitamente e riavvia VersaConvert.");

        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        progress?.Report(15);
        var arguments = new[] { "--headless", "--convert-to", format.NormalizedExtension, "--outdir", outputDirectory, inputPath };
        var result = await _processRunner.RunAsync(libreOffice, arguments, cancellationToken);
        if (!result.Succeeded)
        {
            throw new ConversionFailedException(CreateDiagnosticMessage("LibreOffice", result.Diagnostics));
        }

        var generatedPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + "." + format.NormalizedExtension);
        if (!File.Exists(generatedPath))
        {
            throw new ConversionFailedException("LibreOffice non ha prodotto il file atteso.");
        }

        if (!generatedPath.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(generatedPath, outputPath, overwrite: true);
        }

        progress?.Report(100);
    }

    private static string CreateDiagnosticMessage(string tool, string diagnostics)
    {
        var meaningfulLines = diagnostics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            .TakeLast(4)
            .ToArray();
        return meaningfulLines.Length == 0
            ? $"{tool} non è riuscito a completare la conversione."
            : string.Join(Environment.NewLine, meaningfulLines);
    }

    private static void DeletePartialFile(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
        catch (IOException)
        {
        }
    }
}

public sealed record ToolStatus(bool FfmpegAvailable, bool LibreOfficeAvailable, bool ImageMagickAvailable);

public sealed class ToolMissingException(string message) : InvalidOperationException(message);
public sealed class ConversionFailedException(string message) : InvalidOperationException(message);
