using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace VersaConvert.Core.Services;

public sealed partial class TextConversionService
{
    public async Task ConvertAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var sourceExtension = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var targetExtension = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        var source = await File.ReadAllTextAsync(inputPath, cancellationToken);

        string result;
        if (targetExtension == "html")
        {
            result = sourceExtension is "md" or "markdown" ? MarkdownToHtml(source) : PlainTextToHtml(source);
        }
        else if (targetExtension == "txt")
        {
            result = sourceExtension is "html" or "htm" ? HtmlToText(source) : MarkdownToText(source);
        }
        else if (targetExtension == "md")
        {
            result = sourceExtension is "html" or "htm" ? HtmlToText(source) : source;
        }
        else
        {
            throw new NotSupportedException($"Conversione testuale verso .{targetExtension} non supportata.");
        }

        await File.WriteAllTextAsync(outputPath, result, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
    }

    internal static string PlainTextToHtml(string source)
    {
        var encoded = WebUtility.HtmlEncode(source);
        return "<!doctype html>\n<html lang=\"it\">\n<head>\n<meta charset=\"utf-8\">\n" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
               "<title>Documento convertito</title>\n</head>\n<body>\n<pre>" + encoded + "</pre>\n</body>\n</html>\n";
    }

    internal static string MarkdownToHtml(string source)
    {
        var encoded = WebUtility.HtmlEncode(source).Replace("\r\n", "\n", StringComparison.Ordinal);
        encoded = HeadingRegex().Replace(encoded, match =>
        {
            var level = match.Groups[1].Value.Length;
            return $"<h{level}>{match.Groups[2].Value}</h{level}>";
        });
        encoded = BoldRegex().Replace(encoded, "<strong>$1</strong>");
        encoded = ItalicRegex().Replace(encoded, "<em>$1</em>");
        encoded = LinkRegex().Replace(encoded, "<a href=\"$2\">$1</a>");

        var paragraphs = encoded.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.StartsWith('<') ? block : $"<p>{block.Replace("\n", "<br>\n", StringComparison.Ordinal)}</p>");

        return "<!doctype html>\n<html lang=\"it\">\n<head>\n<meta charset=\"utf-8\">\n" +
               "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
               "<title>Documento convertito</title>\n</head>\n<body>\n" +
               string.Join("\n", paragraphs) + "\n</body>\n</html>\n";
    }

    internal static string HtmlToText(string source)
    {
        var withBreaks = BreakRegex().Replace(source, "\n");
        var withoutTags = TagRegex().Replace(withBreaks, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim() + Environment.NewLine;
    }

    internal static string MarkdownToText(string source)
    {
        var text = LinkRegex().Replace(source, "$1 ($2)");
        text = MarkdownTokenRegex().Replace(text, string.Empty);
        return text;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*([^*]+)\*(?!\*)", RegexOptions.CultureInvariant)]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<(br|/p|/div|/h[1-6]|/li)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(^|\s)[#*_`>~-]+", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownTokenRegex();
}
