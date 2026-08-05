using System.Diagnostics;
using Xunit;
using ConvertAvif;
using ImageMagick;

namespace ConvertAvif.Tests;

public class Ssimulacra2Test : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dummySsimulacra2;

    public Ssimulacra2Test()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Ssimulacra2Test_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        
        // ダミーの ssimulacra2.bat を作成する
        // 標準出力にスコア（例: 0.95）を出力するだけの単純なもの
        _dummySsimulacra2 = Path.Combine(_tempDir, "ssimulacra2_mock.bat");
        CreateMockSsimulacra2("0.95");
    }

    private void CreateMockSsimulacra2(string outputScore)
    {
        File.WriteAllText(_dummySsimulacra2, $"@echo off\necho {outputScore}");
    }

    [Fact]
    public async Task ProcessFile_Ssimulacra2_ShouldUseMockScore()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "test.png");
        using (var img = new MagickImage(MagickColors.Blue, 100, 100))
        {
            img.Write(inputPath, MagickFormat.Png);
        }

        var ic = new ImageConverter
        {
            EvaluationMode = QualityEvaluationMode.Ssimulacra2,
            Ssimulacra2Path = _dummySsimulacra2,
            QualityThreshold = 0.9,
            Quality = 90
        };

        // Act
        // ProcessFile は private なので、一括変換メソッド経由でテストする
        var results = new List<ConversionResult>();
        await foreach (var result in ic.ConvertDirectoryToAvifAsync(_tempDir, new[] { ".png" }))
        {
            results.Add(result);
        }

        // Assert
        Assert.Single(results);
        Assert.True(results[0].IsSuccess, results[0].ErrorMessage);
        Assert.False(File.Exists(inputPath));
        Assert.True(File.Exists(Path.ChangeExtension(inputPath, ".avif")));
    }

    [Fact]
    public async Task ProcessFile_Ssimulacra2_LowScore_ShouldFail()
    {
        // Arrange
        // しきい値より低いスコアを返すように再作成
        CreateMockSsimulacra2("0.5");

        var inputPath = Path.Combine(_tempDir, "test_low.png");
        using (var img = new MagickImage(MagickColors.Red, 100, 100))
        {
            img.Write(inputPath, MagickFormat.Png);
        }

        var ic = new ImageConverter
        {
            EvaluationMode = QualityEvaluationMode.Ssimulacra2,
            Ssimulacra2Path = _dummySsimulacra2,
            QualityThreshold = 0.9, // 0.5 < 0.9 なので失敗するはず
            Quality = 90
        };

        // Act
        var results = new List<ConversionResult>();
        await foreach (var result in ic.ConvertDirectoryToAvifAsync(_tempDir, new[] { ".png" }))
        {
            results.Add(result);
        }

        // Assert
        Assert.Single(results);
        Assert.False(results[0].IsSuccess);
        Assert.Contains("SSIMULACRA2 too low", results[0].ErrorMessage!);
        Assert.True(File.Exists(inputPath));
        Assert.False(File.Exists(Path.ChangeExtension(inputPath, ".avif")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
