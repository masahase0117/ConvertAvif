using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
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
            await img.WriteAsync(inputPath, MagickFormat.Png);
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
            await img.WriteAsync(inputPath, MagickFormat.Png);
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

    [Fact]
    public async Task ProcessFile_Ssimulacra2_MultipleLowScoreParallel_ShouldDeleteAllOutputFiles()
    {
        // Arrange
        CreateMockSsimulacra2("0.4");

        var files = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var filePath = Path.Combine(_tempDir, $"multi_low_{i}.png");
            using (var img = new MagickImage(MagickColors.Purple, 50, 50))
            {
                await img.WriteAsync(filePath, MagickFormat.Png);
            }
            files.Add(filePath);
        }

        var ic = new ImageConverter
        {
            EvaluationMode = QualityEvaluationMode.Ssimulacra2,
            Ssimulacra2Path = _dummySsimulacra2,
            QualityThreshold = 0.9,
            Quality = 80
        };

        // Act
        var results = new List<ConversionResult>();
        await foreach (var result in ic.ConvertDirectoryToAvifAsync(_tempDir, new[] { ".png" }, maxDegreeOfParallelism: 4))
        {
            results.Add(result);
        }

        // Assert
        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.False(r.IsSuccess));
        foreach (var file in files)
        {
            Assert.True(File.Exists(file), $"Original file should be kept: {file}");
            Assert.False(File.Exists(Path.ChangeExtension(file, ".avif")), $"Output AVIF should be deleted: {file}");
        }
    }

    [Fact]
    public async Task ProcessFile_Ssimulacra2_CmykJpg_ShouldConvertOriginalToPngBeforeComparison()
    {
        // Arrange
        // ssimulacra2のモックを作成: 第1引数（元画像）がPNG拡張子なら0.95を出力して正常終了、JPGなら異常終了(exit code 1)
        var mockBat = Path.Combine(_tempDir, "ssimulacra2_cmyk_check.bat");
        File.WriteAllText(mockBat, @"@echo off
if /I ""%~x1""=="" .png"" (
    echo 0.95
    exit /b 0
)
if /I ""%~x1""=="" .jpg"" (
    echo CMYK JPEG decode error 1>&2
    exit /b 1
)
if /I ""%~x1""=="".png"" (
    echo 0.95
    exit /b 0
)
if /I ""%~x1""=="".jpg"" (
    echo CMYK JPEG decode error 1>&2
    exit /b 1
)
echo 0.95
exit /b 0
");

        var inputPath = Path.Combine(_tempDir, "test_cmyk.jpg");
        using (var img = new MagickImage(MagickColors.Cyan, 50, 50))
        {
            img.ColorSpace = ColorSpace.CMYK;
            await img.WriteAsync(inputPath, MagickFormat.Jpg);
        }

        using (var check = new MagickImage(inputPath))
        {
            Assert.Equal(MagickFormat.Jpeg, check.Format);
            Assert.Equal(ColorSpace.CMYK, check.ColorSpace);
        }

        var ic = new ImageConverter
        {
            EvaluationMode = QualityEvaluationMode.Ssimulacra2,
            Ssimulacra2Path = mockBat,
            QualityThreshold = 0.9,
            Quality = 90
        };

        // Act
        var results = new List<ConversionResult>();
        await foreach (var result in ic.ConvertDirectoryToAvifAsync(_tempDir, new[] { ".jpg" }))
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
    public async Task ProcessFile_Ssimulacra2_RgbJpg_ShouldPassOriginalJpgDirectly()
    {
        // Arrange
        // ssimulacra2のモックを作成: 第1引数（元画像）がJPG拡張子なら0.95を出力、PNGの場合は異常終了
        var mockBat = Path.Combine(_tempDir, "ssimulacra2_rgb_check.bat");
        File.WriteAllText(mockBat, @"@echo off
if /I ""%~x1""=="".jpg"" (
    echo 0.95
    exit /b 0
)
if /I ""%~x1""=="" .jpg"" (
    echo 0.95
    exit /b 0
)
echo Original should be passed directly without PNG conversion 1>&2
exit /b 1
");

        var inputPath = Path.Combine(_tempDir, "test_rgb.jpg");
        using (var img = new MagickImage(MagickColors.Red, 50, 50))
        {
            await img.WriteAsync(inputPath, MagickFormat.Jpg);
        }

        var ic = new ImageConverter
        {
            EvaluationMode = QualityEvaluationMode.Ssimulacra2,
            Ssimulacra2Path = mockBat,
            QualityThreshold = 0.9,
            Quality = 90
        };

        // Act
        var results = new List<ConversionResult>();
        await foreach (var result in ic.ConvertDirectoryToAvifAsync(_tempDir, new[] { ".jpg" }))
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
    public async Task ProcessFile_Ssimulacra2_GrayscalePngWithIcc_ShouldRemoveIccProfileBeforeComparison()
    {
        // Arrange
        var capturedFile = Path.Combine(_tempDir, "captured_orig.png");
        var mockBat = Path.Combine(_tempDir, "ssimulacra2_gray_icc_check.bat");
        File.WriteAllText(mockBat, $@"@echo off
copy /Y ""%~1"" ""{capturedFile}"" >nul
echo 0.95
exit /b 0
");

        var inputPath = Path.Combine(_tempDir, "test_gray_icc.png");
        using (var img = new MagickImage(MagickColors.Gray, 100, 100))
        {
            img.SetProfile(ColorProfile.AdobeRGB1998);
            await img.WriteAsync(inputPath, MagickFormat.Png);
        }

        using (var check = new MagickImage(inputPath))
        {
            Assert.NotNull(check.GetColorProfile());
        }

        var ic = new ImageConverter
        {
            EvaluationMode = QualityEvaluationMode.Ssimulacra2,
            Ssimulacra2Path = mockBat,
            QualityThreshold = 0.9,
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
        Assert.True(results[0].IsSuccess, results[0].ErrorMessage);
        Assert.False(File.Exists(inputPath));
        Assert.True(File.Exists(Path.ChangeExtension(inputPath, ".avif")));

        Assert.True(File.Exists(capturedFile));
        using (var capturedImg = new MagickImage(capturedFile))
        {
            // ssimulacra2 に渡された元画像（一時PNG）からはICCプロファイルが除去されていること
            Assert.Null(capturedImg.GetColorProfile());
        }
    }

    [Fact]
    public async Task ProcessFile_Ssimulacra2_GrayscalePngWithoutIcc_ShouldPassOriginalPngDirectly()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "test_gray_no_icc.png");
        var mockBat = Path.Combine(_tempDir, "ssimulacra2_gray_no_icc_check.bat");
        File.WriteAllText(mockBat, $@"@echo off
if /I ""%~1""==""{inputPath}"" (
    echo 0.95
    exit /b 0
)
echo Original should be passed directly without temp file conversion 1>&2
exit /b 1
");

        using (var img = new MagickImage(MagickColors.Gray, 100, 100))
        {
            img.ColorSpace = ColorSpace.Gray;
            await img.WriteAsync(inputPath, MagickFormat.Png);
        }

        using (var check = new MagickImage(inputPath))
        {
            Assert.Null(check.GetColorProfile());
        }

        var ic = new ImageConverter
        {
            EvaluationMode = QualityEvaluationMode.Ssimulacra2,
            Ssimulacra2Path = mockBat,
            QualityThreshold = 0.9,
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
        Assert.True(results[0].IsSuccess, results[0].ErrorMessage);
        Assert.False(File.Exists(inputPath));
        Assert.True(File.Exists(Path.ChangeExtension(inputPath, ".avif")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
