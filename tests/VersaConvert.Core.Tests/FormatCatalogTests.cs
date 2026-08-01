using VersaConvert.Core.Models;
using VersaConvert.Core.Services;

namespace VersaConvert.Core.Tests;

public sealed class FormatCatalogTests
{
    private readonly FormatCatalog _catalog = new();

    [Fact]
    public void VideoOffersAudioExtraction()
    {
        var extensions = _catalog.GetCompatibleOutputs("film.mp4")
            .Select(format => format.NormalizedExtension)
            .ToArray();

        Assert.Contains("mp3", extensions);
        Assert.Contains("wav", extensions);
        Assert.Contains("flac", extensions);
    }

    [Fact]
    public void VideoAndAudioShareOnlyAudioFormats()
    {
        var common = _catalog.GetCommonOutputs(["film.mp4", "intervista.wav"]);

        Assert.NotEmpty(common);
        Assert.All(common, format => Assert.Equal(FormatFamily.Audio, format.Family));
        Assert.Contains(common, format => format.NormalizedExtension == "mp3");
    }

    [Fact]
    public void ImageAndAudioHaveNoCommonOutput()
    {
        Assert.Empty(_catalog.GetCommonOutputs(["foto.png", "musica.mp3"]));
    }

    [Fact]
    public void OfficeDocumentsOfferPdf()
    {
        Assert.Contains(_catalog.GetCompatibleOutputs("relazione.docx"),
            format => format.NormalizedExtension == "pdf");
    }

    [Theory]
    [InlineData("archive.zip")]
    [InlineData("program.exe")]
    [InlineData("data.unknown")]
    public void ArbitraryBinaryFilesAreRejected(string path)
    {
        Assert.False(_catalog.CanRead(path));
    }
}
