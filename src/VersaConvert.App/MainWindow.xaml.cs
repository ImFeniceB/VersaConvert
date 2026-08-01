using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VersaConvert.Core.Models;
using VersaConvert.Core.Services;

namespace VersaConvert.App;

public partial class MainWindow : Window
{
    private readonly FormatCatalog _catalog = new();
    private readonly ConversionService _conversionService = new();
    private CancellationTokenSource? _conversionCancellation;
    private bool _isConverting;
    private IReadOnlyList<ConversionFormat> _availableFormats = [];
    private IReadOnlyList<ConversionFormat> _visibleFormats = [];
    private ConversionFormat? _selectedFormat;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        OutputDirectoryTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VersaConvert");
        UpdateToolStatus();
        UpdateInterfaceState();
    }

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Scegli i file da convertire",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Tutti i file supportati|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.wmv;*.flv;*.m4v;*.mpeg;*.mpg;*.3gp;*.ts;*.mts;*.m2ts;*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.opus;*.wma;*.aiff;*.aif;*.alac;*.ac3;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff;*.tif;*.ico;*.avif;*.heic;*.txt;*.md;*.markdown;*.html;*.htm;*.doc;*.docx;*.odt;*.rtf;*.xls;*.xlsx;*.ods;*.csv;*.ppt;*.pptx;*.odp|Tutti i file|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var unsupported = new List<string>();
        var added = 0;
        foreach (var path in ExpandPaths(paths))
        {
            if (!File.Exists(path) || !_catalog.CanRead(path))
            {
                unsupported.Add(Path.GetFileName(path));
                continue;
            }

            if (Jobs.Any(job => job.InputPath.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Jobs.Add(new ConversionJob(path));
            added++;
        }

        RefreshFormats();
        UpdateInterfaceState();
        if (unsupported.Count > 0)
        {
            var preview = string.Join(Environment.NewLine, unsupported.Take(6));
            if (unsupported.Count > 6) preview += $"{Environment.NewLine}…e altri {unsupported.Count - 6}";
            MessageBox.Show(
                $"Questi file non hanno ancora un formato supportato:{Environment.NewLine}{Environment.NewLine}{preview}",
                "VersaConvert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else if (added > 0)
        {
            OverallStatusText.Text = added == 1 ? "File pronto per la conversione" : $"{added} file aggiunti alla coda";
        }
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).ToArray();
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var file in files) yield return file;
            }
            else
            {
                yield return path;
            }
        }
    }

    private void RefreshFormats()
    {
        var previousExtension = _selectedFormat?.NormalizedExtension;
        _availableFormats = _catalog.GetCommonOutputs(Jobs.Select(job => job.InputPath));
        SetSelectedFormat(_availableFormats.FirstOrDefault(format =>
            format.NormalizedExtension.Equals(previousExtension, StringComparison.OrdinalIgnoreCase)) ?? _availableFormats.FirstOrDefault());
        ApplyFormatFilter();

        if (Jobs.Count > 0 && _availableFormats.Count == 0)
        {
            OverallStatusText.Text = "I file selezionati non condividono un formato di uscita";
        }
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting || _selectedFormat is not ConversionFormat format)
        {
            return;
        }

        var outputDirectory = OutputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            MessageBox.Show("Scegli una cartella di destinazione.", "VersaConvert", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(exception.Message, "Cartella non disponibile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var options = ReadOptions();
        _isConverting = true;
        _conversionCancellation = new CancellationTokenSource();
        SetControlsForConversion(isConverting: true);

        var completed = 0;
        var failed = 0;
        var skipped = 0;
        var jobsToRun = Jobs.ToArray();
        foreach (var job in jobsToRun)
        {
            if (_conversionCancellation.IsCancellationRequested)
            {
                job.Status = JobStatus.Cancelled;
                job.Message = "Non avviato";
                continue;
            }

            var outputPath = OutputPathResolver.Resolve(job.InputPath, outputDirectory, format.NormalizedExtension, options.CollisionBehavior);
            if (outputPath is null)
            {
                job.Status = JobStatus.Skipped;
                job.Message = "File già esistente";
                skipped++;
                completed++;
                UpdateOverallProgress(completed, jobsToRun.Length, 0, job.FileName);
                continue;
            }

            job.OutputPath = outputPath;
            job.Status = JobStatus.Converting;
            job.Progress = 0;
            job.Message = $"Verso {format.DisplayName}";
            var progress = new Progress<double>(value =>
            {
                job.Progress = value;
                job.Message = value > 0 ? $"{value:0}%" : "Preparazione";
                UpdateOverallProgress(completed, jobsToRun.Length, value, job.FileName);
            });

            try
            {
                await _conversionService.ConvertAsync(
                    job.InputPath,
                    outputPath,
                    format,
                    options,
                    progress,
                    _conversionCancellation.Token);
                job.Status = JobStatus.Completed;
                job.Progress = 100;
                job.Message = Path.GetFileName(outputPath);
            }
            catch (OperationCanceledException)
            {
                job.Status = JobStatus.Cancelled;
                job.Message = "Annullato";
            }
            catch (Exception exception) when (exception is ToolMissingException or ConversionFailedException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                job.Status = JobStatus.Failed;
                job.Message = exception.Message;
                failed++;
            }

            completed++;
            UpdateOverallProgress(completed, jobsToRun.Length, 0, job.FileName);
        }

        var wasCancelled = _conversionCancellation.IsCancellationRequested;
        _conversionCancellation.Dispose();
        _conversionCancellation = null;
        _isConverting = false;
        SetControlsForConversion(isConverting: false);
        OverallProgressBar.Value = jobsToRun.Length == 0 ? 0 : 100;
        OverallPercentText.Text = jobsToRun.Length == 0 ? "0%" : "100%";

        if (wasCancelled)
        {
            OverallStatusText.Text = "Conversione annullata";
        }
        else if (failed > 0)
        {
            OverallStatusText.Text = $"Operazione conclusa: {failed} errori, {skipped} saltati";
        }
        else
        {
            OverallStatusText.Text = skipped > 0
                ? $"Conversione completata · {skipped} file saltati"
                : "Tutto fatto — i file sono pronti";
        }
    }

    private ConversionOptions ReadOptions()
    {
        var bitrateItem = (ComboBoxItem)BitrateComboBox.SelectedItem;
        var collisionItem = (ComboBoxItem)CollisionComboBox.SelectedItem;
        return new ConversionOptions
        {
            Quality = (int)Math.Round(QualitySlider.Value),
            AudioBitrateKbps = int.Parse(bitrateItem.Tag.ToString()!),
            PreserveMetadata = PreserveMetadataCheckBox.IsChecked == true,
            CollisionBehavior = Enum.Parse<CollisionBehavior>(collisionItem.Tag.ToString()!)
        };
    }

    private void UpdateOverallProgress(int completed, int total, double currentProgress, string fileName)
    {
        var percent = total == 0 ? 0 : Math.Clamp((completed + currentProgress / 100) / total * 100, 0, 100);
        OverallProgressBar.Value = percent;
        OverallPercentText.Text = $"{percent:0}%";
        OverallStatusText.Text = currentProgress > 0 ? $"Conversione di {fileName}" : $"{completed} di {total} completati";
    }

    private void SetControlsForConversion(bool isConverting)
    {
        AddFilesButton.IsEnabled = !isConverting;
        ClearButton.IsEnabled = !isConverting;
        FormatPickerButton.IsEnabled = !isConverting;
        OutputDirectoryTextBox.IsEnabled = !isConverting;
        QualitySlider.IsEnabled = !isConverting;
        BitrateComboBox.IsEnabled = !isConverting;
        CollisionComboBox.IsEnabled = !isConverting;
        PreserveMetadataCheckBox.IsEnabled = !isConverting;
        ConvertButton.Visibility = isConverting ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = isConverting ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = isConverting;
        UpdateInterfaceState();
    }

    private void UpdateInterfaceState()
    {
        EmptyState.Visibility = Jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileCountText.Text = Jobs.Count == 1 ? "1 FILE" : $"{Jobs.Count} FILE";
        ClearButton.IsEnabled = Jobs.Count > 0 && !_isConverting;
        FormatPickerButton.IsEnabled = Jobs.Count > 0 && _availableFormats.Count > 0 && !_isConverting;
        ConvertButton.IsEnabled = Jobs.Count > 0 && _selectedFormat is not null && !_isConverting &&
                                  !string.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text);
        OpenOutputButton.IsEnabled = !string.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text);
    }

    private void UpdateToolStatus()
    {
        var status = _conversionService.GetToolStatus();
        SetStatusText(FfmpegStatusText, status.FfmpegAvailable, status.FfmpegAvailable ? "Disponibile" : "Non trovato");
        SetStatusText(OfficeStatusText, status.LibreOfficeAvailable, status.LibreOfficeAvailable ? "Disponibile" : "Opzionale");
        OfficeStatusText.ToolTip = status.LibreOfficeAvailable
            ? "LibreOffice è pronto per convertire documenti, fogli e presentazioni."
            : "Installa LibreOffice per convertire documenti, fogli e presentazioni.";
    }

    private static void SetStatusText(TextBlock target, bool available, string text)
    {
        target.Text = available ? $"●  {text}" : $"○  {text}";
        target.Foreground = available
            ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SuccessBrush"]
            : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["MutedTextBrush"];
    }

    private void RemoveJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting || sender is not Button { Tag: ConversionJob job }) return;
        Jobs.Remove(job);
        RefreshFormats();
        UpdateInterfaceState();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting) return;
        Jobs.Clear();
        RefreshFormats();
        OverallProgressBar.Value = 0;
        OverallPercentText.Text = "0%";
        OverallStatusText.Text = "Aggiungi uno o più file per iniziare";
        UpdateInterfaceState();
    }

    private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Scegli dove salvare i file convertiti",
            Multiselect = false,
            InitialDirectory = Directory.Exists(OutputDirectoryTextBox.Text) ? OutputDirectoryTextBox.Text : null
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var directory = OutputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _conversionCancellation?.Cancel();
        OverallStatusText.Text = "Annullamento in corso…";
        CancelButton.IsEnabled = false;
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (_isConverting || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => _conversionCancellation?.Cancel();

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValueText is not null) QualityValueText.Text = $"{e.NewValue:0}%";
    }

    private void FormatPickerPopup_Opened(object? sender, EventArgs e)
    {
        FormatSearchTextBox.Clear();
        ApplyFormatFilter();
        FormatSearchTextBox.Focus();
        Keyboard.Focus(FormatSearchTextBox);
    }

    private void FormatSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FormatSearchPlaceholder is null) return;

        FormatSearchPlaceholder.Visibility = string.IsNullOrEmpty(FormatSearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFormatFilter();
    }

    private void FormatSearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            FormatPickerButton.IsChecked = false;
            FormatPickerButton.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _visibleFormats.FirstOrDefault() is { } firstFormat)
        {
            SetSelectedFormat(firstFormat);
            FormatPickerButton.IsChecked = false;
            FormatPickerButton.Focus();
            e.Handled = true;
        }
    }

    private void FormatOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConversionFormat format }) return;
        SetSelectedFormat(format);
        FormatPickerButton.IsChecked = false;
        FormatPickerButton.Focus();
    }

    private void SetSelectedFormat(ConversionFormat? format)
    {
        _selectedFormat = format;
        SelectedFormatExtensionText.Text = format?.DisplayExtension ?? "—";
        SelectedFormatNameText.Text = format?.DisplayName ?? "Nessun formato";
        FormatDescriptionText.Text = format?.Description ?? "Aggiungi un file per vedere i formati compatibili.";
        FormatPickerButton.ToolTip = format is null
            ? "Aggiungi un file per scegliere il formato di uscita."
            : $"Formato selezionato: {format.DisplayName} (.{format.NormalizedExtension})";
        UpdateInterfaceState();
    }

    private void ApplyFormatFilter()
    {
        if (FormatOptionsItemsControl is null || FormatSearchTextBox is null) return;

        var query = FormatSearchTextBox.Text.Trim();
        _visibleFormats = string.IsNullOrWhiteSpace(query)
            ? _availableFormats
            : _availableFormats.Where(format =>
                format.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                format.NormalizedExtension.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                format.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();

        FormatOptionsItemsControl.ItemsSource = _visibleFormats.Select(format =>
            new FormatPickerItem(
                format,
                format.NormalizedExtension.Equals(_selectedFormat?.NormalizedExtension, StringComparison.OrdinalIgnoreCase)));
        FormatResultCountText.Text = _visibleFormats.Count == 1 ? "1 FORMATO" : $"{_visibleFormats.Count} FORMATI";
        FormatEmptyText.Visibility = _visibleFormats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OutputDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateInterfaceState();

    private sealed record FormatPickerItem(ConversionFormat Format, bool IsSelected)
    {
        public string DisplayExtension => Format.DisplayExtension;
        public string Tooltip => $"{Format.DisplayName} — {Format.Description}";
    }
}
