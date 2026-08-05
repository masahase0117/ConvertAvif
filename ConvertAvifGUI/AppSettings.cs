namespace ConvertAvifGUI;

public class AppSettings
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string Extensions { get; set; } = ".jpg,.jpeg,.png";
    public uint Quality { get; set; } = 75;
    public double QualityThreshold { get; set; } = 0.9;
    public string EvaluationMode { get; set; } = "SSIM";
    public string? Ssimulacra2Path { get; set; }
    public int MaxDegreeOfParallelism { get; set; } = 4;
    public string ConversionEngine { get; set; } = "Magick";
    public string? AvifEncPath { get; set; }
    public string? AvifEncCustomOptions { get; set; }
}
