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

            _converter.Quality = _settings.Quality;
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

            var progress = new Progress<ConversionProgress>(p =>
            {
                ConversionProgressBar.IsIndeterminate = false;
                ConversionProgressBar.Maximum = p.TotalFiles;
                ConversionProgressBar.Value = p.ProcessedFiles;
                StatusTextBlock.Text = $"進行中: {p.ProcessedFiles} / {p.TotalFiles} (失敗: {p.FailedFiles})";
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

            StatusTextBlock.Text = "変換完了";
            MessageBox.Show("変換が完了しました。");
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "キャンセルされました";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エラーが発生しました: {ex.Message}");
            StatusTextBlock.Text = "エラー発生";
        }
        finally
        {
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
    }

    private void SetUiState(bool isRunning)
    {
        ConvertButton.IsEnabled = !isRunning;
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
}
