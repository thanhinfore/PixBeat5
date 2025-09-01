using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using PixBeat5.Models;
using SkiaSharp;
using System.IO;
using System.Text.Json;

namespace PixBeat5.Services;

public interface IRenderService
{
    Task<string> RenderVideoAsync(ProjectData project, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default);
}

public class RenderService : IRenderService
{
    private readonly ILogger<RenderService> _logger;

    public RenderService(ILogger<RenderService> logger)
    {
        _logger = logger;
    }

    public async Task<string> RenderVideoAsync(ProjectData project, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (project.Audio == null)
            throw new ArgumentException("Project must have audio data");

        _logger.LogInformation("Starting video render for project: {ProjectName}", project.Name);

        var tempDir = Path.Combine(Path.GetTempPath(), "pixbeat_render", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Load template
            var template = await LoadTemplateAsync(project.Template);

            // Calculate frame count
            var totalFrames = (int)(project.Settings.Duration.TotalSeconds * project.Settings.Fps);
            var renderProgress = new RenderProgress { TotalFrames = totalFrames, Stage = "Preparing" };

            // Generate frames
            var frameFiles = await GenerateFramesAsync(project, template, tempDir, renderProgress, progress, cancellationToken);

            // Render video with FFmpeg
            renderProgress.Stage = "Encoding Video";
            progress?.Report(renderProgress);

            var outputPath = await EncodeVideoAsync(frameFiles, project.Audio.FilePath, project.Settings, cancellationToken);

            _logger.LogInformation("Video render complete: {OutputPath}", outputPath);
            return outputPath;
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temp directory: {TempDir}", tempDir);
            }
        }
    }

    private async Task<TemplateInfo> LoadTemplateAsync(string templateName)
    {
        var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates", templateName, "template.json");

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Template not found: {templateName}");
        }

        var json = await File.ReadAllTextAsync(templatePath);
        var template = JsonSerializer.Deserialize<TemplateInfo>(json);
        return template ?? throw new InvalidOperationException($"Failed to load template: {templateName}");
    }

    private async Task<string[]> GenerateFramesAsync(
        ProjectData project,
        TemplateInfo template,
        string outputDir,
        RenderProgress renderProgress,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken)
    {
        var frameFiles = new string[renderProgress.TotalFrames];
        var startTime = DateTime.Now;

        await Task.Run(() =>
        {
            Parallel.For(0, renderProgress.TotalFrames, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, frameIndex =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frameTime = frameIndex / (double)project.Settings.Fps;
                var frameFile = Path.Combine(outputDir, $"frame_{frameIndex:D6}.png");

                // Generate frame using SkiaSharp
                GenerateSingleFrame(project, template, frameTime, frameFile);

                frameFiles[frameIndex] = frameFile;

                // Update progress
                if (frameIndex % 10 == 0)
                {
                    renderProgress.CurrentFrame = frameIndex;
                    renderProgress.Elapsed = DateTime.Now - startTime;
                    if (frameIndex > 0)
                    {
                        var avgTimePerFrame = renderProgress.Elapsed.TotalMilliseconds / frameIndex;
                        var remainingFrames = renderProgress.TotalFrames - frameIndex;
                        renderProgress.Estimated = TimeSpan.FromMilliseconds(remainingFrames * avgTimePerFrame);
                    }
                    progress?.Report(renderProgress);
                }
            });
        }, cancellationToken);

        return frameFiles;
    }

    private void GenerateSingleFrame(ProjectData project, TemplateInfo template, double frameTime, string outputPath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(project.Settings.Width, project.Settings.Height));
        var canvas = surface.Canvas;

        // Clear background
        canvas.Clear(SKColors.Black);

        // Simple template rendering based on template type
        switch (template.Id)
        {
            case "pixel_runner":
                DrawPixelRunner(canvas, project, frameTime);
                break;
            case "equalizer":
                DrawEqualizer(canvas, project, frameTime);
                break;
            case "waveform":
                DrawWaveform(canvas, project, frameTime);
                break;
            default:
                DrawSimpleVisualizer(canvas, project, frameTime);
                break;
        }

        // Add watermark
        if (!string.IsNullOrEmpty(project.Settings.Watermark))
        {
            DrawWatermark(canvas, project.Settings.Watermark, project.Settings);
        }

        // Save frame
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outputPath, data.ToArray());
    }

    private void DrawPixelRunner(SKCanvas canvas, ProjectData project, double frameTime)
    {
        var audio = project.Audio!;
        var settings = project.Settings;

        // Background gradient
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, settings.Height),
            new[] { SKColor.Parse("#1a1a2e"), SKColor.Parse("#16213e") },
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = gradient };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);

        // Ground
        using var groundPaint = new SKPaint { Color = SKColor.Parse("#0f3460") };
        var groundY = settings.Height - 100;
        canvas.DrawRect(0, groundY, settings.Width, 100, groundPaint);

        // Runner (simple rectangle that "jumps" on beats)
        var runnerX = settings.Width * 0.2f;
        var runnerY = groundY - 60;

        // Check if current time is near a beat
        var nearestBeat = audio.BeatTimes.OrderBy(bt => Math.Abs(bt - frameTime)).FirstOrDefault();
        if (Math.Abs(nearestBeat - frameTime) < 0.1) // Within 100ms of beat
        {
            runnerY -= 20; // Jump effect
        }

        using var runnerPaint = new SKPaint { Color = SKColor.Parse("#e94560") };
        canvas.DrawRect(runnerX, runnerY, 40, 60, runnerPaint);

        // Energy visualization as background elements
        var energyIndex = (int)(frameTime * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
        if (energyIndex < audio.EnergyLevels.Length)
        {
            var energy = audio.EnergyLevels[energyIndex];
            var alpha = (byte)(energy * 255 * 0.3); // Max 30% opacity
            using var energyPaint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
            canvas.DrawRect(0, 0, settings.Width, settings.Height, energyPaint);
        }
    }

    private void DrawEqualizer(SKCanvas canvas, ProjectData project, double frameTime)
    {
        var audio = project.Audio!;
        var settings = project.Settings;

        // Dark background
        canvas.Clear(SKColor.Parse("#0a0a0a"));

        // Get current energy
        var energyIndex = (int)(frameTime * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
        var currentEnergy = energyIndex < audio.EnergyLevels.Length ? audio.EnergyLevels[energyIndex] : 0;

        // Draw equalizer bars
        var barCount = 20;
        var barWidth = settings.Width / (float)barCount;

        using var barPaint = new SKPaint();

        for (int i = 0; i < barCount; i++)
        {
            var barHeight = (float)(currentEnergy * settings.Height * 0.8 * (0.5 + 0.5 * Math.Sin(frameTime * 2 + i)));
            var x = i * barWidth;
            var y = settings.Height - barHeight;

            // Color based on frequency (low = red, high = blue)
            var hue = i / (float)barCount * 300; // 0-300 degrees
            barPaint.Color = SKColor.FromHsl(hue, 100, 50);

            canvas.DrawRect(x + 2, y, barWidth - 4, barHeight, barPaint);
        }
    }

    private void DrawWaveform(SKCanvas canvas, ProjectData project, double frameTime)
    {
        var audio = project.Audio!;
        var settings = project.Settings;

        // Gradient background
        using var gradient = SKShader.CreateRadialGradient(
            new SKPoint(settings.Width / 2, settings.Height / 2), settings.Width / 2,
            new[] { SKColor.Parse("#2d1b69"), SKColor.Parse("#11002e") },
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = gradient };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);

        // Draw waveform
        using var waveformPath = new SKPath();
        using var waveformPaint = new SKPaint
        {
            Color = SKColor.Parse("#f39c12"),
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        var centerY = settings.Height / 2f;
        var waveformWidth = settings.Width * 0.8f;
        var startX = settings.Width * 0.1f;

        for (int i = 0; i < waveformWidth; i++)
        {
            var time = frameTime + (i - waveformWidth / 2) * 0.01; // Show wave around current time
            if (time >= 0 && time < audio.Duration.TotalSeconds)
            {
                var energyIndex = (int)(time * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
                var energy = energyIndex < audio.EnergyLevels.Length ? audio.EnergyLevels[energyIndex] : 0;

                var y = centerY + (float)(energy * 100 * Math.Sin(time * 10));
                var x = startX + i;

                if (i == 0)
                    waveformPath.MoveTo(x, y);
                else
                    waveformPath.LineTo(x, y);
            }
        }

        canvas.DrawPath(waveformPath, waveformPaint);

        // Current time indicator
        using var indicatorPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2 };
        var indicatorX = startX + waveformWidth / 2;
        canvas.DrawLine(indicatorX, 0, indicatorX, settings.Height, indicatorPaint);
    }

    private void DrawSimpleVisualizer(SKCanvas canvas, ProjectData project, double frameTime)
    {
        var audio = project.Audio!;
        var settings = project.Settings;

        // Simple pulsing circle based on energy
        canvas.Clear(SKColors.Black);

        var energyIndex = (int)(frameTime * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
        var currentEnergy = energyIndex < audio.EnergyLevels.Length ? audio.EnergyLevels[energyIndex] : 0;

        var centerX = settings.Width / 2f;
        var centerY = settings.Height / 2f;
        var radius = (float)(50 + currentEnergy * 200);

        using var circlePaint = new SKPaint
        {
            Color = SKColor.FromHsl((float)(frameTime * 60) % 360, 80, 60),
            Style = SKPaintStyle.Fill
        };

        canvas.DrawCircle(centerX, centerY, radius, circlePaint);
    }

    private void DrawWatermark(SKCanvas canvas, string watermark, VideoSettings settings)
    {
        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(150),
            TextSize = 24,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        var textBounds = new SKRect();
        textPaint.MeasureText(watermark, ref textBounds);

        var x = settings.Width - textBounds.Width - 20;
        var y = settings.Height - 30;

        canvas.DrawText(watermark, x, y, textPaint);
    }

    private async Task<string> EncodeVideoAsync(string[] frameFiles, string audioPath, VideoSettings settings, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            $"pixbeat_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
        );

        var framePattern = Path.Combine(Path.GetDirectoryName(frameFiles[0])!, "frame_%06d.png");

        await FFMpegArguments
            .FromFileInput(framePattern, false, options => options.WithFramerate(settings.Fps))
            .AddFileInput(audioPath)
            .OutputToFile(outputPath, true, options =>
            {
                options
                    .WithVideoCodec(VideoCodec.LibX264)
                    .WithAudioCodec(AudioCodec.Aac)
                    .WithVariableBitrate(4)
                    .WithVideoFilters(filterOptions => filterOptions.Scale(settings.Width, settings.Height))
                    .WithFastStart()
                    .WithArgument(new Argument("-pix_fmt yuv420p"))
                    .WithArgument(new Argument("-shortest"));

                // Quality settings
                switch (settings.Quality)
                {
                    case "Draft":
                        options.WithArgument(new Argument("-crf 28"));
                        break;
                    case "High":
                        options.WithArgument(new Argument("-crf 18"));
                        break;
                    default: // Standard
                        options.WithArgument(new Argument("-crf 23"));
                        break;
                }
            })
            .ProcessAsynchronously(true, cancellationToken);

        return outputPath;
    }
}