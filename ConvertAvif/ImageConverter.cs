using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ImageMagick;

namespace ConvertAvif;

/// <summary>
///     変換処理の進捗状況を表します。
/// </summary>
public record ConversionProgress(
    int TotalFiles,
    int ProcessedFiles,
    int SuccessfulFiles,
    int FailedFiles,
    string CurrentFile);

/// <summary>
///     変換結果を表します。
/// </summary>
public record ConversionResult(string InputPath, bool IsSuccess, string? ErrorMessage);

public class ImageConverter
{
    /// <summary>
    /// クオリティ (0-100)
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public uint Quality
    {
        get;
        set
        {
            if (value > 100)
                throw new ArgumentOutOfRangeException(nameof(Quality), "Quality must be between 0 and 100.");
            field = value;
        }
    } = 75;

    /// <summary>
    /// ビット深度 (指定しない場合は元の画像の設定を使用)
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public uint? BitDepth
    {
        get;
        set
        {
            if (value != 8 && value != 10 && value != 12 && value != null)
                throw new ArgumentOutOfRangeException(nameof(BitDepth), "Bit depth must be 8, 10, or 12.");
            field = value;
        }
    } = null;

    /// <summary>
    /// 速度 (0-10)
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public uint? Speed
    {
        get;
        set
        {
            if (value > 10)
                throw new ArgumentOutOfRangeException(nameof(Speed), "Speed must be between 0 and 10.");
            field = value;
        }
    } = null;

    /// <summary>
    /// 色空間 または ピクセル形式 ("RGB", "YV12", "YUV444" など、または ColorSpace 列挙型の名前)
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public string? ColorSpace { get;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (value.Equals("YV12", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("YUV444", StringComparison.OrdinalIgnoreCase) ||
                    Enum.TryParse<ColorSpace>(value, true, out var _))
                {
                    field = value;
                }
                else
                {
                    throw new ArgumentException($"Invalid color space or pixel format: {value}", nameof(value));
                }
            }
            else
            {
                field=value;
            }
        } } = null;


    /// <summary>
    ///     各種画像をAVIF形式に変換します。
    /// </summary>
    /// <param name="inputPath">入力ファイルのパス</param>
    /// <param name="outputPath">出力AVIFファイルのパス</param>
    public void ConvertToAvif(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be null or empty.", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));

        using var image = new MagickImage(inputPath);

        if (!string.IsNullOrWhiteSpace(ColorSpace))
        {
            if (ColorSpace.Equals("YV12", StringComparison.OrdinalIgnoreCase))
            {
                image.ColorSpace = ImageMagick.ColorSpace.YUV;
                image.Settings.SetDefine(MagickFormat.Avif, "chroma-subsampling", "4:2:0");
            }
            else if (ColorSpace.Equals("YUV444", StringComparison.OrdinalIgnoreCase))
            {
                image.ColorSpace = ImageMagick.ColorSpace.YUV;
                image.Settings.SetDefine(MagickFormat.Avif, "chroma-subsampling", "4:4:4");
            }
            else if (Enum.TryParse<ColorSpace>(ColorSpace, true, out var parsedColorSpace))
            {
                image.ColorSpace = parsedColorSpace;
            }
        }

        if (BitDepth.HasValue) image.Depth = BitDepth.Value;
        if (Speed.HasValue) image.Settings.SetDefine(MagickFormat.Avif, "speed", Speed.Value.ToString());

        image.Quality = Quality;
        image.Write(outputPath, MagickFormat.Avif);
    }

    /// <summary>
    ///     指定したフォルダ内の画像をAVIF形式に一括変換します。
    /// </summary>
    /// <param name="directoryPath">対象フォルダのパス</param>
    /// <param name="extensions">対象とする拡張子 (例: ".jpg", ".png")</param>
    /// <param name="ssimThreshold">SSIMのしきい値 (0.0 - 1.0)</param>
    /// <param name="maxDegreeOfParallelism">並列実行数</param>
    /// <param name="progress">進捗通知用の IProgress インターフェース</param>
    /// <param name="ct">キャンセル申告</param>
    /// <returns>変換結果の非同期ストリーム</returns>
    public async IAsyncEnumerable<ConversionResult> ConvertDirectoryToAvifAsync(
        string directoryPath,
        string[] extensions,
        double ssimThreshold = 0.9,
        int maxDegreeOfParallelism = 4,
        IProgress<ConversionProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var totalFiles = 0;
        var processedCount = 0;
        var successCount = 0;
        var failedCount = 0;

        var fileChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            { SingleWriter = true, SingleReader = false });
        var resultChannel = Channel.CreateUnbounded<ConversionResult>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        // 探索ステージ
        var producerTask = Task.Run(async () =>
        {
            var filesToProcess = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
            foreach (var file in filesToProcess)
            {
                totalFiles++;
                await fileChannel.Writer.WriteAsync(file, ct);
            }

            fileChannel.Writer.Complete();
        }, ct);

        // 変換・検証ステージ
        var workerTasks = Enumerable.Range(0, maxDegreeOfParallelism).Select(_ => Task.Run(async () =>
        {
            await foreach (var inputFile in fileChannel.Reader.ReadAllAsync(ct))
            {
                var result = ProcessFile(inputFile, ssimThreshold, ct);
                await resultChannel.Writer.WriteAsync(result, ct);
            }
        }, ct)).ToArray();

        _ = Task.WhenAll(workerTasks).ContinueWith(_ => resultChannel.Writer.Complete(), ct);

        // 結果集約ステージ (IAsyncEnumerableとして yield return する)
        await foreach (var result in resultChannel.Reader.ReadAllAsync(ct))
        {
            processedCount++;
            if (result.IsSuccess) successCount++;
            else failedCount++;

            progress?.Report(new ConversionProgress(totalFiles, processedCount, successCount, failedCount,
                result.InputPath));

            yield return result;
        }

        await producerTask;
    }

    private ConversionResult ProcessFile(string inputPath, double ssimThreshold,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var outputPath = Path.ChangeExtension(inputPath, ".avif");

            // 1. 変換
            ConvertToAvif(inputPath, outputPath);

            // 2. 検証
            using var original = new MagickImage(inputPath);
            using var converted = new MagickImage(outputPath);

            // 正常なAVIFファイルか (MagickImageで読み込めている時点で基本OKだが、形式確認)
            if (converted.Format != MagickFormat.Avif)
                return new ConversionResult(inputPath, false, "Generated file is not in AVIF format.");

            // 縦横の画素数が同じか
            if (original.Width != converted.Width || original.Height != converted.Height)
                return new ConversionResult(inputPath, false,
                    $"Dimension mismatch: Original {original.Width}x{original.Height}, Converted {converted.Width}x{converted.Height}");

            // Exifプロファイルの確認
            var originalExif = original.GetExifProfile();
            if (originalExif != null)
            {
                var convertedExif = converted.GetExifProfile();
                if (convertedExif == null)
                    return new ConversionResult(inputPath, false, "Exif profile lost during conversion.");
            }

            // SSIMの確認
            var ssim = original.Compare(converted, ErrorMetric.StructuralSimilarity);
            if (ssim < ssimThreshold)
                return new ConversionResult(inputPath, false, $"SSIM too low: {ssim:F4} (Threshold: {ssimThreshold})");

            // ファイルサイズが小さいか
            var originalSize = new FileInfo(inputPath).Length;
            var convertedSize = new FileInfo(outputPath).Length;
            if (convertedSize >= originalSize)
                return new ConversionResult(inputPath, false,
                    $"File size increased: Original {originalSize}, Converted {convertedSize}");

            // すべて合格なら元ファイルを削除
            File.Delete(inputPath);

            return new ConversionResult(inputPath, true, null);
        }
        catch (Exception ex)
        {
            return new ConversionResult(inputPath, false, ex.Message);
        }
    }
}