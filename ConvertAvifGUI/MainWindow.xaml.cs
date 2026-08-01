using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using ConvertAvif;
using System.Collections.ObjectModel;

namespace ConvertAvifGUI;

public partial class MainWindow : Window
{
    private readonly ImageConverter _converter = new();
    private CancellationTokenSource? _cts;
    private AppSettings _settings = new();
    private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
    private readonly ObservableCollection<ConversionResult> _failures = new();

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

            SourceDirTextBox.Text = _settings.SourceDirectory;
            ExtensionsTextBox.Text = _settings.Extensions;
            SsimTextBox.Text = _settings.SsimThreshold.ToString();
            ParallelTextBox.Text = _settings.MaxDegreeOfParallelism.ToString();
            EngineComboBox.Text = _settings.ConversionEngine;
            AvifEncPathTextBox.Text = _settings.AvifEncPath;
            AvifEncOptionsTextBox.Text = _settings.AvifEncCustomOptions;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"設定の読み込みに失敗しました: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        _settings.SourceDirectory = SourceDirTextBox.Text;
        _settings.Extensions = ExtensionsTextBox.Text;
        if (double.TryParse(SsimTextBox.Text, out double ssim)) _settings.SsimThreshold = ssim;
        if (int.TryParse(ParallelTextBox.Text, out int parallel)) _settings.MaxDegreeOfParallelism = parallel;
        _settings.ConversionEngine = EngineComboBox.Text;
        _settings.AvifEncPath = AvifEncPathTextBox.Text;
        _settings.AvifEncCustomOptions = AvifEncOptionsTextBox.Text;

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(new { AppSettings = _settings }, options);
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
            if (!double.TryParse(SsimTextBox.Text, out double ssimThreshold)) ssimThreshold = 0.9;
            if (!int.TryParse(ParallelTextBox.Text, out int maxParallelism)) maxParallelism = 4;

            _converter.ConversionEngine = Enum.TryParse<AvifConversionEngine>(EngineComboBox.Text, out var engine) ? engine : AvifConversionEngine.Magick;
            _converter.AvifEncPath = AvifEncPathTextBox.Text;
            _converter.AvifEncCustomOptions = AvifEncOptionsTextBox.Text;

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
                ssimThreshold, 
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

    private void SetUiState(bool isRunning)
    {
        ConvertButton.IsEnabled = !isRunning;
        CancelButton.IsEnabled = isRunning;
        SourceDirTextBox.IsEnabled = !isRunning;
        BrowseButton.IsEnabled = !isRunning;
        ExtensionsTextBox.IsEnabled = !isRunning;
        SsimTextBox.IsEnabled = !isRunning;
        ParallelTextBox.IsEnabled = !isRunning;
        EngineComboBox.IsEnabled = !isRunning;
        AvifEncPathTextBox.IsEnabled = !isRunning;
        AvifEncBrowseButton.IsEnabled = !isRunning;
        AvifEncOptionsTextBox.IsEnabled = !isRunning;
    }
}
