using VersaConvert.Core.Models;

namespace VersaConvert.Core.Services;

public sealed class FormatCatalog
{
    private static readonly HashSet<string> VideoInputs = CreateSet(
        "mp4", "mkv", "mov", "avi", "webm", "wmv", "flv", "m4v", "mpeg", "mpg", "3gp", "ts", "mts", "m2ts");

    private static readonly HashSet<string> AudioInputs = CreateSet(
        "mp3", "wav", "flac", "aac", "m4a", "ogg", "opus", "wma", "aiff", "aif", "alac", "ac3");

    private static readonly HashSet<string> ImageInputs = CreateSet(
        "png", "jpg", "jpeg", "webp", "bmp", "gif", "tiff", "tif", "ico", "avif", "heic");

    private static readonly HashSet<string> TextInputs = CreateSet("txt", "md", "markdown", "html", "htm");
    private static readonly HashSet<string> DocumentInputs = CreateSet("doc", "docx", "odt", "rtf");
    private static readonly HashSet<string> SpreadsheetInputs = CreateSet("xls", "xlsx", "ods", "csv");
    private static readonly HashSet<string> PresentationInputs = CreateSet("ppt", "pptx", "odp");

    private static readonly IReadOnlyList<ConversionFormat> VideoOutputs =
    [
        new("mp4", "MP4", FormatFamily.Video, "Video H.264 compatibile ovunque"),
        new("mkv", "Matroska", FormatFamily.Video, "Contenitore video flessibile"),
        new("webm", "WebM", FormatFamily.Video, "Video ottimizzato per il web"),
        new("mov", "QuickTime", FormatFamily.Video, "Video per flussi Apple e creativi"),
        new("avi", "AVI", FormatFamily.Video, "Formato video legacy"),
        new("gif", "GIF animata", FormatFamily.Image, "Anteprima animata senza audio"),
        new("mp3", "MP3", FormatFamily.Audio, "Audio universale e compatto"),
        new("wav", "WAV", FormatFamily.Audio, "Audio PCM non compresso"),
        new("flac", "FLAC", FormatFamily.Audio, "Audio lossless compresso"),
        new("m4a", "M4A", FormatFamily.Audio, "Audio AAC ad alta compatibilità"),
        new("ogg", "Ogg Vorbis", FormatFamily.Audio, "Audio aperto per il web"),
        new("opus", "Opus", FormatFamily.Audio, "Audio efficiente per voce e musica")
    ];

    private static readonly IReadOnlyList<ConversionFormat> AudioOutputs = VideoOutputs
        .Where(format => format.Family == FormatFamily.Audio)
        .Concat([new ConversionFormat("aac", "AAC", FormatFamily.Audio, "Flusso audio AAC")])
        .ToArray();

    private static readonly IReadOnlyList<ConversionFormat> ImageOutputs =
    [
        new("png", "PNG", FormatFamily.Image, "Immagine lossless con trasparenza"),
        new("jpg", "JPEG", FormatFamily.Image, "Foto compatta e universale"),
        new("webp", "WebP", FormatFamily.Image, "Immagine moderna per il web"),
        new("avif", "AVIF", FormatFamily.Image, "Immagine ad alta compressione"),
        new("bmp", "Bitmap", FormatFamily.Image, "Bitmap Windows non compressa"),
        new("tiff", "TIFF", FormatFamily.Image, "Immagine per stampa e archiviazione"),
        new("gif", "GIF", FormatFamily.Image, "Immagine con palette compatta")
    ];

    private static readonly IReadOnlyList<ConversionFormat> TextOutputs =
    [
        new("txt", "Testo semplice", FormatFamily.Text, "Solo testo, senza formattazione"),
        new("md", "Markdown", FormatFamily.Text, "Testo strutturato e portabile"),
        new("html", "HTML", FormatFamily.Text, "Pagina web autonoma")
    ];

    private static readonly IReadOnlyList<ConversionFormat> DocumentOutputs =
    [
        new("pdf", "PDF", FormatFamily.Document, "Documento pronto da condividere"),
        new("docx", "Word", FormatFamily.Document, "Documento Microsoft Word"),
        new("odt", "OpenDocument", FormatFamily.Document, "Documento OpenDocument"),
        new("rtf", "Rich Text", FormatFamily.Document, "Testo formattato interoperabile"),
        new("txt", "Testo semplice", FormatFamily.Text, "Solo contenuto testuale"),
        new("html", "HTML", FormatFamily.Text, "Documento come pagina web")
    ];

    private static readonly IReadOnlyList<ConversionFormat> SpreadsheetOutputs =
    [
        new("pdf", "PDF", FormatFamily.Document, "Foglio pronto da condividere"),
        new("xlsx", "Excel", FormatFamily.Spreadsheet, "Cartella Microsoft Excel"),
        new("ods", "OpenDocument Calc", FormatFamily.Spreadsheet, "Foglio OpenDocument"),
        new("csv", "CSV", FormatFamily.Spreadsheet, "Dati tabellari interoperabili")
    ];

    private static readonly IReadOnlyList<ConversionFormat> PresentationOutputs =
    [
        new("pdf", "PDF", FormatFamily.Document, "Presentazione pronta da condividere"),
        new("pptx", "PowerPoint", FormatFamily.Presentation, "Presentazione Microsoft PowerPoint"),
        new("odp", "OpenDocument Impress", FormatFamily.Presentation, "Presentazione OpenDocument")
    ];

    public bool CanRead(string path) => GetInputFamily(path) is not null;

    public FormatFamily? GetInputFamily(string path)
    {
        var extension = Normalize(Path.GetExtension(path));
        if (VideoInputs.Contains(extension)) return FormatFamily.Video;
        if (AudioInputs.Contains(extension)) return FormatFamily.Audio;
        if (ImageInputs.Contains(extension)) return FormatFamily.Image;
        if (TextInputs.Contains(extension)) return FormatFamily.Text;
        if (DocumentInputs.Contains(extension)) return FormatFamily.Document;
        if (SpreadsheetInputs.Contains(extension)) return FormatFamily.Spreadsheet;
        if (PresentationInputs.Contains(extension)) return FormatFamily.Presentation;
        return null;
    }

    public IReadOnlyList<ConversionFormat> GetCompatibleOutputs(string path)
    {
        return GetInputFamily(path) switch
        {
            FormatFamily.Video => VideoOutputs,
            FormatFamily.Audio => AudioOutputs,
            FormatFamily.Image => ImageOutputs,
            FormatFamily.Text => TextOutputs,
            FormatFamily.Document => DocumentOutputs,
            FormatFamily.Spreadsheet => SpreadsheetOutputs,
            FormatFamily.Presentation => PresentationOutputs,
            _ => []
        };
    }

    public IReadOnlyList<ConversionFormat> GetCommonOutputs(IEnumerable<string> paths)
    {
        var pathList = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (pathList.Length == 0)
        {
            return [];
        }

        var first = GetCompatibleOutputs(pathList[0]);
        var commonExtensions = first.Select(item => item.NormalizedExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in pathList.Skip(1))
        {
            commonExtensions.IntersectWith(GetCompatibleOutputs(path).Select(item => item.NormalizedExtension));
        }

        return first.Where(item => commonExtensions.Contains(item.NormalizedExtension)).ToArray();
    }

    public bool IsCompatible(string inputPath, ConversionFormat output) =>
        GetCompatibleOutputs(inputPath).Any(candidate =>
            candidate.NormalizedExtension.Equals(output.NormalizedExtension, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> CreateSet(params string[] values) =>
        values.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string extension) => extension.TrimStart('.').ToLowerInvariant();
}
