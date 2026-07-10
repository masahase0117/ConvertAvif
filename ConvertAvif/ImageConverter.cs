using ImageMagick;

namespace ConvertAvif;

public static class ImageConverter
{
    /// <summary>
    /// 各種画像をAVIF形式に変換します。
    /// </summary>
    /// <param name="inputPath">入力ファイルのパス</param>
    /// <param name="outputPath">出力AVIFファイルのパス</param>
    /// <param name="quality">クオリティ (0-100)</param>
    /// <param name="colorSpace">色空間 または ピクセル形式 ("RGB", "YV12", "YUV444" など、または ColorSpace 列挙型の名前)</param>
    /// <param name="bitDepth">ビット深度 (指定しない場合は元の画像の設定を使用)</param>
    /// <param name="speed">速度 (0-10)</param>
    public static void ConvertToAvif(string inputPath, string outputPath, int quality = 75, string? colorSpace = null, int? bitDepth = null, int? speed = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be null or empty.", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));

        using var image = new MagickImage(inputPath);
        
        if (!string.IsNullOrWhiteSpace(colorSpace))
        {
            if (colorSpace.Equals("YV12", StringComparison.OrdinalIgnoreCase))
            {
                image.ColorSpace = ColorSpace.YUV;
                image.Settings.SetDefine(MagickFormat.Avif, "chroma-subsampling", "4:2:0");
            }
            else if (colorSpace.Equals("YUV444", StringComparison.OrdinalIgnoreCase))
            {
                image.ColorSpace = ColorSpace.YUV;
                image.Settings.SetDefine(MagickFormat.Avif, "chroma-subsampling", "4:4:4");
            }
            else if (Enum.TryParse<ColorSpace>(colorSpace, true, out var parsedColorSpace))
            {
                image.ColorSpace = parsedColorSpace;
            }
            else
            {
                throw new ArgumentException($"Invalid color space or pixel format: {colorSpace}", nameof(colorSpace));
            }
        }

        if (bitDepth.HasValue)
        {
            image.Depth = (uint)bitDepth.Value;
        }
        if (speed.HasValue)
        {
            image.Settings.SetDefine(MagickFormat.Avif, "speed", speed.Value.ToString());
        }

        image.Quality = (uint)quality;
        image.Write(outputPath, MagickFormat.Avif);
    }
}
