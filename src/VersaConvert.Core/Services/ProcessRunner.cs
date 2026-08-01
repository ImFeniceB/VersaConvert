using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VersaConvert.Core.Services;

public sealed partial class ProcessRunner
{
    public async Task<ProcessResult> RunFfmpegAsync(
        string executable,
        IReadOnlyList<string> arguments,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(executable, arguments);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var diagnostics = new StringBuilder();
        TimeSpan? duration = null;

        if (!process.Start())
        {
            throw new InvalidOperationException("Impossibile avviare FFmpeg.");
        }

        using var registration = cancellationToken.Register(() => TryKill(process));
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            diagnostics.AppendLine(line);
            if (diagnostics.Length > 32_000)
            {
                diagnostics.Remove(0, diagnostics.Length - 24_000);
            }

            var durationMatch = DurationRegex().Match(line);
            if (durationMatch.Success && TimeSpan.TryParse(durationMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var parsedDuration))
            {
                duration = parsedDuration;
            }

            if (duration is { TotalMilliseconds: > 0 } && line.StartsWith("out_time=", StringComparison.Ordinal) &&
                TimeSpan.TryParse(line[9..], CultureInfo.InvariantCulture, out var current))
            {
                progress?.Report(Math.Clamp(current.TotalMilliseconds / duration.Value.TotalMilliseconds * 100, 0, 99));
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        _ = await standardOutputTask;
        return new ProcessResult(process.ExitCode, diagnostics.ToString());
    }

    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(executable, arguments);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Impossibile avviare {Path.GetFileName(executable)}.");
        }

        using var registration = cancellationToken.Register(() => TryKill(process));
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(process.ExitCode, (await outputTask) + Environment.NewLine + (await errorTask));
    }

    private static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    [GeneratedRegex(@"Duration:\s+(\d{2}:\d{2}:\d{2}\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex DurationRegex();
}

public sealed record ProcessResult(int ExitCode, string Diagnostics)
{
    public bool Succeeded => ExitCode == 0;
}
