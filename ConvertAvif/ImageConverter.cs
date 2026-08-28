using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Diagnostics;
using System.Text.RegularExpressions;
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

/// <summary>
///     変換エンジンの種類を表します。
/// </summary>
public enum AvifConversionEngine
{
    /// <summary>
    /// ImageMagick を使用します。
    /// </summary>
    Magick,

    /// <summary>
    /// avifenc を使用します。
    /// </summary>
    AvifEnc
}

/// <summary>
/// 画質評価に使用するエンジン
/// </summary>
public enum QualityEvaluationMode
{
    /// <summary>
    /// SSIM (Structural Similarity) を使用します。
    /// </summary>
    SSIM,

    /// <summary>
    /// SSIMULACRA2 を使用します。
    /// </summary>
    Ssimulacra2
}

public partial class ImageConverter
{
    /// <summary>
    /// 使用する変換エンジン
    /// </summary>
    public AvifConversionEngine ConversionEngine { get; set; } = AvifConversionEngine.Magick;

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
                    value.Equals("YUV400", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("400", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("420", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("422", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("444", StringComparison.OrdinalIgnoreCase) ||
                    Enum.TryParse<ColorSpace>(value, true, out _))
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
    /// avifencのパス
    /// </summary>
    public string? AvifEncPath { get; set; }

    /// <summary>
    /// avifencのカスタムオプション
    /// </summary>
    public string? AvifEncCustomOptions { get; set; }

    /// <summary>
    /// avifenc実行時のプロセス優先度
    /// </summary>
    public ProcessPriorityClass AvifEncPriority { get; set; } = ProcessPriorityClass.Idle;

    /// <summary>
    /// 画質評価モード
    /// </summary>
    public QualityEvaluationMode EvaluationMode { get; set; } = QualityEvaluationMode.SSIM;

    /// <summary>
    /// ssimulacra2.exe のパス
    /// </summary>
    public string? Ssimulacra2Path { get; set; }

    /// <summary>
    /// 画質評価のしきい値
    /// </summary>
    public double QualityThreshold { get; set; } = 0.9;


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
            else if (ColorSpace.Equals("YUV400", StringComparison.OrdinalIgnoreCase) ||
                     ColorSpace.Equals("400", StringComparison.OrdinalIgnoreCase))
            {
                image.ColorSpace = ImageMagick.ColorSpace.Gray;
                image.Settings.SetDefine(MagickFormat.Avif, "chroma-subsampling", "4:0:0");
            }
            else if (Enum.TryParse<ColorSpace>(ColorSpace, true, out var parsedColorSpace))
            {
                image.ColorSpace = parsedColorSpace;
                if (parsedColorSpace is ImageMagick.ColorSpace.Gray or ImageMagick.ColorSpace.LinearGray)
                {
                    image.Settings.SetDefine(MagickFormat.Avif, "chroma-subsampling", "4:0:0");
                }
            }
        }
        else if (IsGrayscaleImage(image))
        {
            if (image.ColorSpace != ImageMagick.ColorSpace.LinearGray)
            {
                image.ColorSpace = ImageMagick.ColorSpace.Gray;
            }
            image.Settings.SetDefine(MagickFormat.Avif, "chroma-subsampling", "4:0:0");
        }

        if (BitDepth.HasValue) image.Depth = BitDepth.Value;
        if (Speed.HasValue) image.Settings.SetDefine(MagickFormat.Avif, "speed", Speed.Value.ToString());

        image.Quality = Quality;
        if (Quality == 100)
        {
            image.Settings.SetDefine(MagickFormat.Avif, "lossless", "true");
            // Workaround for environment-specific AOM encoder issues during lossless conversion
            image.Settings.SetDefine(MagickFormat.Avif, "enable-chroma-deltaq", "false");
            image.Settings.SetDefine("heic:enable-chroma-deltaq", "false");
        }
        image.Write(outputPath, MagickFormat.Avif);
    }

    /// <summary>
    ///     avifenc を利用して各種画像をAVIF形式に変換します。
    /// </summary>
    /// <param name="inputPath">入力ファイルのパス</param>
    /// <param name="outputPath">出力AVIFファイルのパス</param>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="FileNotFoundException"></exception>
    public async Task ConvertToAvifWithAvifEnc(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be null or empty.", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be null or empty.", nameof(outputPath));
        if (string.IsNullOrWhiteSpace(AvifEncPath))
            throw new InvalidOperationException("avifenc path is not set.");
        if (!File.Exists(AvifEncPath))
            throw new FileNotFoundException("avifenc not found.", AvifEncPath);

        var version = GetAvifEncVersion(AvifEncPath);
        var arguments = BuildAvifEncArguments(version, inputPath, outputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = AvifEncPath,
            Arguments = arguments,
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start avifenc.");
        try
        {
            process.PriorityClass = AvifEncPriority;
        }
        catch (Exception ex)
        {
            // 優先度の設定に失敗しても、変換自体は続行を試みる
            Console.WriteLine($"[Warning] Failed to set process priority: {ex.Message}");
        }
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode == 0) return;
        var error = await errorTask.ConfigureAwait(false);
        throw new InvalidOperationException($"avifenc failed with exit code {process.ExitCode}. Error: {error}");
    }

    private static string GetAvifEncVersion(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start avifenc to check version.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // Version: 1.4.2 (dav1d [dec]:1.5.3-0-gb546257, aom [enc]:3.14.1)
        var match = MyRegex().Match(output);
        return match.Success ? match.Groups[1].Value : "0.0.0";
    }

    private string BuildAvifEncArguments(string version, string inputPath, string outputPath)
    {
        var args = new List<string>();

        // Version comparison
        var v = Version.Parse(version);
        var isV140OrNewer = v >= new Version(1, 4, 0);

        // Quality
        if (Quality == 100)
        {
            args.Add("-l");
        }
        else
        {
            args.Add("-q");
            args.Add(Quality.ToString());
        }

        // Speed
        if (Speed.HasValue)
        {
            args.Add("-s");
            args.Add(Speed.Value.ToString());
        }

        // Bit Depth
        if (BitDepth.HasValue && Quality < 100)
        {
            args.Add("-d");
            args.Add(BitDepth.Value.ToString());
        }

        var isGrayscale = false;
        if (!string.IsNullOrWhiteSpace(ColorSpace))
        {
            if (ColorSpace.Equals("400", StringComparison.OrdinalIgnoreCase) ||
                ColorSpace.Equals("YUV400", StringComparison.OrdinalIgnoreCase) ||
                ColorSpace.Equals("Gray", StringComparison.OrdinalIgnoreCase) ||
                ColorSpace.Equals("LinearGray", StringComparison.OrdinalIgnoreCase))
            {
                isGrayscale = true;
            }
        }
        else
        {
            try
            {
                using var testImage = new MagickImage(inputPath);
                isGrayscale = IsGrayscaleImage(testImage);
            }
            catch
            {
                // 入力画像の読み込みに失敗した場合は無視
            }
        }

        // Color Space / YUV Format
        if (!string.IsNullOrWhiteSpace(ColorSpace))
        {
            if (Quality < 100 || isGrayscale || ColorSpace.Contains("444") || ColorSpace.Equals("RGB", StringComparison.OrdinalIgnoreCase) || ColorSpace.Equals("sRGB", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("-y");
                if (ColorSpace.Equals("YV12", StringComparison.OrdinalIgnoreCase))
                {
                    args.Add("420");
                }
                else if (ColorSpace.Equals("YUV444", StringComparison.OrdinalIgnoreCase))
                {
                    args.Add("444");
                }
                else if (ColorSpace.Equals("YUV400", StringComparison.OrdinalIgnoreCase) || ColorSpace.Equals("Gray", StringComparison.OrdinalIgnoreCase) || ColorSpace.Equals("LinearGray", StringComparison.OrdinalIgnoreCase))
                {
                    args.Add("400");
                }
                else
                {
                    // avifenc accepts 444, 422, 420, 400
                    if (ColorSpace.Contains("444")) args.Add("444");
                    else if (ColorSpace.Contains("422")) args.Add("422");
                    else if (ColorSpace.Contains("420")) args.Add("420");
                    else if (ColorSpace.Contains("400")) args.Add("400");
                    else args.Add("auto");
                }
            }
        }
        else if (isGrayscale)
        {
            args.Add("-y");
            args.Add("400");
        }

        // Example of version-specific difference
        // If we wanted to use something like 'iq' tuning which was added later
        if (isV140OrNewer)
        {
            // In 1.4.0+, we might want to default to 'iq' for better quality if not specified
            // but for now we follow the issue description's focus on basic compatibility
        }

        // Custom Options
        if (!string.IsNullOrWhiteSpace(AvifEncCustomOptions))
        {
            args.Add(AvifEncCustomOptions);
        }

        args.Add($"\"{inputPath}\"");
        args.Add($"\"{outputPath}\"");

        return string.Join(" ", args);
    }

    /// <summary>
    ///     指定したフォルダ内の画像をAVIF形式に一括変換します。
    /// </summary>
    /// <param name="directoryPath">対象フォルダのパス</param>
    /// <param name="extensions">対象とする拡張子 (例: ".jpg", ".png")</param>
    /// <param name="maxDegreeOfParallelism">並列実行数</param>
    /// <param name="progress">進捗通知用の IProgress インターフェース</param>
    /// <param name="ct">キャンセル申告</param>
    /// <returns>変換結果の非同期ストリーム</returns>
    public async IAsyncEnumerable<ConversionResult> ConvertDirectoryToAvifAsync(
        string directoryPath,
        string[] extensions,
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

        // 探索ステージ (先に全ファイルをリストアップして総数を確定)
        var filesToProcess = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();
        totalFiles = filesToProcess.Count;

        var producerTask = Task.Run(async () =>
        {
            foreach (var file in filesToProcess)
            {
                await fileChannel.Writer.WriteAsync(file, ct).ConfigureAwait(false);
            }

            fileChannel.Writer.Complete();
        }, ct);

        // 変換・検証ステージ
        var workerTasks = Enumerable.Range(0, maxDegreeOfParallelism).Select(_ => Task.Run(async () =>
        {
            await foreach (var inputFile in fileChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var result = ProcessFile(inputFile, ConversionEngine, ct);
                await resultChannel.Writer.WriteAsync(result, ct).ConfigureAwait(false);
            }
        }, ct)).ToArray();

        _ = Task.WhenAll(workerTasks).ContinueWith(_ => resultChannel.Writer.Complete(), ct);

        // 結果集約ステージ (IAsyncEnumerableとして yield return する)
        await foreach (var result in resultChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            processedCount++;
            if (result.IsSuccess) successCount++;
            else failedCount++;

            progress?.Report(new ConversionProgress(totalFiles, processedCount, successCount, failedCount,
                result.InputPath));

            yield return result;
        }

        await producerTask.ConfigureAwait(false);
    }

    private ConversionResult ProcessFile(string inputPath,
        AvifConversionEngine engine,
        CancellationToken ct)
    {
        var outputPath = Path.ChangeExtension(inputPath, ".avif");
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            return new ConversionResult(inputPath, false, "Input and output file paths are identical.");
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            // 1. 変換
            if (engine == AvifConversionEngine.AvifEnc)
            {
                try
                {
                    ConvertToAvifWithAvifEnc(inputPath, outputPath).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // avifenc が失敗した場合は Magick.NET にフォールバック
                    Console.WriteLine($"[Info] avifenc failed, falling back to Magick.NET: {ex.Message}");
                    ConvertToAvif(inputPath, outputPath);
                }
            }
            else
            {
                ConvertToAvif(inputPath, outputPath);
            }

            // 2. 検証
            string? validationError = null;
            var originalBytes = File.ReadAllBytes(inputPath);
            var convertedBytes = File.ReadAllBytes(outputPath);

            using (var original = new MagickImage(originalBytes))
            using (var converted = new MagickImage(convertedBytes))
            {
                // 正常なAVIFファイルか (MagickImageで読み込めている時点で基本OKだが、形式確認)
                if (converted.Format != MagickFormat.Avif)
                {
                    validationError = "Generated file is not in AVIF format.";
                }
                // 縦横の画素数が同じか
                else if (original.Width != converted.Width || original.Height != converted.Height)
                {
                    validationError =
                        $"Dimension mismatch: Original {original.Width}x{original.Height}, Converted {converted.Width}x{converted.Height}";
                }
                // Exifプロファイルの確認
                else if (original.GetExifProfile() != null && converted.GetExifProfile() == null)
                {
                    validationError = "Exif profile lost during conversion.";
                }
                // 画質の確認
                // Qualityが100の場合はロスレスのため画質の確認をスキップする
                else if (Quality < 100)
                {
                    if (EvaluationMode == QualityEvaluationMode.Ssimulacra2)
                    {
                        var score = GetSsimulacra2Score(inputPath, converted);
                        if (score < QualityThreshold)
                        {
                            validationError = $"SSIMULACRA2 too low: {score:F4} (Threshold: {QualityThreshold})";
                        }
                    }
                    else
                    {
                        // SSIMの確認 (Magick.NETのSSIMは不一致度を返すため 1.0 から引いて類似度にする)
                        var ssim = 1.0 - original.Compare(converted, ErrorMetric.StructuralSimilarity);
                        if (ssim < QualityThreshold)
                        {
                            validationError = $"SSIM too low: {ssim:F4} (Threshold: {QualityThreshold})";
                        }
                    }
                }
            }

            if (validationError != null)
            {
                DeleteOutputFile(outputPath);
                return new ConversionResult(inputPath, false, validationError);
            }

            // ファイルサイズが小さいか
            var originalSize = new FileInfo(inputPath).Length;
            var convertedSize = new FileInfo(outputPath).Length;
            if (convertedSize >= originalSize)
            {
                DeleteOutputFile(outputPath);
                return new ConversionResult(inputPath, false,
                    $"File size increased: Original {originalSize}, Converted {convertedSize}");
            }

            // すべて合格なら元ファイルを削除
            DeleteOutputFile(inputPath);

            return new ConversionResult(inputPath, true, null);
        }
        catch (Exception ex)
        {
            DeleteOutputFile(outputPath);
            return new ConversionResult(inputPath, false, ex.Message);
        }
    }

    private double GetSsimulacra2Score(string originalPath, MagickImage convertedImage)
    {
        var exePath = Ssimulacra2Path ?? "ssimulacra2.exe";
        var tmpPath = Path.Combine(Path.GetTempPath(), $"tmp_{Guid.NewGuid():N}.png");
        using (var img = convertedImage.Clone())
        {
            img.ColorSpace = ImageMagick.ColorSpace.sRGB; // 色空間を sRGB に強制変換
            img.Alpha(AlphaOption.Remove); // アルファチャンネルを削除
            img.RemoveProfile("icc"); // ICC プロファイルを除去（必要なら有効化）
            img.Format = MagickFormat.Png;
            img.Write(tmpPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"\"{originalPath}\" \"{tmpPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var process = Process.Start(psi) ??
                                throw new InvalidOperationException("Failed to start ssimulacra2.exe");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();


            if (process.ExitCode != 0)
            {
                throw new Exception($"ssimulacra2.exe failed with exit code {process.ExitCode}: {error}");
            }

            return double.TryParse(output.Trim(), out var score)
                ? score
                : throw new Exception($"Failed to parse ssimulacra2.exe output: {output}");
        }
        finally
        {
            DeleteOutputFile(tmpPath);
        }
    }

    /// <summary>
    ///     画像がグレイスケール画像かどうかを判定します。
    /// </summary>
    /// <param name="image">判定対象の画像</param>
    /// <returns>グレイスケール画像の場合は true</returns>
    private static bool IsGrayscaleImage(MagickImage image)
    {
        if (image.ColorSpace is ImageMagick.ColorSpace.Gray or ImageMagick.ColorSpace.LinearGray)
            return true;
        if (image.ColorType is ColorType.Grayscale or ColorType.GrayscaleAlpha or ColorType.Bilevel)
            return true;
        var detected = image.DetermineColorType();
        return detected is ColorType.Grayscale or ColorType.GrayscaleAlpha or ColorType.Bilevel;
    }

    /// <summary>
    ///     出力ファイルを安全に削除します。一時的なファイルロックに対応するためリトライを行います。
    /// </summary>
    /// <param name="path">削除対象のファイルパス</param>
    private static void DeleteOutputFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        const int maxRetries = 10;
        const int delayMs = 100;

        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (i == maxRetries - 1)
                {
                    Console.WriteLine($"[Warning] 出力ファイルの削除に失敗しました: {path} ({ex.Message})");
                }
                else
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(delayMs);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] 出力ファイルの削除中に予期しないエラーが発生しました: {path} ({ex.Message})");
                return;
            }
        }
    }

    [GeneratedRegex(@"Version:\s*(\d+\.\d+\.\d+)")]
    private static partial Regex MyRegex();
}