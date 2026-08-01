using VersaConvert.Core.Models;

namespace VersaConvert.Core.Services;

public static class OutputPathResolver
{
    public static string? Resolve(
        string inputPath,
        string outputDirectory,
        string outputExtension,
        CollisionBehavior behavior)
    {
        Directory.CreateDirectory(outputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = outputExtension.TrimStart('.').ToLowerInvariant();
        var candidate = Path.Combine(outputDirectory, $"{baseName}.{extension}");

        if (Path.GetFullPath(candidate).Equals(Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase))
        {
            candidate = Path.Combine(outputDirectory, $"{baseName}_convertito.{extension}");
        }

        if (!File.Exists(candidate) || behavior == CollisionBehavior.Overwrite)
        {
            return candidate;
        }

        if (behavior == CollisionBehavior.Skip)
        {
            return null;
        }

        for (var index = 2; index < 10_000; index++)
        {
            var numbered = Path.Combine(outputDirectory, $"{baseName} ({index}).{extension}");
            if (!File.Exists(numbered))
            {
                return numbered;
            }
        }

        throw new IOException("Impossibile trovare un nome file libero nella cartella di destinazione.");
    }
}
