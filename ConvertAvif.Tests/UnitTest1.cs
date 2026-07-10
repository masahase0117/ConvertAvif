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
            // Act
            ImageConverter.ConvertToAvif(bmpPath, avifPath);

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

        for (int i = 0; i < taskCount; i++)
        {
            string bmpPath = $"parallel_test_{i}.bmp";
            string avifPath = $"parallel_test_{i}.avif";
            testFiles.Add((bmpPath, avifPath));

            // テスト用BMP作成
            using var image = new MagickImage(MagickColors.Green, 10, 10);
            image.Write(bmpPath, MagickFormat.Bmp);
        }

        try
        {
            // Act
            foreach (var file in testFiles)
            {
                tasks.Add(Task.Run(() => ImageConverter.ConvertToAvif(file.Bmp, file.Avif)));
            }

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
            ImageConverter.ConvertToAvif(bmpPath, avifPath, quality: 50, colorSpace: targetColorSpace, bitDepth: targetDepth);

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
        string bmpPath = $"test_{pixelFormat}.bmp";
        string avifPath = $"test_{pixelFormat}.avif";

        using (var image = new MagickImage(MagickColors.Blue, 10, 10))
        {
            image.Write(bmpPath, MagickFormat.Bmp);
        }

        try
        {
            // Act
            ImageConverter.ConvertToAvif(bmpPath, avifPath, colorSpace: pixelFormat);

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
}
