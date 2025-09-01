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
    private readonly Random _random = new();

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
            var template = await LoadTemplateAsync(project.Template);
            var totalFrames = (int)(project.Settings.Duration.TotalSeconds * project.Settings.Fps);
            var renderProgress = new RenderProgress { TotalFrames = totalFrames, Stage = "Generating Frames" };

            var frameFiles = await GenerateFramesAsync(project, template, tempDir, renderProgress, progress, cancellationToken);

            renderProgress.Stage = "Encoding Video";
            progress?.Report(renderProgress);

            var outputPath = await EncodeVideoAsync(frameFiles, project.Audio.FilePath, project.Settings, cancellationToken);

            _logger.LogInformation("Video render complete: {OutputPath}", outputPath);
            return outputPath;
        }
        finally
        {
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
            return new TemplateInfo
            {
                Id = templateName,
                Name = templateName,
                Description = "Default template"
            };
        }

        try
        {
            var json = await File.ReadAllTextAsync(templatePath);
            var template = JsonSerializer.Deserialize<TemplateInfo>(json);
            return template ?? new TemplateInfo { Id = templateName, Name = templateName };
        }
        catch
        {
            return new TemplateInfo { Id = templateName, Name = templateName };
        }
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

        // Pre-calculate some animation data
        var animationState = new AnimationState
        {
            Stars = GenerateStars(100),
            Particles = new List<Particle>(),
            Buildings = GenerateBuildings(15)
        };

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

                GenerateSingleFrame(project, template, frameTime, frameFile, animationState, frameIndex);

                frameFiles[frameIndex] = frameFile;

                if (frameIndex % 10 == 0)
                {
                    renderProgress.CurrentFrame = frameIndex;
                    renderProgress.Stage = $"Rendering Frame {frameIndex}/{renderProgress.TotalFrames}";
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

    private void GenerateSingleFrame(ProjectData project, TemplateInfo template, double frameTime,
        string outputPath, AnimationState animState, int frameIndex)
    {
        using var surface = SKSurface.Create(new SKImageInfo(project.Settings.Width, project.Settings.Height));
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.Black);

        switch (template.Id)
        {
            case "pixel_runner":
                DrawEnhancedPixelRunner(canvas, project, frameTime, animState, frameIndex);
                break;
            case "equalizer":
                DrawEnhancedEqualizer(canvas, project, frameTime, frameIndex);
                break;
            case "waveform":
                DrawEnhancedWaveform(canvas, project, frameTime, frameIndex);
                break;
            default:
                DrawEnhancedPixelRunner(canvas, project, frameTime, animState, frameIndex);
                break;
        }

        if (!string.IsNullOrEmpty(project.Settings.Watermark))
        {
            DrawWatermark(canvas, project.Settings.Watermark, project.Settings);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outputPath, data.ToArray());
    }

    private void DrawEnhancedPixelRunner(SKCanvas canvas, ProjectData project, double frameTime,
    AnimationState animState, int frameIndex)
    {
        var audio = project.Audio!;
        var settings = project.Settings;

        // 1. Animated gradient background
        var color1 = InterpolateColor(SKColor.Parse("#0a0e27"), SKColor.Parse("#1a1a3e"),
            (float)(Math.Sin(frameTime * 0.2) * 0.5 + 0.5));
        var color2 = InterpolateColor(SKColor.Parse("#16213e"), SKColor.Parse("#2a2a5e"),
            (float)(Math.Sin(frameTime * 0.15) * 0.5 + 0.5));

        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, settings.Height),
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = gradient };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);

        // 2. Stars in background
        DrawStars(canvas, animState.Stars, frameTime, settings);

        // 3. City skyline silhouette
        DrawCityscape(canvas, animState.Buildings, settings, frameTime);

        // 4. Ground with texture
        var groundY = settings.Height - 120;
        using var groundGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, groundY), new SKPoint(0, settings.Height),
            new[] { SKColor.Parse("#0f3460"), SKColor.Parse("#051e3e") },
            SKShaderTileMode.Clamp);
        using var groundPaint = new SKPaint { Shader = groundGradient };
        canvas.DrawRect(0, groundY, settings.Width, 120, groundPaint);

        // Grid lines on ground
        using var gridPaint = new SKPaint
        {
            Color = SKColor.Parse("#1a4a7a").WithAlpha(100),
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke
        };
        for (int i = 0; i < 10; i++)
        {
            var y = groundY + i * 15;
            canvas.DrawLine(0, y, settings.Width, y, gridPaint);
        }

        // 5. Runner character with animation - FIX: Explicit float declarations
        float runnerX = settings.Width * 0.25f;
        float baseY = groundY - 80;
        float runnerY = baseY;  // Explicitly declare as float

        // Check if we're on a beat
        bool onBeat = false;
        float beatStrength = 0;
        if (audio.BeatTimes.Length > 0)
        {
            var nearestBeat = audio.BeatTimes.OrderBy(bt => Math.Abs(bt - frameTime)).FirstOrDefault();
            var beatDistance = Math.Abs(nearestBeat - frameTime);
            if (beatDistance < 0.15)
            {
                onBeat = true;
                beatStrength = 1f - (float)(beatDistance / 0.15);
                runnerY = baseY - 40 * beatStrength;  // Now this works since runnerY is float
            }
        }

        // Draw runner with pixel art style
        DrawPixelCharacter(canvas, runnerX, runnerY, frameIndex, onBeat, beatStrength);

        // 6. Energy particles
        if (audio.EnergyLevels.Length > 0)
        {
            var energyIndex = (int)(frameTime * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
            if (energyIndex < audio.EnergyLevels.Length)
            {
                var energy = audio.EnergyLevels[energyIndex];
                DrawEnergyParticles(canvas, settings, energy, frameTime, onBeat);
            }
        }

        // 7. Speed lines when jumping
        if (onBeat && beatStrength > 0.3f)
        {
            DrawSpeedLines(canvas, settings, beatStrength);
        }

        // 8. Score/Info display
        DrawInfoPanel(canvas, audio, frameTime, settings);
    }

    private void DrawEnhancedEqualizer(SKCanvas canvas, ProjectData project, double frameTime, int frameIndex)
    {
        var audio = project.Audio!;
        var settings = project.Settings;

        // Dark background with subtle gradient
        using var bgGradient = SKShader.CreateRadialGradient(
            new SKPoint(settings.Width / 2, settings.Height / 2),
            Math.Max(settings.Width, settings.Height),
            new[] { SKColor.Parse("#0a0a0a"), SKColor.Parse("#1a1a2e") },
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = bgGradient };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);

        // Grid background
        DrawGrid(canvas, settings, frameTime);

        double currentEnergy = 0;
        if (audio.EnergyLevels.Length > 0)
        {
            var energyIndex = (int)(frameTime * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
            currentEnergy = energyIndex < audio.EnergyLevels.Length ? audio.EnergyLevels[energyIndex] : 0;
        }

        // Enhanced equalizer bars
        var barCount = 32;
        var barWidth = settings.Width / (float)barCount;
        var maxHeight = settings.Height * 0.7f;

        for (int i = 0; i < barCount; i++)
        {
            // Create frequency-like animation
            var frequency = (i + 1) / (float)barCount;
            var phase = frameTime * (2 + i * 0.1) + i * 0.2;
            var wave = Math.Sin(phase) * 0.3 + 0.7;

            var barHeight = (float)(currentEnergy * maxHeight * wave * (0.5 + 0.5 * frequency));
            barHeight = Math.Max(20, barHeight); // Minimum height

            var x = i * barWidth + 2;
            var y = settings.Height - barHeight - 50;
            var width = barWidth - 4;

            // FIX: Calculate hue properly as float
            var hueValue = (i / (float)barCount * 300 + frameTime * 30) % 360;
            var hue = (float)hueValue;
            var topColor = SKColor.FromHsl(hue, 80f, 60f);
            var bottomColor = SKColor.FromHsl(hue, 100f, 40f);

            using var barGradient = SKShader.CreateLinearGradient(
                new SKPoint(x, y), new SKPoint(x, y + barHeight),
                new[] { topColor, bottomColor },
                SKShaderTileMode.Clamp);
            using var barPaint = new SKPaint { Shader = barGradient };

            // Draw bar with rounded corners
            using var barPath = new SKPath();
            barPath.AddRoundRect(new SKRoundRect(new SKRect(x, y, x + width, y + barHeight), 2, 2));
            canvas.DrawPath(barPath, barPaint);

            // Add glow effect for high bars
            if (barHeight > maxHeight * 0.6f)
            {
                using var glowPaint = new SKPaint
                {
                    Color = topColor.WithAlpha(50),
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5)
                };
                canvas.DrawPath(barPath, glowPaint);
            }

            // Draw reflection
            using var reflectionPaint = new SKPaint
            {
                Shader = barGradient,
                Color = SKColors.White.WithAlpha(30)
            };
            var reflectionHeight = barHeight * 0.3f;
            canvas.DrawRect(x, settings.Height - 50 + 5, width, reflectionHeight, reflectionPaint);
        }

        // Add visualizer info
        DrawVisualizerInfo(canvas, audio, frameTime, settings);
    }

    private void DrawEnhancedWaveform(SKCanvas canvas, ProjectData project, double frameTime, int frameIndex)
    {
        var audio = project.Audio!;
        var settings = project.Settings;

        // Dark background with animated gradient
        var hue1 = (float)((frameTime * 10) % 360);
        var hue2 = (float)((frameTime * 10 + 180) % 360);

        using var bgGradient = SKShader.CreateRadialGradient(
            new SKPoint(settings.Width / 2, settings.Height / 2),
            Math.Max(settings.Width, settings.Height),
            new[] {
            SKColor.FromHsl(hue1, 20f, 10f),
            SKColor.FromHsl(hue2, 30f, 5f)
            },
            SKShaderTileMode.Clamp);
        using var bgPaint = new SKPaint { Shader = bgGradient };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);

        // Circular elements in background
        DrawCircularVisualizer(canvas, settings, audio, frameTime);

        var centerY = settings.Height / 2f;
        var waveformHeight = settings.Height * 0.4f;

        if (audio.EnergyLevels.Length > 0)
        {
            // Main waveform
            using var waveformPath = new SKPath();
            using var mirrorPath = new SKPath();

            var points = 200;
            for (int i = 0; i < points; i++)
            {
                var x = (i / (float)(points - 1)) * settings.Width;
                var time = frameTime + (i - points / 2) * 0.02;

                if (time >= 0 && time < audio.Duration.TotalSeconds)
                {
                    var energyIndex = (int)(time * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
                    var energy = energyIndex < audio.EnergyLevels.Length ? audio.EnergyLevels[energyIndex] : 0;

                    // Add some wave modulation
                    var wave1 = Math.Sin(time * 10 + i * 0.1) * 0.3;
                    var wave2 = Math.Sin(time * 5 + i * 0.05) * 0.2;
                    var amplitude = energy * waveformHeight * (1 + wave1 + wave2);

                    var y = centerY + (float)amplitude;
                    var mirrorY = centerY - (float)amplitude;

                    if (i == 0)
                    {
                        waveformPath.MoveTo(x, y);
                        mirrorPath.MoveTo(x, mirrorY);
                    }
                    else
                    {
                        waveformPath.LineTo(x, y);
                        mirrorPath.LineTo(x, mirrorY);
                    }
                }
            }

            // Draw main waveform with glow - FIX
            var waveHue = (float)((frameTime * 30) % 360);
            var waveColor = SKColor.FromHsl(waveHue, 80f, 60f);

            using var glowPaint = new SKPaint
            {
                Color = waveColor.WithAlpha(100),
                StrokeWidth = 8,
                Style = SKPaintStyle.Stroke,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4),
                IsAntialias = true
            };
            canvas.DrawPath(waveformPath, glowPaint);
            canvas.DrawPath(mirrorPath, glowPaint);

            using var waveformPaint = new SKPaint
            {
                Color = waveColor,
                StrokeWidth = 3,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawPath(waveformPath, waveformPaint);
            canvas.DrawPath(mirrorPath, waveformPaint);

            // Fill area under waveform
            waveformPath.LineTo(settings.Width, centerY);
            waveformPath.LineTo(0, centerY);
            waveformPath.Close();

            using var fillPaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, centerY),
                    new SKPoint(0, settings.Height),
                    new[] { waveColor.WithAlpha(50), SKColors.Transparent },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawPath(waveformPath, fillPaint);
        }

        // Center line
        using var centerLinePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(50),
            StrokeWidth = 1
        };
        canvas.DrawLine(0, centerY, settings.Width, centerY, centerLinePaint);

        // Time indicator - FIX
        var indicatorX = settings.Width / 2;
        using var indicatorPaint = new SKPaint
        {
            Color = SKColors.White,
            StrokeWidth = 2,
            PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, (float)frameIndex * 0.5f)
        };
        canvas.DrawLine(indicatorX, 0, indicatorX, settings.Height, indicatorPaint);
    }

    // Helper methods for enhanced visuals

    private void DrawPixelCharacter(SKCanvas canvas, float x, float y, int frame, bool onBeat, float beatStrength)
    {
        var pixelSize = 4;

        // Simple pixel art character (8x12 pixels scaled up)
        int[,] character = new int[,] {
            {0,0,1,1,1,1,0,0}, // Head
            {0,1,2,2,2,2,1,0},
            {0,1,2,3,3,2,1,0}, // Face
            {0,1,2,2,2,2,1,0},
            {0,0,1,1,1,1,0,0},
            {0,4,4,4,4,4,4,0}, // Body
            {4,4,4,4,4,4,4,4},
            {4,4,4,4,4,4,4,4},
            {0,4,4,4,4,4,4,0},
            {0,5,5,0,0,5,5,0}, // Legs (animated)
            {0,5,5,0,0,5,5,0},
            {0,6,6,0,0,6,6,0}  // Feet
        };

        // Animate legs based on frame
        var runFrame = (frame / 5) % 4;
        if (runFrame == 1 || runFrame == 3)
        {
            character[9, 1] = 0; character[9, 2] = 0;
            character[9, 5] = 5; character[9, 6] = 5;
        }

        var colors = new SKColor[] {
            SKColors.Transparent,
            SKColor.Parse("#8B4513"), // Brown (outline)
            SKColor.Parse("#FDBCB4"), // Skin
            SKColor.Parse("#000000"), // Eyes
            SKColor.Parse("#FF0000"), // Shirt
            SKColor.Parse("#0000FF"), // Pants
            SKColor.Parse("#654321")  // Shoes
        };

        // Add glow when on beat
        if (onBeat && beatStrength > 0.5f)
        {
            using var glowPaint = new SKPaint
            {
                Color = SKColors.Yellow.WithAlpha((byte)(beatStrength * 100)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10)
            };
            canvas.DrawRect(x - 20, y - 20, 8 * pixelSize + 40, 12 * pixelSize + 40, glowPaint);
        }

        // Draw character pixels
        for (int py = 0; py < 12; py++)
        {
            for (int px = 0; px < 8; px++)
            {
                var colorIndex = character[py, px];
                if (colorIndex > 0)
                {
                    using var paint = new SKPaint { Color = colors[colorIndex] };
                    canvas.DrawRect(x + px * pixelSize, y + py * pixelSize, pixelSize, pixelSize, paint);
                }
            }
        }
    }

    private void DrawStars(SKCanvas canvas, List<Star> stars, double frameTime, VideoSettings settings)
    {
        using var starPaint = new SKPaint { Color = SKColors.White };

        foreach (var star in stars)
        {
            var twinkle = (float)(Math.Sin(frameTime * star.TwinkleSpeed + star.Phase) * 0.5 + 0.5);
            starPaint.Color = SKColors.White.WithAlpha((byte)(twinkle * 255 * star.Brightness));
            canvas.DrawCircle(star.X * settings.Width, star.Y * settings.Height, star.Size, starPaint);
        }
    }

    private void DrawCityscape(SKCanvas canvas, List<Building> buildings, VideoSettings settings, double frameTime)
    {
        using var buildingPaint = new SKPaint { Color = SKColor.Parse("#0a1929") };

        foreach (var building in buildings)
        {
            var x = building.X * settings.Width;
            var height = building.Height * settings.Height * 0.4f;
            var y = settings.Height - 120 - height;
            var width = building.Width * settings.Width;

            canvas.DrawRect(x, y, width, height, buildingPaint);

            // Windows with lights
            using var windowPaint = new SKPaint { Color = SKColors.Yellow.WithAlpha(150) };
            for (int wy = 0; wy < building.WindowRows; wy++)
            {
                for (int wx = 0; wx < building.WindowCols; wx++)
                {
                    if (_random.NextDouble() > 0.3) // Some windows are lit
                    {
                        var windowX = x + 5 + wx * 15;
                        var windowY = y + 10 + wy * 20;
                        canvas.DrawRect(windowX, windowY, 8, 12, windowPaint);
                    }
                }
            }
        }
    }

    private void DrawEnergyParticles(SKCanvas canvas, VideoSettings settings, double energy, double frameTime, bool onBeat)
    {
        var particleCount = (int)(energy * 20);
        using var particlePaint = new SKPaint();

        for (int i = 0; i < particleCount; i++)
        {
            var angle = i * Math.PI * 2 / particleCount + frameTime;
            var radius = 100 + energy * 200 + Math.Sin(frameTime * 3 + i) * 20;
            var x = settings.Width / 2 + (float)(Math.Cos(angle) * radius);
            var y = settings.Height / 2 + (float)(Math.Sin(angle) * radius);

            var hue = (float)((i * 10 + frameTime * 60) % 360);
            particlePaint.Color = SKColor.FromHsl(hue, 100f, 60f).WithAlpha((byte)(energy * 255));

            var size = onBeat ? 6 : 3;
            canvas.DrawCircle(x, y, size, particlePaint);
        }
    }

    private void DrawSpeedLines(SKCanvas canvas, VideoSettings settings, float intensity)
    {
        using var linePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)(intensity * 100)),
            StrokeWidth = 2,
            PathEffect = SKPathEffect.CreateDash(new float[] { 20, 10 }, 0)
        };

        var centerY = settings.Height / 2;
        for (int i = 0; i < 5; i++)
        {
            var y = centerY + (i - 2) * 50;
            canvas.DrawLine(0, y, settings.Width, y, linePaint);
        }
    }

    private void DrawInfoPanel(SKCanvas canvas, AudioData audio, double frameTime, VideoSettings settings)
    {
        using var panelPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(150)
        };
        canvas.DrawRect(10, 10, 250, 80, panelPaint);

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        canvas.DrawText($"BPM: {audio.Tempo:F1}", 20, 35, textPaint);
        canvas.DrawText($"Genre: {audio.Genre}", 20, 55, textPaint);
        canvas.DrawText($"Time: {TimeSpan.FromSeconds(frameTime):mm\\:ss}", 20, 75, textPaint);
    }

    private void DrawGrid(SKCanvas canvas, VideoSettings settings, double frameTime)
    {
        using var gridPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(20),
            StrokeWidth = 1
        };

        var gridSize = 50;
        var offset = (float)(frameTime * 10 % gridSize);

        for (int x = 0; x < settings.Width; x += gridSize)
        {
            canvas.DrawLine(x + offset, 0, x + offset, settings.Height, gridPaint);
        }

        for (int y = 0; y < settings.Height; y += gridSize)
        {
            canvas.DrawLine(0, y, settings.Width, y, gridPaint);
        }
    }

    private void DrawCircularVisualizer(SKCanvas canvas, VideoSettings settings, AudioData audio, double frameTime)
    {
        var centerX = settings.Width / 2f;
        var centerY = settings.Height / 2f;

        double energy = 0;
        if (audio.EnergyLevels.Length > 0)
        {
            var energyIndex = (int)(frameTime * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
            energy = energyIndex < audio.EnergyLevels.Length ? audio.EnergyLevels[energyIndex] : 0;
        }

        using var circlePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        for (int i = 0; i < 5; i++)
        {
            var radius = 50 + i * 30 + (float)(energy * 100 * Math.Sin(frameTime * 2 + i));
            var alpha = (byte)(255 - i * 40);
            circlePaint.Color = SKColors.Cyan.WithAlpha(alpha);
            canvas.DrawCircle(centerX, centerY, radius, circlePaint);
        }
    }

    private void DrawVisualizerInfo(SKCanvas canvas, AudioData audio, double frameTime, VideoSettings settings)
    {
        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(200),
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        var info = $"{audio.Genre} • {audio.Tempo:F0} BPM • {audio.Key} {audio.Mode}";
        var textBounds = new SKRect();
        textPaint.MeasureText(info, ref textBounds);

        var x = (settings.Width - textBounds.Width) / 2;
        canvas.DrawText(info, x, settings.Height - 20, textPaint);
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

    // Helper methods
    private SKColor InterpolateColor(SKColor color1, SKColor color2, float t)
    {
        color1.ToHsl(out float h1, out float s1, out float l1);
        color2.ToHsl(out float h2, out float s2, out float l2);

        var h = h1 + (h2 - h1) * t;
        var s = s1 + (s2 - s1) * t;
        var l = l1 + (l2 - l1) * t;

        return SKColor.FromHsl(h, s, l);
    }

    private List<Star> GenerateStars(int count)
    {
        var stars = new List<Star>();
        for (int i = 0; i < count; i++)
        {
            stars.Add(new Star
            {
                X = (float)_random.NextDouble(),
                Y = (float)_random.NextDouble() * 0.5f,
                Size = (float)(_random.NextDouble() * 2 + 0.5),
                Brightness = (float)(_random.NextDouble() * 0.5 + 0.5),
                TwinkleSpeed = (float)(_random.NextDouble() * 2 + 1),
                Phase = (float)(_random.NextDouble() * Math.PI * 2)
            });
        }
        return stars;
    }

    private List<Building> GenerateBuildings(int count)
    {
        var buildings = new List<Building>();
        var x = 0f;

        for (int i = 0; i < count; i++)
        {
            var building = new Building
            {
                X = x,
                Width = (float)(_random.NextDouble() * 0.08 + 0.04),
                Height = (float)(_random.NextDouble() * 0.5 + 0.3),
                WindowRows = _random.Next(3, 8),
                WindowCols = _random.Next(2, 5)
            };
            buildings.Add(building);
            x += building.Width + 0.01f;
        }

        return buildings;
    }

    private async Task<string> EncodeVideoAsync(string[] frameFiles, string audioPath, VideoSettings settings, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            $"pixbeat_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
        );

        var framePattern = Path.Combine(Path.GetDirectoryName(frameFiles[0])!, "frame_%06d.png");

        try
        {
            await FFMpegArguments
                .FromFileInput(framePattern, false, options => options
                    .WithFramerate(settings.Fps))
                .AddFileInput(audioPath)
                .OutputToFile(outputPath, true, options =>
                {
                    options.WithVideoCodec(VideoCodec.LibX264)
                           .WithAudioCodec(AudioCodec.Aac)
                           .WithVariableBitrate(4)
                           .WithFastStart();

                    if (settings.Quality == "Draft")
                        options.WithCustomArgument("-crf 28");
                    else if (settings.Quality == "High")
                        options.WithCustomArgument("-crf 18");
                    else
                        options.WithCustomArgument("-crf 23");

                    options.WithCustomArgument("-pix_fmt yuv420p")
                           .WithCustomArgument("-shortest");
                })
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg encoding failed");
            throw new InvalidOperationException($"Video encoding failed: {ex.Message}", ex);
        }
    }

    // Helper classes
    private class AnimationState
    {
        public List<Star> Stars { get; set; } = new();
        public List<Particle> Particles { get; set; } = new();
        public List<Building> Buildings { get; set; } = new();
    }

    private class Star
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Size { get; set; }
        public float Brightness { get; set; }
        public float TwinkleSpeed { get; set; }
        public float Phase { get; set; }
    }

    private class Particle
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public float Life { get; set; }
        public SKColor Color { get; set; }
    }

    private class Building
    {
        public float X { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int WindowRows { get; set; }
        public int WindowCols { get; set; }
    }
}