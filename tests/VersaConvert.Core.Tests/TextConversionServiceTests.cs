using VersaConvert.Core.Services;

namespace VersaConvert.Core.Tests;

public sealed class TextConversionServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"versaconvert-text-{Guid.NewGuid():N}");
    private readonly TextConversionService _service = new();

    public TextConversionServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task MarkdownToHtmlCreatesStructuredDocument()
    {
        var input = Path.Combine(_directory, "readme.md");
        var output = Path.Combine(_directory, "readme.html");
        await File.WriteAllTextAsync(input, "# Titolo\n\nUn testo **importante**.");

        await _service.ConvertAsync(input, output, CancellationToken.None);

        var html = await File.ReadAllTextAsync(output);
        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h1>Titolo</h1>", html);
        Assert.Contains("<strong>importante</strong>", html);
    }

    [Fact]
    public async Task HtmlToTextRemovesMarkupAndDecodesEntities()
    {
        var input = Path.Combine(_directory, "page.html");
        var output = Path.Combine(_directory, "page.txt");
        await File.WriteAllTextAsync(input, "<h1>Titolo</h1><p>Caffè &amp; tè</p>");

        await _service.ConvertAsync(input, output, CancellationToken.None);

        var text = await File.ReadAllTextAsync(output);
        Assert.Contains("Titolo", text);
        Assert.Contains("Caffè & tè", text);
        Assert.DoesNotContain("<h1>", text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
