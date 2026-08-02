namespace VersaConvert.Core.Models;

public sealed record UserPreferences
{
    public string? OutputDirectory { get; init; }
    public ConversionPreset Preset { get; init; } = ConversionPreset.Balanced;
    public int Quality { get; init; } = 75;
    public int AudioBitrateKbps { get; init; } = 192;
    public CollisionBehavior CollisionBehavior { get; init; } = CollisionBehavior.Rename;
    public bool PreserveMetadata { get; init; } = true;
    public bool OpenOutputOnCompletion { get; init; }
}

public enum ConversionPreset
{
    Balanced,
    Maximum,
    Compact,
    Custom
}
