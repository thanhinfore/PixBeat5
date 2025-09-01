using System.Text.Json.Serialization;

namespace PixBeat5.Models;

public class AudioData
{
    public string FilePath { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public double Tempo { get; set; }
    public double[] BeatTimes { get; set; } = Array.Empty<double>();
    public double[] EnergyLevels { get; set; } = Array.Empty<double>();
    public string Genre { get; set; } = "Unknown";
    public string Key { get; set; } = "C";
    public string Mode { get; set; } = "major";
    public string Mood { get; set; } = "Neutral";
    public double Confidence { get; set; } = 0.0;
    public DateTime AnalyzedAt { get; set; } = DateTime.Now;
}

public class ProjectData
{
    public string Name { get; set; } = "New Project";
    public string Template { get; set; } = "pixel_runner";
    public AudioData? Audio { get; set; }
    public VideoSettings Settings { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string OutputPath { get; set; } = "";
}

public class VideoSettings
{
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1920;
    public int Fps { get; set; } = 30;
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(30);
    public string Quality { get; set; } = "Standard";
    public string Watermark { get; set; } = "LuyenAI.vn";
}

public class TemplateInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string PreviewImage { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public Dictionary<string, TemplateParameter> Parameters { get; set; } = new();
}

public class TemplateParameter
{
    public string Type { get; set; } = "float";
    public object DefaultValue { get; set; } = 1.0;
    public object? MinValue { get; set; }
    public object? MaxValue { get; set; }
    public string Description { get; set; } = "";
}

public class RenderProgress
{
    public int CurrentFrame { get; set; }
    public int TotalFrames { get; set; }
    public string Stage { get; set; } = "";
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Estimated { get; set; }
    public double Percentage => TotalFrames > 0 ? (double)CurrentFrame / TotalFrames * 100 : 0;
}