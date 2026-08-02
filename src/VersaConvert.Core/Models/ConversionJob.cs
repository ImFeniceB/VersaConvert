using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VersaConvert.Core.Models;

public sealed class ConversionJob : INotifyPropertyChanged
{
    private JobStatus _status = JobStatus.Ready;
    private double _progress;
    private string _message = "Pronto";
    private string? _outputPath;

    public ConversionJob(string inputPath)
    {
        InputPath = Path.GetFullPath(inputPath);
        var info = new FileInfo(InputPath);
        FileName = info.Name;
        SizeBytes = info.Exists ? info.Length : 0;
    }

    public string InputPath { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public string SizeDisplay => FileSizeFormatter.Format(SizeBytes);
    public string ExtensionDisplay => Path.GetExtension(InputPath).TrimStart('.').ToUpperInvariant();

    public string? OutputPath
    {
        get => _outputPath;
        set
        {
            if (SetField(ref _outputPath, value))
            {
                OnPropertyChanged(nameof(HasOutput));
            }
        }
    }

    public bool HasOutput => Status == JobStatus.Completed && !string.IsNullOrWhiteSpace(OutputPath);
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped;

    public JobStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(HasOutput));
                OnPropertyChanged(nameof(CanRetry));
            }
        }
    }

    public string StatusDisplay => Status switch
    {
        JobStatus.Ready => "Pronto",
        JobStatus.Converting => "Conversione",
        JobStatus.Completed => "Completato",
        JobStatus.Skipped => "Saltato",
        JobStatus.Cancelled => "Annullato",
        JobStatus.Failed => "Errore",
        _ => Status.ToString()
    };

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public enum JobStatus
{
    Ready,
    Converting,
    Completed,
    Skipped,
    Cancelled,
    Failed
}

internal static class FileSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {Units[unit]}";
    }
}
