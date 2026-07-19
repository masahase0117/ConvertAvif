using ImageMagick;

namespace ConvertAvif.Tests;

public class ImageConverterTests
{
    [Fact]
    public void ConvertToAvif_ValidBmp_ShouldCreateAvif()
    {
        // Arrange
        const string bmpPath = "test_avif.bmp";
        const string avifPath = "test.avif";

        // 10x10のテスト用BMP画像を作成
        using (var image = new MagickImage(MagickColors.Blue, 10, 10))
        {
            image.Write(bmpPath, MagickFormat.Bmp);
        }

        try
        {
            var ic = new ImageConverter();
            // Act
            ic.ConvertToAvif(bmpPath, avifPath);

            // Assert
            Assert.True(File.Exists(avifPath), "Output AVIF file should exist.");

            // 出力ファイルが有効なAVIFであることを確認
            using (var avifImage = new MagickImage(avifPath))
            {
                Assert.Equal(10, (int)avifImage.Width);
                Assert.Equal(10, (int)avifImage.Height);
                // フォーマットがAVIFであることを確認 (Magick.NETのバージョンによってはAVIFと表示されない可能性もあるが、通常はサポートされている)
                Assert.Equal(MagickFormat.Avif, avifImage.Format);
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(bmpPath)) File.Delete(bmpPath);
            if (File.Exists(avifPath)) File.Delete(avifPath);
        }
    }

    [Fact]
    public async Task ConvertToAvif_Parallel_ShouldProcessAllFilesSuccessfully()
    {
        // Arrange
        const int taskCount = 10;
        var tasks = new List<Task>();
        var testFiles = new List<(string Bmp, string Avif)>();
        var ic = new ImageConverter();

        for (var i = 0; i < taskCount; i++)
        {
            var bmpPath = $"parallel_test_{i}.bmp";
            var avifPath = $"parallel_test_{i}.avif";
            testFiles.Add((bmpPath, avifPath));

            // テスト用BMP作成
            using var image = new MagickImage(MagickColors.Green, 10, 10);
            image.Write(bmpPath, MagickFormat.Bmp);
        }

        try
        {
            // Act
            foreach (var file in testFiles)
                tasks.Add(Task.Run(() => ic.ConvertToAvif(file.Bmp, file.Avif)));

            await Task.WhenAll(tasks);

            // Assert
            foreach (var file in testFiles)
            {
                Assert.True(File.Exists(file.Avif), $"Output AVIF file {file.Avif} should exist.");
                using var avifImage = new MagickImage(file.Avif);
                Assert.Equal(MagickFormat.Avif, avifImage.Format);
            }
        }
        finally
        {
            // Cleanup
            foreach (var file in testFiles)
            {
                if (File.Exists(file.Bmp)) File.Delete(file.Bmp);
                if (File.Exists(file.Avif)) File.Delete(file.Avif);
            }
        }
    }

    [Fact]
    public void ConvertToAvif_WithColorSpaceAndDepth_ShouldCreateAvifWithSpecifiedSettings()
    {
        // Arrange
        const string bmpPath = "test_settings.bmp";
        const string avifPath = "test_settings.avif";
        const string targetColorSpace = "LinearGray";
        const int targetDepth = 8; // Q8ライブラリなので8を指定

        using (var image = new MagickImage(MagickColors.White, 10, 10))
        {
            image.Write(bmpPath, MagickFormat.Bmp);
        }

        try
        {
            // Act
            var ic = new ImageConverter
            {
                Quality = 50,
                ColorSpace = targetColorSpace,
                BitDepth = targetDepth
            };
            ic.ConvertToAvif(bmpPath, avifPath);

            // Assert
            Assert.True(File.Exists(avifPath));
            using (var avifImage = new MagickImage(avifPath))
            {
                Assert.Equal((uint)targetDepth, avifImage.Depth);
                Assert.Equal(MagickFormat.Avif, avifImage.Format);
            }
        }
        finally
        {
            if (File.Exists(bmpPath)) File.Delete(bmpPath);
            if (File.Exists(avifPath)) File.Delete(avifPath);
        }
    }

    [Theory]
    [InlineData("RGB")]
    [InlineData("YV12")]
    [InlineData("YUV444")]
    public void ConvertToAvif_WithSpecificPixelFormats_ShouldCreateAvif(string pixelFormat)
    {
        // Arrange
        var bmpPath = $"test_{pixelFormat}.bmp";
        var avifPath = $"test_{pixelFormat}.avif";

        using (var image = new MagickImage(MagickColors.Blue, 10, 10))
        {
            image.Write(bmpPath, MagickFormat.Bmp);
        }

        try
        {
            // Act
            var ic = new ImageConverter();
            ic.ColorSpace = pixelFormat;
            ic.ConvertToAvif(bmpPath, avifPath);

            // Assert
            Assert.True(File.Exists(avifPath), $"Output AVIF for {pixelFormat} should exist.");
            using var avifImage = new MagickImage(avifPath);
            Assert.Equal(MagickFormat.Avif, avifImage.Format);
        }
        finally
        {
            if (File.Exists(bmpPath)) File.Delete(bmpPath);
            if (File.Exists(avifPath)) File.Delete(avifPath);
        }
    }

    [Fact]
    public async Task ConvertDirectoryToAvifAsync_ValidFiles_ShouldConvertAndDeleteOriginals()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var subDir = Path.Combine(testDir, "sub");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(testDir, "image1.jpg");
        var file2 = Path.Combine(subDir, "image2.jpg");

        // JPGとして保存 (SSIM比較のために少し複雑な画像にする)
        using (var img = new MagickImage(MagickColors.Red, 100, 100))
        {
            img.AddNoise(NoiseType.Gaussian);
            img.Write(file1, MagickFormat.Jpg);
            img.Write(file2, MagickFormat.Jpg);
        }

        var progressList = new List<ConversionProgress>();
        var progress = new Progress<ConversionProgress>(p => progressList.Add(p));

        try
        {
            // Act
            var results = new List<ConversionResult>();
            var ic = new ImageConverter();
            ic.Quality = 50;
            await foreach (var result in ic.ConvertDirectoryToAvifAsync(
                testDir,
                new[] { ".jpg" },
                0.1, // しきい値をさらに下げる
                progress: progress))
            {
                results.Add(result);
            }

            // Assert
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.True(r.IsSuccess, r.ErrorMessage));
            Assert.False(File.Exists(file1), "Original file1 should be deleted.");
            Assert.False(File.Exists(file2), "Original file2 should be deleted.");
            Assert.True(File.Exists(Path.ChangeExtension(file1, ".avif")));
            Assert.True(File.Exists(Path.ChangeExtension(file2, ".avif")));

            // 進捗通知の確認
            Assert.NotEmpty(progressList);
            Assert.Equal(2, progressList.Last().ProcessedFiles);
            Assert.Equal(2, progressList.Last().TotalFiles);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ConvertDirectoryToAvifAsync_LowSSIM_ShouldNotDeleteOriginal()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var file = Path.Combine(testDir, "low_ssim.jpg");

        using (var img = new MagickImage(MagickColors.Green, 100, 100))
        {
            img.AddNoise(NoiseType.Impulse);
            img.Write(file, MagickFormat.Jpg);
        }

        try
        {
            // Act
            var results = new List<ConversionResult>();
            var ic = new ImageConverter();
            ic.Quality = 1; // 極端に低クオリティにしてSSIMを下げる
            await foreach (var result in ic.ConvertDirectoryToAvifAsync(
                testDir,
                new[] { ".jpg" },
                0.999)) // 非常に高いしきい値
            {
                results.Add(result);
            }

            // Assert
            Assert.Single(results);
            var firstResult = results[0];
            Assert.False(firstResult.IsSuccess);
            Assert.Contains("SSIM too low", firstResult.ErrorMessage!);
            Assert.True(File.Exists(file), "Original file should NOT be deleted due to low SSIM.");
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }
    [Theory]
    [InlineData("avifenc_v1.2.0.exe")]
    [InlineData("avifenc_v1.3.0.exe")]
    [InlineData("avifenc_v1.4.0.exe")]
    [InlineData("avifenc_v1.4.2.exe")]
    public void ConvertToAvifWithAvifEnc_MultipleVersions_ShouldCreateAvif(string avifEncExe)
    {
        // Arrange
        const string pngPath = "test_avifenc.png";
        const string avifPath = "test_avifenc.avif";
        // テストプロジェクトのディレクトリにあるavifencを使用する
        string avifEncPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, avifEncExe);

        // テスト用PNG画像を作成 (avifencはPNGをサポート)
        using (var image = new MagickImage(MagickColors.Red, 10, 10))
        {
            image.Write(pngPath, MagickFormat.Png);
        }

        try
        {
            var ic = new ImageConverter
            {
                AvifEncPath = avifEncPath,
                Quality = 60,
                Speed = 8
            };

            // Act
            ic.ConvertToAvifWithAvifEnc(pngPath, avifPath);

            // Assert
            Assert.True(File.Exists(avifPath), $"Output AVIF file should exist for {avifEncExe}.");

            using (var avifImage = new MagickImage(avifPath))
            {
                Assert.Equal(10, (int)avifImage.Width);
                Assert.Equal(10, (int)avifImage.Height);
                Assert.Equal(MagickFormat.Avif, avifImage.Format);
            }
        }
        finally
        {
            // Cleanup
            if (File.Exists(pngPath)) File.Delete(pngPath);
            if (File.Exists(avifPath)) File.Delete(avifPath);
        }
    }

    [Fact]
    public void ConvertToAvifWithAvifEnc_WithCustomOptions_ShouldPassOptionsToAvifEnc()
    {
        // Arrange
        const string pngPath = "test_custom.png";
        const string avifPath = "test_custom.avif";
        string avifEncPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "avifenc_v1.4.2.exe");

        using (var image = new MagickImage(MagickColors.Cyan, 10, 10))
        {
            image.Write(pngPath, MagickFormat.Png);
        }

        try
        {
            var ic = new ImageConverter
            {
                AvifEncPath = avifEncPath,
                AvifEncCustomOptions = "--tilecolslog2 1 --tilerowslog2 1"
            };

            // Act
            ic.ConvertToAvifWithAvifEnc(pngPath, avifPath);

            // Assert
            Assert.True(File.Exists(avifPath), "Output AVIF file should exist.");
        }
        finally
        {
            if (File.Exists(pngPath)) File.Delete(pngPath);
            if (File.Exists(avifPath)) File.Delete(avifPath);
        }
    }

    [Fact]
    public void ConvertToAvifWithAvifEnc_WithPriority_ShouldNotThrow()
    {
        // Arrange
        const string pngPath = "test_priority.png";
        const string avifPath = "test_priority.avif";
        string avifEncPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "avifenc_v1.4.2.exe");

        using (var image = new MagickImage(MagickColors.Yellow, 10, 10))
        {
            image.Write(pngPath, MagickFormat.Png);
        }

        try
        {
            var ic = new ImageConverter
            {
                AvifEncPath = avifEncPath,
                AvifEncPriority = System.Diagnostics.ProcessPriorityClass.BelowNormal
            };

            // Act & Assert
            // 優先度の設定自体が例外を投げないことを確認しつつ、変換が成功することを確認
            var exception = Record.Exception(() => ic.ConvertToAvifWithAvifEnc(pngPath, avifPath));
            Assert.Null(exception);
            Assert.True(File.Exists(avifPath));
        }
        finally
        {
            if (File.Exists(pngPath)) File.Delete(pngPath);
            if (File.Exists(avifPath)) File.Delete(avifPath);
        }
    }

    [Fact]
    public async Task ConvertDirectoryToAvifAsync_WithAvifEnc_ShouldConvertAndDeleteOriginals()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        var file = Path.Combine(testDir, "image.png");
        string avifEncPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "avifenc_v1.4.2.exe");

        using (var img = new MagickImage(MagickColors.Red, 200, 200))
        {
            img.AddNoise(NoiseType.Gaussian);
            img.Write(file, MagickFormat.Png);
        }

        try
        {
            // Act
            var results = new List<ConversionResult>();
            var ic = new ImageConverter
            {
                AvifEncPath = avifEncPath,
                ConversionEngine = AvifConversionEngine.AvifEnc
            };
            await foreach (var result in ic.ConvertDirectoryToAvifAsync(
                testDir,
                new[] { ".png" },
                0.01))
            {
                results.Add(result);
            }

            // Assert
            Assert.Single(results);
            Assert.True(results[0].IsSuccess, results[0].ErrorMessage);
            Assert.False(File.Exists(file), "Original file should be deleted.");
            Assert.True(File.Exists(Path.ChangeExtension(file, ".avif")));
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }
}