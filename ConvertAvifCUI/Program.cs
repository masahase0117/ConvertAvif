using System.CommandLine;
using ConvertAvif;

namespace ConvertAvifCUI;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("画像をAVIF形式に一括変換します。");

        var dirArgument = new Argument<DirectoryInfo>(
            name: "directory",
            description: "対象とするディレクトリのパス");

        var extensionsOption = new Option<string[]>(
            name: "--extensions",
            description: "対象とする拡張子 (例: .jpg .png)",
            getDefaultValue: () => new[] { ".jpg", ".jpeg", ".png" });
        extensionsOption.AddAlias("-e");

        var qualityOption = new Option<uint>(
            name: "--quality",
            description: "変換品質 (0-100)",
            getDefaultValue: () => 75u);
        qualityOption.AddAlias("-q");

        var ssimOption = new Option<double>(
            name: "--ssim",
            description: "SSIMのしきい値 (0.0-1.0)。この値未満の場合は元のファイルを削除しません。",
            getDefaultValue: () => 0.9);
        ssimOption.AddAlias("-s");

        var parallelOption = new Option<int>(
            name: "--parallel",
            description: "並列実行数",
            getDefaultValue: () => 4);
        parallelOption.AddAlias("-p");

        var speedOption = new Option<uint?>(
            name: "--speed",
            description: "エンコード速度 (0-10)");

        var depthOption = new Option<uint?>(
            name: "--depth",
            description: "ビット深度 (8, 10, 12)");

        rootCommand.AddArgument(dirArgument);
        rootCommand.AddOption(extensionsOption);
        rootCommand.AddOption(qualityOption);
        rootCommand.AddOption(ssimOption);
        rootCommand.AddOption(parallelOption);
        rootCommand.AddOption(speedOption);
        rootCommand.AddOption(depthOption);

        rootCommand.SetHandler(async (dir, extensions, quality, ssim, parallel, speed, depth) =>
        {
            var converter = new ImageConverter
            {
                Quality = quality,
                Speed = speed,
                BitDepth = depth
            };

            Console.WriteLine($"ディレクトリ: {dir.FullName}");
            Console.WriteLine($"拡張子: {string.Join(", ", extensions)}");
            Console.WriteLine($"クオリティ: {quality}");
            Console.WriteLine($"SSIMしきい値: {ssim}");
            Console.WriteLine("変換を開始します...");

            var progress = new Progress<ConversionProgress>(p =>
            {
                // シンプルな進捗表示
                Console.Write($"\r進捗: {p.ProcessedFiles}/{p.TotalFiles} (成功: {p.SuccessfulFiles}, 失敗: {p.FailedFiles}) 現在: {Path.GetFileName(p.CurrentFile)}".PadRight(Console.WindowWidth - 1));
            });

            await foreach (var result in converter.ConvertDirectoryToAvifAsync(
                dir.FullName, 
                extensions, 
                ssimThreshold: ssim, 
                maxDegreeOfParallelism: parallel,
                progress: progress))
            {
                if (!result.IsSuccess)
                {
                    Console.WriteLine($"\n[失敗] {result.InputPath}: {result.ErrorMessage}");
                }
            }

            Console.WriteLine("\n完了しました。");
        }, dirArgument, extensionsOption, qualityOption, ssimOption, parallelOption, speedOption, depthOption);

        return await rootCommand.InvokeAsync(args);
    }
}
