using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using ConvertAvif;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ConvertAvifGUI;

public partial class MainWindow
{
    private readonly ImageConverter _converter = new();
    private CancellationTokenSource? _cts;
    private AppSettings _settings = new();
    private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
    private readonly ObservableCollection<ConversionResult> _failures = [];

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        FailureListView.ItemsSource = _failures;
    }

    private void LoadSettings()
    {
        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            _settings = config.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
            
            // UIに値をセット
            SourceDirTextBox.Text = _settings.SourceDirectory;
            ExtensionsTextBox.Text = _settings.Extensions;
            QualitySlider.Value = _settings.Quality;
            EvaluationModeComboBox.Text = _settings.EvaluationMode;
            Ssimulacra2PathTextBox.Text = _settings.Ssimulacra2Path ?? "";
            SsimTextBox.Text = _settings.QualityThreshold.ToString();
            ParallelTextBox.Text = _settings.MaxDegreeOfParallelism.ToString();
            EngineComboBox.Text = _settings.ConversionEngine;
            AvifEncPathTextBox.Text = _settings.AvifEncPath ?? "";
            AvifEncOptionsTextBox.Text = _settings.AvifEncCustomOptions ?? "";
            PriorityComboBox.SelectedIndex = _settings.AvifEncPriority ?? 0;
            SpeedSlider.Value = _settings.AvifEncSpeed ?? 3;
            TuneComboBox.Text = _settings.AvifEncTune ?? "iq";

            // 初期状態の更新
            UpdateAvifEncSettingsVisibility();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"設定の読み込みに失敗しました: {ex.Message}");
        }
    }

    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };
    private void SaveSettings()
    {
        _settings.SourceDirectory = SourceDirTextBox.Text;
        _settings.Extensions = ExtensionsTextBox.Text;
        _settings.Quality = (uint)QualitySlider.Value;
        _settings.EvaluationMode = EvaluationModeComboBox.Text;
        _settings.Ssimulacra2Path = Ssimulacra2PathTextBox.Text;
        if (double.TryParse(SsimTextBox.Text, out var threshold)) _settings.QualityThreshold = threshold;
        if (int.TryParse(ParallelTextBox.Text, out var parallel)) _settings.MaxDegreeOfParallelism = parallel;
        _settings.ConversionEngine = EngineComboBox.Text;
        _settings.AvifEncPath = AvifEncPathTextBox.Text;
        _settings.AvifEncCustomOptions = AvifEncOptionsTextBox.Text;
        _settings.AvifEncPriority = PriorityComboBox.SelectedIndex;
        if (int.TryParse(SpeedTextBox.Text, out var speed)) _settings.AvifEncSpeed = speed;
        _settings.AvifEncTune = TuneComboBox.Text;

        try
        {
            var json = JsonSerializer.Serialize(new { AppSettings = _settings }, _jsonSerializerOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            // 設定保存の失敗は致命的ではないが通知
            Console.WriteLine($"設定の保存に失敗しました: {ex.Message}");
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(SourceDirTextBox.Text) ? SourceDirTextBox.Text : null
        };

        if (dialog.ShowDialog() == true)
        {
            SourceDirTextBox.Text = dialog.FolderName;
        }
    }

    private void AvifEncBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            InitialDirectory = !string.IsNullOrWhiteSpace(AvifEncPathTextBox.Text) 
                ? Path.GetDirectoryName(AvifEncPathTextBox.Text) 
                : null
        };

        if (dialog.ShowDialog() == true)
        {
            AvifEncPathTextBox.Text = dialog.FileName;
        }
    }

    private void Ssimulacra2BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            InitialDirectory = !string.IsNullOrWhiteSpace(Ssimulacra2PathTextBox.Text)
                ? Path.GetDirectoryName(Ssimulacra2PathTextBox.Text)
                : null
        };

        if (dialog.ShowDialog() == true)
        {
            Ssimulacra2PathTextBox.Text = dialog.FileName;
        }
    }

    private void ConfigureConverter(uint quality, double threshold, uint speed)
    {
        _converter.Quality = quality;
        _converter.ConversionEngine = Enum.TryParse<AvifConversionEngine>(EngineComboBox.Text, out var engine) ? engine : AvifConversionEngine.Magick;
        _converter.AvifEncPath = AvifEncPathTextBox.Text;
        _converter.AvifEncCustomOptions = AvifEncOptionsTextBox.Text;
        _converter.AvifEncPriority = PriorityComboBox.SelectedIndex switch
        {
            0 => ProcessPriorityClass.Idle,
            1 => ProcessPriorityClass.BelowNormal,
            2 => ProcessPriorityClass.Normal,
            3 => ProcessPriorityClass.AboveNormal,
            4 => ProcessPriorityClass.High,
            5 => ProcessPriorityClass.RealTime,
            _ => ProcessPriorityClass.Idle
        };
        _converter.EvaluationMode = Enum.TryParse<QualityEvaluationMode>(EvaluationModeComboBox.Text, out var evalMode) ? evalMode : QualityEvaluationMode.SSIM;
        _converter.Ssimulacra2Path = Ssimulacra2PathTextBox.Text;
        _converter.QualityThreshold = threshold;
        _converter.Speed = speed;
        if (quality != 100)
        {
            _converter.AvifEncCustomOptions = AvifEncOptionsTextBox.Text + " -a tune=" + TuneComboBox.Text;
            if (SharpYuvCheckBox.IsChecked == true)
            {
                _converter.AvifEncCustomOptions += " --sharpyuv";
            }
        }
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceDir = SourceDirTextBox.Text;
        if (!Directory.Exists(sourceDir))
        {
            MessageBox.Show("有効なソースディレクトリを選択してください。");
            return;
        }

        SaveSettings();
        _failures.Clear();
        _cts = new CancellationTokenSource();
        
        SetUiState(true);
        StatusTextBlock.Text = "スキャン中...";
        ConversionProgressBar.Value = 0;
        ConversionProgressBar.IsIndeterminate = true;

        try
        {
            var extensions = ExtensionsTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!double.TryParse(SsimTextBox.Text, out var threshold)) threshold = 0.9;
            if (!int.TryParse(ParallelTextBox.Text, out var maxParallelism)) maxParallelism = 4;
            if (!uint.TryParse(SpeedTextBox.Text, out var speed)) speed = 3;

            ConfigureConverter(_settings.Quality, threshold, speed);

            var progress = new Progress<ConversionProgress>(p =>
            {
                if (p.TotalFiles > 0)
                {
                    ConversionProgressBar.IsIndeterminate = false;
                    ConversionProgressBar.Maximum = p.TotalFiles;
                    ConversionProgressBar.Value = p.ProcessedFiles;
                    var percent = (double)p.ProcessedFiles / p.TotalFiles * 100;
                    StatusTextBlock.Text = $"進行中: {p.ProcessedFiles} / {p.TotalFiles} ({percent:F1}%) (失敗: {p.FailedFiles})";
                }
                else
                {
                    ConversionProgressBar.IsIndeterminate = true;
                    StatusTextBlock.Text = $"進行中: {p.ProcessedFiles} 件処理 (失敗: {p.FailedFiles})";
                }
            });

            await foreach (var result in _converter.ConvertDirectoryToAvifAsync(
                sourceDir, 
                extensions, 
                maxParallelism, 
                progress, 
                _cts.Token))
            {
                if (!result.IsSuccess)
                {
                    _failures.Add(result);
                }
            }

            StatusTextBlock.Text = $"変換完了(失敗: {_failures.Count})";
            MessageBox.Show(this, $"変換が完了しました。(失敗: {_failures.Count})");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "キャンセルされました";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"エラーが発生しました: {ex.Message}");
            StatusTextBlock.Text = "エラー発生";
        }
        finally
        {
            ConversionProgressBar.IsIndeterminate = false;
            SetUiState(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private async void ContinuousConvertButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceDir = SourceDirTextBox.Text;
        if (!Directory.Exists(sourceDir))
        {
            MessageBox.Show("有効なソースディレクトリを選択してください。");
            return;
        }

        SaveSettings();
        _failures.Clear();
        _cts = new CancellationTokenSource();

        SetUiState(true);
        StatusTextBlock.Text = "スキャン中...";
        ConversionProgressBar.Value = 0;
        ConversionProgressBar.IsIndeterminate = true;

        try
        {
            var extensions = ExtensionsTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!double.TryParse(SsimTextBox.Text, out var threshold)) threshold = 0.9;
            if (!int.TryParse(ParallelTextBox.Text, out var maxParallelism)) maxParallelism = 4;
            if (!uint.TryParse(SpeedTextBox.Text, out var speed)) speed = 3;

            var currentQuality = _settings.Quality;

            var targetFiles = Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();

            var persistentFailures = new List<ConversionResult>();

            while (targetFiles.Count > 0)
            {
                QualitySlider.Value = currentQuality;
                ConfigureConverter(currentQuality, threshold, speed);

                var qualityFailures = new List<ConversionResult>();

                var progress = new Progress<ConversionProgress>(p =>
                {
                    if (p.TotalFiles > 0)
                    {
                        ConversionProgressBar.IsIndeterminate = false;
                        ConversionProgressBar.Maximum = p.TotalFiles;
                        ConversionProgressBar.Value = p.ProcessedFiles;
                        var percent = (double)p.ProcessedFiles / p.TotalFiles * 100;
                        StatusTextBlock.Text = $"Quality {currentQuality} で変換中: {p.ProcessedFiles} / {p.TotalFiles} ({percent:F1}%) (失敗: {p.FailedFiles})";
                    }
                    else
                    {
                        ConversionProgressBar.IsIndeterminate = true;
                        StatusTextBlock.Text = $"Quality {currentQuality} で変換中: {p.ProcessedFiles} 件処理 (失敗: {p.FailedFiles})";
                    }
                });

                await foreach (var result in _converter.ConvertFilesToAvifAsync(
                    targetFiles,
                    maxParallelism,
                    progress,
                    _cts.Token))
                {
                    if (!result.IsSuccess)
                    {
                        if (result.IsQualityEvaluationFailure)
                        {
                            qualityFailures.Add(result);
                        }
                        else
                        {
                            persistentFailures.Add(result);
                        }
                    }
                }

                _failures.Clear();
                foreach (var failure in persistentFailures)
                {
                    _failures.Add(failure);
                }
                foreach (var failure in qualityFailures)
                {
                    _failures.Add(failure);
                }

                if (qualityFailures.Count == 0 || currentQuality >= 100)
                {
                    break;
                }

                currentQuality = Math.Min(100, currentQuality + 5);
                targetFiles = qualityFailures.Select(f => f.InputPath).ToList();
            }

            StatusTextBlock.Text = $"連続変換完了(失敗: {_failures.Count})";
            MessageBox.Show(this, $"連続変換が完了しました。(失敗: {_failures.Count})");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "キャンセルされました";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"エラーが発生しました: {ex.Message}");
            StatusTextBlock.Text = "エラー発生";
        }
        finally
        {
            ConversionProgressBar.IsIndeterminate = false;
            SetUiState(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        SaveSettings();
        Environment.Exit(0);
    }

    private void SetUiState(bool isRunning)
    {
        ConvertButton.IsEnabled = !isRunning;
        ContinuousConvertButton.IsEnabled = !isRunning;
        CancelButton.IsEnabled = isRunning;
        SourceDirTextBox.IsEnabled = !isRunning;
        BrowseButton.IsEnabled = !isRunning;
        ExtensionsTextBox.IsEnabled = !isRunning;
        EvaluationModeComboBox.IsEnabled = !isRunning;
        SsimTextBox.IsEnabled = !isRunning;
        QualitySlider.IsEnabled = !isRunning;
        QualityTextBox.IsEnabled = !isRunning;
        ParallelTextBox.IsEnabled = !isRunning;
        EngineComboBox.IsEnabled = !isRunning;
        Ssimulacra2PathTextBox.IsEnabled = !isRunning;
        Ssimulacra2BrowseButton.IsEnabled = !isRunning;
        PriorityComboBox.IsEnabled = !isRunning;

        UpdateAvifEncSettingsVisibility();
        UpdateEvaluationSettingsVisibility();
    }

    private void EngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAvifEncSettingsVisibility();
    }

    private void EvaluationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEvaluationSettingsVisibility();
    }

    private void UpdateAvifEncSettingsVisibility()
    {
        if (AvifEncSettingsGroup == null) return;

        var isAvifEnc = false;
        if (EngineComboBox.SelectedItem is ComboBoxItem item)
        {
            isAvifEnc = item.Content.ToString() == "AvifEnc";
        }
        else
        {
            isAvifEnc = EngineComboBox.Text == "AvifEnc";
        }

        // 実行中は常に無効、停止中はエンジン設定に従う
        AvifEncSettingsGroup.IsEnabled = ConvertButton.IsEnabled && isAvifEnc;
    }

    private void UpdateEvaluationSettingsVisibility()
    {
        if (Ssimulacra2SettingsGroup == null) return;

        var isSsimulacra2 = false;
        if (EvaluationModeComboBox.SelectedItem is ComboBoxItem item)
        {
            isSsimulacra2 = item.Content.ToString() == "Ssimulacra2";
        }
        else
        {
            isSsimulacra2 = EvaluationModeComboBox.Text == "Ssimulacra2";
        }

        Ssimulacra2SettingsGroup.Visibility = isSsimulacra2 ? Visibility.Visible : Visibility.Collapsed;
        ThresholdLabel.Text = isSsimulacra2 ? "SSIMULACRA2 閾値:" : "SSIM 閾値:";
    }

    private GridViewColumnHeader? _lastHeaderClicked = null;
    private ListSortDirection _lastDirection = ListSortDirection.Ascending;
    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader headerClicked || headerClicked.Role == GridViewColumnHeaderRole.Padding) return;

        if (headerClicked.Column.DisplayMemberBinding is not Binding binding) return;
        var sortBy = binding.Path.Path;
        if (string.IsNullOrEmpty(sortBy)) return;

        var direction = ListSortDirection.Ascending;
        if (headerClicked == _lastHeaderClicked && _lastDirection == ListSortDirection.Ascending)
        {
            direction = ListSortDirection.Descending;
        }

        // ソート実行
        Sort(sortBy, direction);

        // 【追加】ヘッダーに▲▼の見た目を反映
        UpdateHeaderTemplate(headerClicked, direction);

        _lastHeaderClicked = headerClicked;
        _lastDirection = direction;
    }
    private void Sort(string sortBy, ListSortDirection direction)
    {
        var dataView = CollectionViewSource.GetDefaultView(FailureListView.ItemsSource);
        if (dataView == null) return;

        dataView.SortDescriptions.Clear();
        var sd = new SortDescription(sortBy, direction);
        dataView.SortDescriptions.Add(sd);
        dataView.Refresh();
    }
    // 【追加】ヘッダーの文字の後ろに矢印をつける処理
    private void UpdateHeaderTemplate(GridViewColumnHeader clickedHeader, ListSortDirection direction)
    {
        // 前回クリックしたヘッダーから矢印を消す
        if (_lastHeaderClicked != null && _lastHeaderClicked != clickedHeader)
        {
            ResetHeaderContent(_lastHeaderClicked);
        }

        // 初回、または文字列のままの場合は現在の状態を保持
        if (clickedHeader.Tag is not string propName) return;
        // 元のヘッダーテキストを取得
        var baseText = clickedHeader.Column.Header.ToString();
        // 既に矢印がついていたら削る
        if (baseText != null && (baseText.EndsWith(" ▲") || baseText.EndsWith(" ▼")))
        {
            baseText = baseText[..^2];
        }

        // 新しい矢印を付与
        var arrow = (direction == ListSortDirection.Ascending) ? " ▲" : " ▼";
        clickedHeader.Column.Header = baseText + arrow;
    }
    private void ResetHeaderContent(GridViewColumnHeader header)
    {
        var text = header.Column.Header.ToString();
        if (text != null && (text.EndsWith(" ▲") || text.EndsWith(" ▼")))
        {
            header.Column.Header = text[..^2];
        }
    }

    private void FailureListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hasSelection = FailureListView.SelectedItems.Count > 0;
        OpenFileMenuItem.IsEnabled = hasSelection;
        ShowInExplorerMenuItem.IsEnabled = hasSelection;
        CopyErrorMessageMenuItem.IsEnabled = hasSelection;
        CopyFilePathMenuItem.IsEnabled = hasSelection;
        CopyFullRowMenuItem.IsEnabled = hasSelection;
    }

    private void ListViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem item)
        {
            if (!item.IsSelected)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }
    }

    private void FailureListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedFiles();
            e.Handled = true;
        }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CopyErrorMessage();
            e.Handled = true;
        }
    }

    private void FailureListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView && e.ChangedButton == MouseButton.Left)
        {
            var dependencyObject = e.OriginalSource as DependencyObject;
            var listViewItem = FindVisualParent<ListViewItem>(dependencyObject);
            if (listViewItem != null)
            {
                OpenSelectedFiles();
            }
        }
    }

    private void OpenFileMenuItem_Click(object sender, RoutedEventArgs e) => OpenSelectedFiles();
    private void ShowInExplorerMenuItem_Click(object sender, RoutedEventArgs e) => ShowSelectedFilesInExplorer();
    private void CopyErrorMessageMenuItem_Click(object sender, RoutedEventArgs e) => CopyErrorMessage();
    private void CopyFilePathMenuItem_Click(object sender, RoutedEventArgs e) => CopyFilePath();
    private void CopyFullRowMenuItem_Click(object sender, RoutedEventArgs e) => CopyFullRow();

    /// <summary>
    ///     選択された対象ファイルを開きます。
    /// </summary>
    private void OpenSelectedFiles()
    {
        var selected = FailureListView.SelectedItems.Cast<ConversionResult>().ToList();
        if (selected.Count == 0) return;

        foreach (var result in selected)
        {
            if (string.IsNullOrWhiteSpace(result.InputPath)) continue;

            if (!File.Exists(result.InputPath))
            {
                MessageBox.Show(this, $"ファイルが見つかりません: {result.InputPath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            try
            {
                Process.Start(new ProcessStartInfo(result.InputPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"ファイルを開けませんでした ({result.InputPath}): {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    ///     選択された対象ファイルをエクスプローラーで表示します。
    /// </summary>
    private void ShowSelectedFilesInExplorer()
    {
        var selected = FailureListView.SelectedItems.Cast<ConversionResult>().ToList();
        if (selected.Count == 0) return;

        foreach (var result in selected)
        {
            if (string.IsNullOrWhiteSpace(result.InputPath)) continue;

            try
            {
                if (File.Exists(result.InputPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.InputPath}\"")
                    {
                        UseShellExecute = true
                    });
                }
                else
                {
                    var dir = Path.GetDirectoryName(result.InputPath);
                    if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                        {
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        MessageBox.Show(this, $"ファイルまたはフォルダが見つかりません: {result.InputPath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"エクスプローラーを開けませんでした ({result.InputPath}): {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    ///     選択された項目のエラーメッセージをクリップボードにコピーします。
    /// </summary>
    private void CopyErrorMessage()
    {
        var selected = FailureListView.SelectedItems.Cast<ConversionResult>().ToList();
        if (selected.Count == 0) return;

        var messages = selected
            .Select(x => x.ErrorMessage ?? string.Empty)
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();

        if (messages.Count > 0)
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, messages));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"クリップボードへのコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>
    ///     選択された項目のファイルパスをクリップボードにコピーします。
    /// </summary>
    private void CopyFilePath()
    {
        var selected = FailureListView.SelectedItems.Cast<ConversionResult>().ToList();
        if (selected.Count == 0) return;

        var paths = selected
            .Select(x => x.InputPath)
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();

        if (paths.Count > 0)
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, paths));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"クリップボードへのコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>
    ///     選択された項目の行情報（パス、成否、メッセージ）をクリップボードにコピーします。
    /// </summary>
    private void CopyFullRow()
    {
        var selected = FailureListView.SelectedItems.Cast<ConversionResult>().ToList();
        if (selected.Count == 0) return;

        var rows = selected
            .Select(x => $"{x.InputPath}\t{(x.IsSuccess ? "成功" : "失敗")}\t{x.ErrorMessage}")
            .ToList();

        if (rows.Count > 0)
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, rows));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"クリップボードへのコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
