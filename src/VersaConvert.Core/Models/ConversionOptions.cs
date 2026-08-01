namespace VersaConvert.Core.Models;

public sealed record ConversionOptions
{
    public int Quality { get; init; } = 75;
    public int AudioBitrateKbps { get; init; } = 192;
    public bool PreserveMetadata { get; init; } = true;
    public CollisionBehavior CollisionBehavior { get; init; } = CollisionBehavior.Rename;
}

public enum CollisionBehavior
{
    Rename,
    Overwrite,
    Skip
}
