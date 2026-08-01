namespace VersaConvert.Core.Models;

public sealed record ConversionFormat(
    string Extension,
    string DisplayName,
    FormatFamily Family,
    string Description)
{
    public string NormalizedExtension => Extension.TrimStart('.').ToLowerInvariant();
    public string DisplayExtension => NormalizedExtension.ToUpperInvariant();

    public override string ToString() => $"{DisplayName} (.{NormalizedExtension})";
}
