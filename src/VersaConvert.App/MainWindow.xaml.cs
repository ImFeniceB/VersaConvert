using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using VersaConvert.Core.Models;
using VersaConvert.Core.Services;

namespace VersaConvert.App;

public partial class MainWindow : Window
{
    private readonly FormatCatalog _catalog = new();
    private readonly ConversionService _conversionService = new();
    private readonly UserPreferencesStore _preferencesStore = new();
    private readonly Stopwatch _conversionStopwatch = new();
    private CancellationTokenSource? _conversionCancellation;
    private ToolStatus _toolStatus = new(false, false, false);
    private bool _isConverting;
    private bool _applyingPreset;
    private bool _loadingPreferences;
    private bool _suppressNextAddAnimation;
    private bool _animateCurrentBatch = true;
    private bool _isQueueMutationAnimating;
    private IReadOnlyList<ConversionFormat> _availableFormats = [];
    private IReadOnlyList<ConversionFormat> _visibleFormats = [];
    private IReadOnlyList<ConversionJob>? _requestedJobs;
    private ConversionFormat? _selectedFormat;
    private bool _animateNextFormatPickerOpen;
    private bool _isDropFeedbackHiding;
    private int _dropFeedbackAnimationVersion;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), handledEventsToo: true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(Window_PreviewMouseUp), handledEventsToo: true);
        LoadPreferences();
        UpdateToolStatus();
        UpdateInterfaceState();
    }

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var animateEntries = !_suppressNextAddAnimation && !LastInputWasKeyboard;
        var dialog = new OpenFileDialog
        {
            Title = "Scegli i file da convertire",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Tutti i file supportati|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.wmv;*.flv;*.m4v;*.mpeg;*.mpg;*.3gp;*.ts;*.mts;*.m2ts;*.mp3;*.wav;*.flac;*.aac;*.m4a;*.ogg;*.opus;*.wma;*.aiff;*.aif;*.alac;*.ac3;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tiff;*.tif;*.ico;*.avif;*.heic;*.txt;*.md;*.markdown;*.html;*.htm;*.doc;*.docx;*.odt;*.rtf;*.xls;*.xlsx;*.ods;*.csv;*.ppt;*.pptx;*.odp|Tutti i file|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddPaths(dialog.FileNames, animateEntries);
        }

        _suppressNextAddAnimation = false;
    }

    private void AddPaths(IEnumerable<string> paths, bool animateEntries = true)
    {
        var unsupported = new List<string>();
        var addedJobs = new List<ConversionJob>();
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

            var job = new ConversionJob(path);
            job.PropertyChanged += Job_PropertyChanged;
            Jobs.Add(job);
            addedJobs.Add(job);
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

        if (animateEntries && AnimationsEnabled)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                for (var index = 0; index < addedJobs.Count; index++)
                {
                    AnimateJobEntrance(addedJobs[index], Math.Min(index * 32, 224));
                }
            });
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

        var animateBatch = AnimationsEnabled && !LastInputWasKeyboard;

        var jobsToRun = _requestedJobs is { } requestedJobs ? requestedJobs.ToArray() : Jobs.ToArray();
        _requestedJobs = null;
        if (jobsToRun.Length == 0)
        {
            return;
        }

        if (GetToolIssue(jobsToRun) is { } toolIssue)
        {
            MessageBox.Show(toolIssue, "Motore non disponibile", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        var collisions = CountDirectCollisions(jobsToRun, outputDirectory, format.NormalizedExtension);
        if (options.CollisionBehavior == CollisionBehavior.Overwrite && collisions > 0)
        {
            var answer = MessageBox.Show(
                collisions == 1
                    ? "Un file esistente verrà sovrascritto. Vuoi continuare?"
                    : $"{collisions} file esistenti verranno sovrascritti. Vuoi continuare?",
                "Conferma sovrascrittura",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        PrepareJobsForRetry(jobsToRun.Where(job => job.CanRetry));

        _animateCurrentBatch = animateBatch;
        _isConverting = true;
        _conversionCancellation = new CancellationTokenSource();
        _conversionStopwatch.Restart();
        OverallEtaText.Text = "Calcolo tempo…";
        SetControlsForConversion(isConverting: true);

        var completed = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
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
                succeeded++;
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
        _conversionStopwatch.Stop();
        _conversionCancellation.Dispose();
        _conversionCancellation = null;
        _isConverting = false;
        SetControlsForConversion(isConverting: false);
        OverallProgressBar.Value = jobsToRun.Length == 0 ? 0 : 100;
        OverallPercentText.Text = jobsToRun.Length == 0 ? "0%" : "100%";
        OverallEtaText.Text = jobsToRun.Length == 0
            ? string.Empty
            : wasCancelled
                ? $"Interrotto dopo {FormatDuration(_conversionStopwatch.Elapsed)}"
                : $"Completato in {FormatDuration(_conversionStopwatch.Elapsed)}";

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

        if (_animateCurrentBatch) AnimateCompletionStatus();
        UpdateInterfaceState();
        SavePreferences();
        if (!wasCancelled && succeeded > 0 && OpenOutputOnCompletionCheckBox.IsChecked == true)
        {
            OpenOutputDirectory();
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
        if (percent >= 2 && percent < 100 && _conversionStopwatch.Elapsed.TotalSeconds >= 1)
        {
            var remainingSeconds = _conversionStopwatch.Elapsed.TotalSeconds * (100 - percent) / percent;
            OverallEtaText.Text = $"Circa {FormatDuration(TimeSpan.FromSeconds(remainingSeconds))} rimanenti";
        }
        else if (percent >= 100)
        {
            OverallEtaText.Text = "Finalizzazione…";
        }
    }

    private void SetControlsForConversion(bool isConverting)
    {
        AddFilesButton.IsEnabled = !isConverting;
        ClearButton.IsEnabled = !isConverting;
        JobsList.IsEnabled = !isConverting;
        FormatPickerButton.IsEnabled = !isConverting;
        PresetComboBox.IsEnabled = !isConverting;
        OutputDirectoryTextBox.IsEnabled = !isConverting;
        QualitySlider.IsEnabled = !isConverting;
        BitrateComboBox.IsEnabled = !isConverting;
        CollisionComboBox.IsEnabled = !isConverting;
        PreserveMetadataCheckBox.IsEnabled = !isConverting;
        OpenOutputOnCompletionCheckBox.IsEnabled = !isConverting;
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
        var hasCompletedJobs = Jobs.Any(job => job.Status is JobStatus.Completed or JobStatus.Skipped);
        ClearCompletedButton.Visibility = hasCompletedJobs && !_isConverting ? Visibility.Visible : Visibility.Collapsed;
        ClearCompletedButton.IsEnabled = !_isConverting && !_isQueueMutationAnimating;
        FormatPickerButton.IsEnabled = Jobs.Count > 0 && _availableFormats.Count > 0 && !_isConverting;
        var toolIssue = GetToolIssue(Jobs);
        ConvertButton.IsEnabled = Jobs.Count > 0 && _selectedFormat is not null && !_isConverting &&
                                  !string.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text) && toolIssue is null;
        OpenOutputButton.IsEnabled = !string.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text);
        RetryFailedButton.Visibility = !_isConverting && Jobs.Any(job => job.CanRetry)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateContextualSettings();
        UpdatePreflight(toolIssue);
    }

    private void UpdateToolStatus()
    {
        _toolStatus = _conversionService.GetToolStatus();
        SetStatusText(FfmpegStatusText, _toolStatus.FfmpegAvailable, _toolStatus.FfmpegAvailable ? "Disponibile" : "Non trovato");
        SetStatusText(OfficeStatusText, _toolStatus.LibreOfficeAvailable, _toolStatus.LibreOfficeAvailable ? "Disponibile" : "Opzionale");
        OfficeStatusText.ToolTip = _toolStatus.LibreOfficeAvailable
            ? "LibreOffice è pronto per convertire documenti, fogli e presentazioni."
            : "Installa LibreOffice per convertire documenti, fogli e presentazioni.";
    }

    private static void SetStatusText(TextBlock target, bool available, string text)
    {
        target.Text = text;
        target.Foreground = available
            ? (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SuccessBrush"]
            : (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["MutedTextBrush"];
    }

    private async void RemoveJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting || sender is not Button { Tag: ConversionJob job }) return;
        if (AnimationsEnabled && !LastInputWasKeyboard) await AnimateJobExitAsync(job);
        if (!Jobs.Contains(job)) return;
        job.PropertyChanged -= Job_PropertyChanged;
        Jobs.Remove(job);
        RefreshFormats();
        UpdateInterfaceState();
    }

    private void OpenJobOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConversionJob job } || string.IsNullOrWhiteSpace(job.OutputPath)) return;
        if (!File.Exists(job.OutputPath))
        {
            job.Message = "Il file convertito non è più disponibile";
            job.OutputPath = null;
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{job.OutputPath}\"") { UseShellExecute = true });
    }

    private void RetryJobButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting || sender is not Button { Tag: ConversionJob job }) return;
        _requestedJobs = [job];
        ConvertButton_Click(ConvertButton, new RoutedEventArgs(Button.ClickEvent));
    }

    private void RetryFailedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting) return;
        var retryable = Jobs.Where(job => job.CanRetry).ToArray();
        if (retryable.Length == 0) return;
        _requestedJobs = retryable;
        ConvertButton_Click(ConvertButton, new RoutedEventArgs(Button.ClickEvent));
    }

    private static void PrepareJobsForRetry(IEnumerable<ConversionJob> jobs)
    {
        foreach (var job in jobs)
        {
            job.OutputPath = null;
            job.Status = JobStatus.Ready;
            job.Progress = 0;
            job.Message = "Pronto per un nuovo tentativo";
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting) return;
        foreach (var job in Jobs) job.PropertyChanged -= Job_PropertyChanged;
        Jobs.Clear();
        RefreshFormats();
        OverallProgressBar.Value = 0;
        OverallPercentText.Text = "0%";
        OverallEtaText.Text = string.Empty;
        OverallStatusText.Text = "Aggiungi uno o più file per iniziare";
        UpdateInterfaceState();
    }

    private async void ClearCompletedButton_Click(object sender, RoutedEventArgs e) =>
        await RemoveCompletedJobsAsync(animate: AnimationsEnabled && !LastInputWasKeyboard);

    private async Task RemoveCompletedJobsAsync(bool animate)
    {
        if (_isConverting || _isQueueMutationAnimating) return;
        var completedJobs = Jobs
            .Where(job => job.Status is JobStatus.Completed or JobStatus.Skipped)
            .ToArray();
        if (completedJobs.Length == 0) return;

        _isQueueMutationAnimating = true;
        UpdateInterfaceState();
        try
        {
            if (animate)
            {
                await Task.WhenAll(completedJobs.Select(AnimateJobExitAsync));
            }

            foreach (var job in completedJobs)
            {
                job.PropertyChanged -= Job_PropertyChanged;
                Jobs.Remove(job);
            }

            RefreshFormats();
            OverallStatusText.Text = completedJobs.Length == 1
                ? "1 file completato rimosso dalla coda"
                : $"{completedJobs.Length} file completati rimossi dalla coda";
            OverallEtaText.Text = string.Empty;
        }
        finally
        {
            _isQueueMutationAnimating = false;
            UpdateInterfaceState();
        }
    }

    private void MoveJob(ConversionJob job, int offset)
    {
        var currentIndex = Jobs.IndexOf(job);
        if (currentIndex < 0 || Jobs.Count < 2) return;
        var targetIndex = Math.Clamp(currentIndex + offset, 0, Jobs.Count - 1);
        if (currentIndex == targetIndex) return;
        Jobs.Move(currentIndex, targetIndex);
        JobsList.SelectedItem = job;
        JobsList.ScrollIntoView(job);
        OverallStatusText.Text = $"{job.FileName} spostato in posizione {targetIndex + 1}";
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
        => OpenOutputDirectory();

    private void OpenOutputDirectory()
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
        var canAccept = !_isConverting && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        if (canAccept) ShowDropFeedback();
        else HideDropFeedback();
        e.Handled = true;
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e) => Window_DragEnter(sender, e);

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        HideDropFeedback();
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        HideDropFeedback();
        if (_isConverting || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var shortcutKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (shortcutKey == Key.Delete && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !_isConverting)
        {
            _ = RemoveCompletedJobsAsync(animate: false);
            e.Handled = true;
            return;
        }

        if (shortcutKey == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !_isConverting)
        {
            _suppressNextAddAnimation = true;
            AddFilesButton_Click(AddFilesButton, new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        if (shortcutKey == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ConvertButton.IsEnabled)
        {
            ConvertButton_Click(ConvertButton, new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && shortcutKey is Key.Up or Key.Down &&
            !_isConverting && JobsList.SelectedItem is ConversionJob jobToMove)
        {
            MoveJob(jobToMove, shortcutKey == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (shortcutKey == Key.Delete && !_isConverting && Keyboard.FocusedElement is not TextBoxBase &&
            JobsList.SelectedItem is ConversionJob selectedJob)
        {
            selectedJob.PropertyChanged -= Job_PropertyChanged;
            Jobs.Remove(selectedJob);
            RefreshFormats();
            UpdateInterfaceState();
            e.Handled = true;
            return;
        }

        if (shortcutKey != Key.Escape) return;
        if (FormatPickerPopup.IsOpen)
        {
            FormatPickerButton.IsChecked = false;
            FormatPickerButton.Focus();
            e.Handled = true;
        }
        else if (_isConverting)
        {
            CancelButton_Click(CancelButton, new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _conversionCancellation?.Cancel();
        SavePreferences();
        foreach (var job in Jobs) job.PropertyChanged -= Job_PropertyChanged;
    }

    private void LoadPreferences()
    {
        _loadingPreferences = true;
        try
        {
            var preferences = _preferencesStore.Load();
            OutputDirectoryTextBox.Text = preferences.OutputDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "VersaConvert");
            ApplyPreset(preferences.Preset);
            if (preferences.Preset == ConversionPreset.Custom)
            {
                QualitySlider.Value = preferences.Quality;
                SelectBitrate(preferences.AudioBitrateKbps);
            }

            CollisionComboBox.SelectedItem = CollisionComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == preferences.CollisionBehavior.ToString());
            PreserveMetadataCheckBox.IsChecked = preferences.PreserveMetadata;
            OpenOutputOnCompletionCheckBox.IsChecked = preferences.OpenOutputOnCompletion;
        }
        finally
        {
            _loadingPreferences = false;
        }
    }

    private void SavePreferences()
    {
        if (_loadingPreferences || PresetComboBox is null) return;
        var preset = PresetComboBox.SelectedItem is ComboBoxItem { Tag: string presetTag } &&
                     Enum.TryParse<ConversionPreset>(presetTag, out var parsedPreset)
            ? parsedPreset
            : ConversionPreset.Custom;
        try
        {
            _preferencesStore.Save(new UserPreferences
            {
                OutputDirectory = OutputDirectoryTextBox.Text,
                Preset = preset,
                Quality = (int)Math.Round(QualitySlider.Value),
                AudioBitrateKbps = BitrateComboBox.SelectedItem is ComboBoxItem bitrateItem
                    ? int.Parse(bitrateItem.Tag!.ToString()!)
                    : 192,
                CollisionBehavior = GetSelectedCollisionBehavior(),
                PreserveMetadata = PreserveMetadataCheckBox.IsChecked == true,
                OpenOutputOnCompletion = OpenOutputOnCompletionCheckBox.IsChecked == true
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Impossibile salvare le preferenze: {exception.Message}");
        }
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValueText is not null) QualityValueText.Text = $"{e.NewValue:0}%";
        MarkPresetAsCustom();
        UpdatePreflight();
    }

    private void BitrateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MarkPresetAsCustom();
        UpdatePreflight();
    }

    private void CollisionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreflight();

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingPreset || PresetComboBox is null || QualitySlider is null || BitrateComboBox is null) return;
        if (PresetComboBox.SelectedItem is not ComboBoxItem { Tag: string presetTag } ||
            !Enum.TryParse<ConversionPreset>(presetTag, out var preset)) return;

        ApplyPreset(preset);
        UpdatePreflight();
    }

    private void ApplyPreset(ConversionPreset preset)
    {
        if (PresetComboBox is null || QualitySlider is null || BitrateComboBox is null) return;
        _applyingPreset = true;
        try
        {
            PresetComboBox.SelectedIndex = (int)preset;
            switch (preset)
            {
                case ConversionPreset.Balanced:
                    QualitySlider.Value = 75;
                    SelectBitrate(192);
                    PresetDescriptionText.Text = "Un buon equilibrio tra qualità e dimensione.";
                    break;
                case ConversionPreset.Maximum:
                    QualitySlider.Value = 92;
                    SelectBitrate(320);
                    PresetDescriptionText.Text = "Più dettaglio, con file più grandi e conversioni più lunghe.";
                    break;
                case ConversionPreset.Compact:
                    QualitySlider.Value = 55;
                    SelectBitrate(128);
                    PresetDescriptionText.Text = "Riduce lo spazio occupato per condivisioni rapide.";
                    break;
                case ConversionPreset.Custom:
                    PresetDescriptionText.Text = "Le impostazioni sono state regolate manualmente.";
                    break;
            }
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    private void SelectBitrate(int bitrate)
    {
        var item = BitrateComboBox.Items.OfType<ComboBoxItem>()
            .MinBy(candidate => Math.Abs(int.Parse(candidate.Tag!.ToString()!) - bitrate));
        BitrateComboBox.SelectedItem = item;
    }

    private void MarkPresetAsCustom()
    {
        if (_applyingPreset || _loadingPreferences || PresetComboBox is null || PresetDescriptionText is null || PresetComboBox.SelectedIndex == (int)ConversionPreset.Custom) return;
        _applyingPreset = true;
        PresetComboBox.SelectedIndex = (int)ConversionPreset.Custom;
        PresetDescriptionText.Text = "Le impostazioni sono state regolate manualmente.";
        _applyingPreset = false;
    }

    private void UpdateContextualSettings()
    {
        if (QualitySettingsPanel is null || BitrateSettingsPanel is null || PresetSettingsPanel is null) return;
        var extension = _selectedFormat?.NormalizedExtension;
        var qualityRelevant = _selectedFormat?.Family == FormatFamily.Video ||
                              extension is "jpg" or "webp" or "avif";
        var bitrateRelevant = _selectedFormat?.Family == FormatFamily.Video ||
                              extension is "mp3" or "m4a" or "aac" or "ogg" or "opus";

        QualitySettingsPanel.Visibility = qualityRelevant ? Visibility.Visible : Visibility.Collapsed;
        BitrateSettingsPanel.Visibility = bitrateRelevant ? Visibility.Visible : Visibility.Collapsed;
        SetContextPanelVisibility(PresetSettingsPanel, qualityRelevant || bitrateRelevant);
    }

    private static void SetContextPanelVisibility(FrameworkElement panel, bool visible)
    {
        if (!visible)
        {
            panel.Visibility = Visibility.Collapsed;
            return;
        }

        var wasCollapsed = panel.Visibility != Visibility.Visible;
        panel.Visibility = Visibility.Visible;
        if (!wasCollapsed || !AnimationsEnabled || LastInputWasKeyboard) return;

        var translate = GetOrCreateTranslateTransform(panel);
        panel.Opacity = 1;
        translate.Y = 0;
        panel.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.45, 1, 170, FillBehavior.Stop));
        translate.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(5, 0, 170, FillBehavior.Stop));
    }

    private void UpdatePreflight(string? knownToolIssue = null)
    {
        if (PreflightBorder is null || ConvertButton is null) return;

        knownToolIssue ??= GetToolIssue(Jobs);
        PreflightWarningText.Visibility = Visibility.Collapsed;
        PreflightWarningText.Foreground = (Brush)Application.Current.Resources["DangerBrush"];
        PreflightBorder.Background = new SolidColorBrush(Color.FromRgb(24, 37, 58));

        if (Jobs.Count == 0)
        {
            PreflightTitleText.Text = "In attesa dei file";
            PreflightSummaryText.Text = "Aggiungi almeno un file per preparare la conversione.";
            ConvertButton.ToolTip = "Aggiungi almeno un file.";
            return;
        }

        if (_selectedFormat is null)
        {
            PreflightTitleText.Text = "Formato comune non disponibile";
            PreflightSummaryText.Text = "I file in coda non condividono ancora un formato di uscita.";
            PreflightWarningText.Text = "Rimuovi i file incompatibili o crea una coda separata.";
            PreflightWarningText.Visibility = Visibility.Visible;
            PreflightBorder.Background = new SolidColorBrush(Color.FromRgb(57, 35, 50));
            ConvertButton.ToolTip = "Nessun formato di uscita è compatibile con tutti i file.";
            return;
        }

        var outputDirectory = OutputDirectoryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            PreflightTitleText.Text = "Scegli una destinazione";
            PreflightSummaryText.Text = "Indica dove salvare i file convertiti.";
            ConvertButton.ToolTip = "Scegli una cartella di destinazione.";
            return;
        }

        var destinationName = GetDestinationName(outputDirectory);
        var presetName = PresetSettingsPanel.Visibility == Visibility.Visible &&
                         PresetComboBox.SelectedItem is ComboBoxItem presetItem
            ? presetItem.Content?.ToString()
            : "Impostazioni compatibili";
        PreflightTitleText.Text = Jobs.Count == 1
            ? $"1 file → {_selectedFormat.DisplayExtension}"
            : $"{Jobs.Count} file → {_selectedFormat.DisplayExtension}";
        PreflightSummaryText.Text = $"Destinazione: {destinationName} · {presetName}";

        if (knownToolIssue is not null)
        {
            PreflightWarningText.Text = knownToolIssue;
            PreflightWarningText.Visibility = Visibility.Visible;
            PreflightBorder.Background = new SolidColorBrush(Color.FromRgb(57, 35, 50));
            ConvertButton.ToolTip = knownToolIssue;
            return;
        }

        var collisions = CountDirectCollisions(Jobs, outputDirectory, _selectedFormat.NormalizedExtension);
        if (collisions > 0)
        {
            var behavior = GetSelectedCollisionBehavior();
            PreflightWarningText.Text = behavior switch
            {
                CollisionBehavior.Overwrite => collisions == 1
                    ? "1 file esistente richiederà conferma prima della sovrascrittura."
                    : $"{collisions} file esistenti richiederanno conferma prima della sovrascrittura.",
                CollisionBehavior.Skip => collisions == 1
                    ? "1 file esistente verrà saltato."
                    : $"{collisions} file esistenti verranno saltati.",
                _ => collisions == 1
                    ? "1 file esistente riceverà un nuovo nome."
                    : $"{collisions} file esistenti riceveranno un nuovo nome."
            };
            PreflightWarningText.Foreground = behavior == CollisionBehavior.Overwrite
                ? (Brush)Application.Current.Resources["DangerBrush"]
                : (Brush)Application.Current.Resources["MutedTextBrush"];
            PreflightWarningText.Visibility = Visibility.Visible;
            if (behavior == CollisionBehavior.Overwrite)
            {
                PreflightBorder.Background = new SolidColorBrush(Color.FromRgb(57, 35, 50));
            }
        }

        if (collisions == 0 || GetSelectedCollisionBehavior() != CollisionBehavior.Overwrite)
        {
            PreflightBorder.Background = new SolidColorBrush(Color.FromRgb(20, 46, 50));
        }
        ConvertButton.ToolTip = "Avvia la conversione (Ctrl+Invio)";
    }

    private string? GetToolIssue(IEnumerable<ConversionJob> jobs)
    {
        var families = jobs.Select(job => _catalog.GetInputFamily(job.InputPath)).ToHashSet();
        if (families.Overlaps([FormatFamily.Video, FormatFamily.Audio, FormatFamily.Image]) && !_toolStatus.FfmpegAvailable)
        {
            return "FFmpeg non è disponibile. Usa la build completa oppure installa FFmpeg e riavvia l'app.";
        }

        if (families.Overlaps([FormatFamily.Document, FormatFamily.Spreadsheet, FormatFamily.Presentation]) && !_toolStatus.LibreOfficeAvailable)
        {
            return "LibreOffice è necessario per i documenti. Installalo e riavvia l'app.";
        }

        return null;
    }

    private static int CountDirectCollisions(IEnumerable<ConversionJob> jobs, string outputDirectory, string extension)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory)) return 0;
        return jobs.Count(job => File.Exists(Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(job.InputPath) + "." + extension)));
    }

    private CollisionBehavior GetSelectedCollisionBehavior() =>
        CollisionComboBox.SelectedItem is ComboBoxItem collisionItem &&
        Enum.TryParse<CollisionBehavior>(collisionItem.Tag?.ToString(), out var behavior)
            ? behavior
            : CollisionBehavior.Rename;

    private static string GetDestinationName(string outputDirectory)
    {
        try
        {
            var trimmed = outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : outputDirectory;
        }
        catch (ArgumentException)
        {
            return outputDirectory;
        }
    }

    private void FormatPickerPopup_Opened(object? sender, EventArgs e)
    {
        if (_animateNextFormatPickerOpen && AnimationsEnabled)
        {
            AnimateFormatPickerOpen();
        }
        else
        {
            ResetFormatPickerMotion();
        }

        _animateNextFormatPickerOpen = false;
        FormatSearchTextBox.Clear();
        ApplyFormatFilter();
        FormatSearchTextBox.Focus();
        Keyboard.Focus(FormatSearchTextBox);
    }

    private void FormatPickerButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _animateNextFormatPickerOpen = FormatPickerButton.IsChecked != true;

    private void FormatPickerButton_PreviewKeyDown(object sender, KeyEventArgs e) =>
        _animateNextFormatPickerOpen = false;

    private void FormatChoiceButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (AnimationsEnabled && sender is Button button) AnimateButtonScale(button, 1.012, 120);
    }

    private void FormatChoiceButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (AnimationsEnabled && sender is Button button) AnimateButtonScale(button, 1, 110);
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

    private static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!AnimationsEnabled || e.ChangedButton != MouseButton.Left) return;
        if (FindButton(e.OriginalSource as DependencyObject) is { IsEnabled: true } button && button.Name != "DropDownToggle")
        {
            AnimateButtonScale(button, 0.975, 80);
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!AnimationsEnabled || e.ChangedButton != MouseButton.Left) return;
        if (FindButton(e.OriginalSource as DependencyObject) is { } button && button.Name != "DropDownToggle")
        {
            var restingScale = button.Tag is ConversionFormat && button.IsMouseOver ? 1.012 : 1;
            AnimateButtonScale(button, restingScale, 130);
        }
    }

    private static ButtonBase? FindButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase button) return button;
            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private static void AnimateButtonScale(ButtonBase button, double target, int durationMilliseconds)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            button.RenderTransform = scale;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        AnimateScale(scale, target, durationMilliseconds);
    }

    private void AnimateFormatPickerOpen()
    {
        ResetFormatPickerMotion();
        FormatPickerSurface.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.55, 1, 180, FillBehavior.Stop));
        FormatPickerScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(0.96, 1, 180, FillBehavior.Stop));
        FormatPickerScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(0.96, 1, 180, FillBehavior.Stop));
    }

    private void ResetFormatPickerMotion()
    {
        FormatPickerSurface.BeginAnimation(UIElement.OpacityProperty, null);
        FormatPickerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FormatPickerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        FormatPickerSurface.Opacity = 1;
        FormatPickerScale.ScaleX = 1;
        FormatPickerScale.ScaleY = 1;
    }

    private void AnimateCompletionStatus()
    {
        OverallStatusText.BeginAnimation(UIElement.OpacityProperty, null);
        OverallStatusTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        OverallStatusText.Opacity = 1;
        OverallStatusTranslate.Y = 0;
        if (!AnimationsEnabled) return;

        OverallStatusText.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.35, 1, 220, FillBehavior.Stop));
        OverallStatusTranslate.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(6, 0, 220, FillBehavior.Stop));
    }

    private void Job_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_animateCurrentBatch || !AnimationsEnabled || e.PropertyName != nameof(ConversionJob.Status) ||
            sender is not ConversionJob { Status: JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Skipped } job)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => AnimateJobResolution(job));
    }

    private void AnimateJobEntrance(ConversionJob job, int delayMilliseconds)
    {
        if (JobsList.ItemContainerGenerator.ContainerFromItem(job) is not ListViewItem item) return;
        var translate = GetOrCreateTranslateTransform(item);
        item.BeginAnimation(UIElement.OpacityProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        item.Opacity = 0.42;
        translate.X = -14;

        var fade = CreateAnimation(null, 1, 210, FillBehavior.Stop);
        var slide = CreateAnimation(null, 0, 210, FillBehavior.Stop);
        fade.BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds);
        slide.BeginTime = fade.BeginTime;
        fade.Completed += (_, _) =>
        {
            item.Opacity = 1;
            translate.X = 0;
            item.BeginAnimation(UIElement.OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
        };
        item.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void AnimateJobResolution(ConversionJob job)
    {
        if (JobsList.ItemContainerGenerator.ContainerFromItem(job) is not ListViewItem item) return;
        var translate = GetOrCreateTranslateTransform(item);
        item.Opacity = 1;
        translate.X = 0;
        item.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.68, 1, 180, FillBehavior.Stop));
        translate.BeginAnimation(TranslateTransform.XProperty, CreateAnimation(6, 0, 180, FillBehavior.Stop));
    }

    private Task AnimateJobExitAsync(ConversionJob job)
    {
        if (!AnimationsEnabled || JobsList.ItemContainerGenerator.ContainerFromItem(job) is not ListViewItem item)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var translate = GetOrCreateTranslateTransform(item);
        var fade = CreateAnimation(null, 0, 110);
        fade.Completed += (_, _) => completion.TrySetResult();
        item.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(null, -7, 110));
        return completion.Task;
    }

    private static TranslateTransform GetOrCreateTranslateTransform(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform translate) return translate;
        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    private void ShowDropFeedback()
    {
        var wasVisible = DropFeedbackOverlay.Visibility == Visibility.Visible;
        if (wasVisible && !_isDropFeedbackHiding) return;

        ++_dropFeedbackAnimationVersion;
        _isDropFeedbackHiding = false;
        DropFeedbackOverlay.Visibility = Visibility.Visible;
        DropFeedbackOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        DropFeedbackScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        DropFeedbackScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (wasVisible)
        {
            DropFeedbackOverlay.Opacity = 1;
            DropFeedbackScale.ScaleX = 1;
            DropFeedbackScale.ScaleY = 1;
            return;
        }

        if (!AnimationsEnabled)
        {
            DropFeedbackOverlay.Opacity = 1;
            DropFeedbackScale.ScaleX = 1;
            DropFeedbackScale.ScaleY = 1;
            return;
        }

        DropFeedbackOverlay.Opacity = 1;
        DropFeedbackScale.ScaleX = 1;
        DropFeedbackScale.ScaleY = 1;
        DropFeedbackOverlay.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0.2, 1, 170, FillBehavior.Stop));
        DropFeedbackScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(0.985, 1, 200, FillBehavior.Stop));
        DropFeedbackScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(0.985, 1, 200, FillBehavior.Stop));
    }

    private void HideDropFeedback()
    {
        if (DropFeedbackOverlay.Visibility != Visibility.Visible) return;

        var animationVersion = ++_dropFeedbackAnimationVersion;
        _isDropFeedbackHiding = true;
        if (!AnimationsEnabled)
        {
            DropFeedbackOverlay.Visibility = Visibility.Collapsed;
            DropFeedbackOverlay.Opacity = 0;
            _isDropFeedbackHiding = false;
            return;
        }

        var fadeOut = CreateAnimation(null, 0, 110, FillBehavior.Stop);
        fadeOut.Completed += (_, _) =>
        {
            if (animationVersion != _dropFeedbackAnimationVersion) return;
            DropFeedbackOverlay.Visibility = Visibility.Collapsed;
            DropFeedbackOverlay.Opacity = 0;
            _isDropFeedbackHiding = false;
        };
        DropFeedbackOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static void AnimateScale(ScaleTransform scale, double target, int durationMilliseconds)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateAnimation(null, target, durationMilliseconds));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateAnimation(null, target, durationMilliseconds));
    }

    private static DoubleAnimation CreateAnimation(
        double? from,
        double to,
        int durationMilliseconds,
        FillBehavior fillBehavior = FillBehavior.HoldEnd)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMilliseconds),
            EasingFunction = new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseOut },
            FillBehavior = fillBehavior
        };
        if (from is double fromValue) animation.From = fromValue;
        return animation;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours} h {duration.Minutes:00} min";
        if (duration.TotalMinutes >= 1) return $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s";
        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))} s";
    }

    private static bool LastInputWasKeyboard => InputManager.Current.MostRecentInputDevice is KeyboardDevice;

    private sealed record FormatPickerItem(ConversionFormat Format, bool IsSelected)
    {
        public string DisplayExtension => Format.DisplayExtension;
        public string Tooltip => $"{Format.DisplayName} — {Format.Description}";
    }

}
