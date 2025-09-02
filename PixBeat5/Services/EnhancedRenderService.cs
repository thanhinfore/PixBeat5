using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using PixBeat5.Models;
using SkiaSharp;
using System.IO;
using System.Text.Json;

namespace PixBeat5.Services;

public class EnhancedRenderService : IRenderService
{
    private readonly ILogger<EnhancedRenderService> _logger;
    private readonly Random _random = new();

    // Mood color palettes
    private readonly Dictionary<string, ColorPalette> _moodPalettes = new()
    {
        ["Energetic"] = new ColorPalette
        {
            Primary = new[] { "#FF006E", "#FB5607", "#FFBE0B" },
            Secondary = new[] { "#8338EC", "#3A86FF", "#06FFB4" },
            Background = new[] { "#1A0033", "#330033", "#4D0033" }
        },
        ["Happy"] = new ColorPalette
        {
            Primary = new[] { "#FFD60A", "#FFC300", "#FF9A00" },
            Secondary = new[] { "#FF5400", "#C77DFF", "#7209B7" },
            Background = new[] { "#003566", "#001D3D", "#000814" }
        },
        ["Calm"] = new ColorPalette
        {
            Primary = new[] { "#CAF0F8", "#ADE8F4", "#90E0EF" },
            Secondary = new[] { "#48CAE4", "#00B4D8", "#0096C7" },
            Background = new[] { "#03045E", "#023047", "#012A4A" }
        },
        ["Neutral"] = new ColorPalette
        {
            Primary = new[] { "#F8F9FA", "#E9ECEF", "#DEE2E6" },
            Secondary = new[] { "#ADB5BD", "#6C757D", "#495057" },
            Background = new[] { "#212529", "#1C1E21", "#161719" }
        }
    };

    // Section detection thresholds
    private class MusicSection
    {
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public string Type { get; set; } = "verse"; // intro, verse, chorus, bridge, outro
        public double Intensity { get; set; }
    }

    public EnhancedRenderService(ILogger<EnhancedRenderService> logger)
    {
        _logger = logger;
    }

    public async Task<string> RenderVideoAsync(ProjectData project, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (project.Audio == null)
            throw new ArgumentException("Project must have audio data");

        _logger.LogInformation("Starting enhanced video render for project: {ProjectName}", project.Name);

        var tempDir = Path.Combine(Path.GetTempPath(), "pixbeat_render", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Detect music sections
            var sections = DetectMusicSections(project.Audio);

            // Load template and color palette
            var template = await LoadTemplateAsync(project.Template);
            var palette = _moodPalettes.GetValueOrDefault(project.Audio.Mood, _moodPalettes["Neutral"]);

            // Calculate frame requirements
            var totalFrames = (int)(project.Settings.Duration.TotalSeconds * project.Settings.Fps);
            var renderProgress = new RenderProgress { TotalFrames = totalFrames, Stage = "Generating Frames" };

            // Generate animation state
            var animState = new EnhancedAnimationState
            {
                Palette = palette,
                Sections = sections,
                BeatGrid = GenerateBeatGrid(project.Audio, project.Settings.Duration),
                Stars = GenerateStars(150),
                Particles = new List<Particle>(),
                Buildings = GenerateBuildings(20),
                PixelCharacters = GeneratePixelCharacters(5)
            };

            // Generate frames
            var frameFiles = await GenerateEnhancedFramesAsync(
                project, template, tempDir, renderProgress, progress, animState, cancellationToken);

            // Encode video
            renderProgress.Stage = "Encoding Video";
            progress?.Report(renderProgress);

            var outputPath = await EncodeVideoAsync(frameFiles, project.Audio.FilePath, project.Settings, cancellationToken);

            _logger.LogInformation("Enhanced video render complete: {OutputPath}", outputPath);
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

    private List<MusicSection> DetectMusicSections(AudioData audio)
    {
        var sections = new List<MusicSection>();
        var duration = audio.Duration.TotalSeconds;

        // Intro (0-10% or first 5 seconds)
        var introEnd = Math.Min(5, duration * 0.1);
        sections.Add(new MusicSection
        {
            StartTime = 0,
            EndTime = introEnd,
            Type = "intro",
            Intensity = 0.3
        });

        // Analyze energy levels for section detection
        if (audio.EnergyLevels.Length > 0)
        {
            var avgEnergy = audio.EnergyLevels.Average();
            var maxEnergy = audio.EnergyLevels.Max();
            var threshold = avgEnergy + (maxEnergy - avgEnergy) * 0.3;

            var currentSection = "verse";
            var sectionStart = introEnd;

            for (int i = 1; i < audio.EnergyLevels.Length; i++)
            {
                var time = i * duration / audio.EnergyLevels.Length;
                var prevEnergy = audio.EnergyLevels[i - 1];
                var currEnergy = audio.EnergyLevels[i];

                // Detect energy jumps for chorus
                if (prevEnergy < threshold && currEnergy >= threshold)
                {
                    // End previous section
                    sections.Add(new MusicSection
                    {
                        StartTime = sectionStart,
                        EndTime = time,
                        Type = currentSection,
                        Intensity = prevEnergy / maxEnergy
                    });

                    // Start chorus
                    currentSection = "chorus";
                    sectionStart = time;
                }
                else if (prevEnergy >= threshold && currEnergy < threshold)
                {
                    // End chorus
                    sections.Add(new MusicSection
                    {
                        StartTime = sectionStart,
                        EndTime = time,
                        Type = currentSection,
                        Intensity = prevEnergy / maxEnergy
                    });

                    // Start verse/bridge
                    currentSection = sections.Count % 3 == 0 ? "bridge" : "verse";
                    sectionStart = time;
                }
            }

            // Add final section
            if (sectionStart < duration - 5)
            {
                sections.Add(new MusicSection
                {
                    StartTime = sectionStart,
                    EndTime = duration - 5,
                    Type = currentSection,
                    Intensity = audio.EnergyLevels.Last() / maxEnergy
                });
            }
        }

        // Outro (last 5 seconds or 10%)
        var outroStart = Math.Max(duration - 5, duration * 0.9);
        sections.Add(new MusicSection
        {
            StartTime = outroStart,
            EndTime = duration,
            Type = "outro",
            Intensity = 0.2
        });

        return sections;
    }

    private async Task<string[]> GenerateEnhancedFramesAsync(
        ProjectData project,
        TemplateInfo template,
        string outputDir,
        RenderProgress renderProgress,
        IProgress<RenderProgress>? progress,
        EnhancedAnimationState animState,
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

                GenerateEnhancedFrame(project, template, frameTime, frameFile, animState, frameIndex);

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

    private void GenerateEnhancedFrame(
        ProjectData project,
        TemplateInfo template,
        double frameTime,
        string outputPath,
        EnhancedAnimationState animState,
        int frameIndex)
    {
        using var surface = SKSurface.Create(new SKImageInfo(project.Settings.Width, project.Settings.Height));
        var canvas = surface.Canvas;

        // Get current section
        var currentSection = animState.Sections.FirstOrDefault(s => frameTime >= s.StartTime && frameTime < s.EndTime)
            ?? new MusicSection { Type = "verse", Intensity = 0.5 };

        // Check if on beat
        var beatInfo = GetBeatInfo(frameTime, animState.BeatGrid);

        // Clear with section-appropriate background
        DrawEnhancedBackground(canvas, project.Settings, currentSection, animState.Palette, frameTime);

        // Draw based on template and section
        switch (template.Id)
        {
            case "pixel_runner":
                DrawPixelRunnerEnhanced(canvas, project, frameTime, animState, currentSection, beatInfo, frameIndex);
                break;
            case "equalizer":
                DrawEqualizerEnhanced(canvas, project, frameTime, animState, currentSection, beatInfo, frameIndex);
                break;
            case "waveform":
                DrawWaveformEnhanced(canvas, project, frameTime, animState, currentSection, beatInfo, frameIndex);
                break;
            default:
                DrawPixelRunnerEnhanced(canvas, project, frameTime, animState, currentSection, beatInfo, frameIndex);
                break;
        }

        // Add section-specific overlays
        DrawSectionOverlays(canvas, project.Settings, currentSection, frameTime, beatInfo);

        // Add intro/outro elements
        if (currentSection.Type == "intro")
        {
            DrawIntro(canvas, project, frameTime, currentSection);
        }
        else if (currentSection.Type == "outro")
        {
            DrawOutro(canvas, project, frameTime, currentSection);
        }

        // Watermark
        if (!string.IsNullOrEmpty(project.Settings.Watermark))
        {
            DrawEnhancedWatermark(canvas, project.Settings.Watermark, project.Settings, frameTime);
        }

        // Save frame
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outputPath, data.ToArray());
    }

    private void DrawPixelRunnerEnhanced(
        SKCanvas canvas,
        ProjectData project,
        double frameTime,
        EnhancedAnimationState animState,
        MusicSection section,
        BeatInfo beatInfo,
        int frameIndex)
    {
        var settings = project.Settings;
        var audio = project.Audio!;

        // Draw cityscape with parallax
        DrawParallaxCityscape(canvas, animState.Buildings, settings, frameTime, section.Intensity);

        // Ground with grid
        DrawAnimatedGround(canvas, settings, frameTime, animState.Palette, beatInfo.OnBeat);

        // Multiple pixel characters based on section
        var characterCount = section.Type == "chorus" ? 5 : section.Type == "bridge" ? 3 : 1;

        for (int i = 0; i < Math.Min(characterCount, animState.PixelCharacters.Count); i++)
        {
            var character = animState.PixelCharacters[i];
            var xOffset = settings.Width * (0.2f + i * 0.15f);
            var yBase = (float)(settings.Height - 150 - i * 20);  // Explicit cast to float

            // Jump on beat with varying heights  
            var jumpHeight = beatInfo.OnBeat ? (float)(60 * beatInfo.Strength * (1 + i * 0.2f)) : 0f;
            var y = yBase - jumpHeight;

            // Draw with section-specific colors
            var characterColor = GetSectionColor(animState.Palette, section.Type, i);
            DrawEnhancedPixelCharacter(canvas, xOffset, y, frameIndex, beatInfo, characterColor, character.Type);
        }

        // Energy particles that follow the beat
        DrawBeatSyncedParticles(canvas, settings, beatInfo, section, animState.Palette);

        // Speed lines for high intensity
        if (section.Intensity > 0.7)
        {
            DrawIntensitySpeedLines(canvas, settings, section.Intensity, frameTime);
        }

        // HUD with song info
        DrawEnhancedHUD(canvas, audio, frameTime, settings, section);
    }

    private void DrawEqualizerEnhanced(
        SKCanvas canvas,
        ProjectData project,
        double frameTime,
        EnhancedAnimationState animState,
        MusicSection section,
        BeatInfo beatInfo,
        int frameIndex)
    {
        var settings = project.Settings;
        var audio = project.Audio!;

        // Grid background with beat pulse
        DrawPulsingGrid(canvas, settings, frameTime, beatInfo);

        // Enhanced equalizer bars
        var barCount = section.Type == "chorus" ? 64 : 32;
        var barWidth = settings.Width / (float)barCount;
        var maxHeight = settings.Height * 0.8f;

        // Get current energy
        var currentEnergy = GetEnergyAtTime(audio, frameTime);

        for (int i = 0; i < barCount; i++)
        {
            // Create varied frequency response
            var frequency = (i + 1) / (float)barCount;
            var phase = frameTime * (3 + i * 0.15) + i * 0.3;
            var wave = Math.Sin(phase) * 0.4 + 0.6;

            // Beat sync enhancement
            var beatMultiplier = beatInfo.OnBeat ? 1.5f : 1.0f;

            // Section-based height modulation
            var sectionMultiplier = section.Type == "chorus" ? 1.3f :
                                   section.Type == "bridge" ? 0.9f : 1.0f;

            var barHeight = (float)(currentEnergy * maxHeight * wave * beatMultiplier * sectionMultiplier);
            barHeight = Math.Max(30, barHeight);

            var x = i * barWidth + 2;
            var y = (settings.Height - barHeight) / 2; // Center vertically
            var width = barWidth - 4;

            // Color based on section and frequency
            var hue = GetEqualizerHue(section.Type, frequency, frameTime);
            var saturation = 80f + beatInfo.Strength * 20f;
            var lightness = 50f + (float)(section.Intensity * 20f);

            var topColor = SKColor.FromHsl(hue, saturation, lightness);
            var bottomColor = SKColor.FromHsl(hue, 100f, (float)Math.Max(20.0, lightness - 30f));

            // Draw bar with gradient
            using var barGradient = SKShader.CreateLinearGradient(
                new SKPoint(x, y),
                new SKPoint(x, y + barHeight),
                new[] { topColor, bottomColor },
                SKShaderTileMode.Clamp);

            using var barPaint = new SKPaint { Shader = barGradient };

            // Rounded rectangle for bar
            using var barPath = new SKPath();
            barPath.AddRoundRect(new SKRoundRect(
                new SKRect(x, y, x + width, y + barHeight), 3, 3));
            canvas.DrawPath(barPath, barPaint);

            // Glow effect for high bars
            if (barHeight > maxHeight * 0.7f)
            {
                using var glowPaint = new SKPaint
                {
                    Color = topColor.WithAlpha(60),
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8)
                };
                canvas.DrawPath(barPath, glowPaint);
            }

            // Mirror reflection
            using var reflectionPaint = new SKPaint
            {
                Shader = barGradient,
                Color = SKColors.White.WithAlpha(20)
            };
            var reflectionHeight = barHeight * 0.4f;
            var reflectionY = y + barHeight + 5;
            canvas.DrawRect(x, reflectionY, width, reflectionHeight, reflectionPaint);
        }

        // Add spectrum analyzer overlay
        DrawSpectrumAnalyzer(canvas, settings, audio, frameTime, section);
    }

    private void DrawWaveformEnhanced(
        SKCanvas canvas,
        ProjectData project,
        double frameTime,
        EnhancedAnimationState animState,
        MusicSection section,
        BeatInfo beatInfo,
        int frameIndex)
    {
        var settings = project.Settings;
        var audio = project.Audio!;

        // Animated circular background elements
        DrawCircularPulse(canvas, settings, frameTime, beatInfo, animState.Palette);

        var centerY = settings.Height / 2f;
        var waveformHeight = settings.Height * 0.5f * section.Intensity;

        // Main waveform with enhanced styling
        using var waveformPath = new SKPath();
        using var mirrorPath = new SKPath();

        var points = 300;
        var timeWindow = 3.0; // seconds of waveform to show

        for (int i = 0; i < points; i++)
        {
            var x = (i / (float)(points - 1)) * settings.Width;
            var time = frameTime - timeWindow / 2 + (i / (float)points) * timeWindow;

            if (time >= 0 && time < audio.Duration.TotalSeconds)
            {
                var energy = GetEnergyAtTime(audio, time);

                // Complex wave modulation
                var wave1 = Math.Sin(time * 8 + i * 0.1) * 0.2;
                var wave2 = Math.Cos(time * 4 + i * 0.05) * 0.15;
                var wave3 = Math.Sin(time * 12) * 0.1;

                // Beat enhancement
                var beatEnhance = beatInfo.OnBeat && Math.Abs(time - frameTime) < 0.1 ?
                    beatInfo.Strength * 0.5 : 0;

                var amplitude = energy * waveformHeight * (1 + wave1 + wave2 + wave3 + beatEnhance);

                var y = centerY + (float)amplitude;
                var mirrorY = centerY - (float)amplitude;

                if (i == 0)
                {
                    waveformPath.MoveTo(x, y);
                    mirrorPath.MoveTo(x, mirrorY);
                }
                else
                {
                    // Smooth curves
                    var prevX = ((i - 1) / (float)(points - 1)) * settings.Width;
                    var midX = (prevX + x) / 2;
                    waveformPath.CubicTo(midX, y, midX, y, x, y);
                    mirrorPath.CubicTo(midX, mirrorY, midX, mirrorY, x, mirrorY);
                }
            }
        }

        // Section-based coloring
        var waveColor = GetWaveformColor(animState.Palette, section.Type, frameTime);

        // Multi-layer glow effect
        for (int layer = 3; layer >= 0; layer--)
        {
            var alpha = (byte)(30 + layer * 20);
            var blur = 10 - layer * 2;

            using var glowPaint = new SKPaint
            {
                Color = waveColor.WithAlpha(alpha),
                StrokeWidth = 10 - layer * 2,
                Style = SKPaintStyle.Stroke,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur),
                IsAntialias = true
            };

            canvas.DrawPath(waveformPath, glowPaint);
            canvas.DrawPath(mirrorPath, glowPaint);
        }

        // Main waveform line
        using var waveformPaint = new SKPaint
        {
            Color = waveColor,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawPath(waveformPath, waveformPaint);
        canvas.DrawPath(mirrorPath, waveformPaint);

        // Fill gradient
        waveformPath.LineTo(settings.Width, centerY);
        waveformPath.LineTo(0, centerY);
        waveformPath.Close();

        using var fillPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, centerY),
                new SKPoint(0, settings.Height),
                new[] { waveColor.WithAlpha(40), SKColors.Transparent },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawPath(waveformPath, fillPaint);

        // Time indicator with beat pulse
        DrawTimeIndicator(canvas, settings, frameTime, beatInfo, waveColor);
    }

    // Helper methods for enhanced rendering

    private void DrawEnhancedBackground(SKCanvas canvas, VideoSettings settings, MusicSection section, ColorPalette palette, double frameTime)
    {
        var bgColors = palette.Background.Select(c => SKColor.Parse(c)).ToArray();

        // Animated gradient based on section
        var color1Index = ((int)(frameTime * 0.1)) % bgColors.Length;
        var color2Index = (color1Index + 1) % bgColors.Length;

        var t = (float)((frameTime * 0.1) % 1.0);
        var color1 = InterpolateColor(bgColors[color1Index], bgColors[color2Index], t);
        var color2 = InterpolateColor(bgColors[(color2Index + 1) % bgColors.Length], bgColors[color1Index], t);

        // Radial gradient for chorus, linear for others
        if (section.Type == "chorus")
        {
            using var gradient = SKShader.CreateRadialGradient(
                new SKPoint(settings.Width / 2, settings.Height / 2),
                Math.Max(settings.Width, settings.Height) * 0.8f,
                new[] { color1, color2 },
                SKShaderTileMode.Clamp);
            using var bgPaint = new SKPaint { Shader = gradient };
            canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);
        }
        else
        {
            using var gradient = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(settings.Width, settings.Height),
                new[] { color1, color2 },
                SKShaderTileMode.Clamp);
            using var bgPaint = new SKPaint { Shader = gradient };
            canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);
        }
    }

    private void DrawIntro(SKCanvas canvas, ProjectData project, double frameTime, MusicSection section)
    {
        var settings = project.Settings;
        var progress = (frameTime - section.StartTime) / (section.EndTime - section.StartTime);

        // Fade in effect
        var alpha = (byte)(255 * Math.Min(1, progress * 2));

        // Logo or title
        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(alpha),
            TextSize = 48,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        var text = "PixBeat";
        var textBounds = new SKRect();
        textPaint.MeasureText(text, ref textBounds);

        var x = (settings.Width - textBounds.Width) / 2;
        var y = settings.Height / 2;

        // Pixel art style text with glow
        using var glowPaint = new SKPaint
        {
            Color = SKColors.Cyan.WithAlpha((byte)(alpha / 2)),
            TextSize = 48,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10),
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        canvas.DrawText(text, x, y, glowPaint);
        canvas.DrawText(text, x, y, textPaint);

        // Subtitle
        using var subtitlePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)(alpha * 0.7)),
            TextSize = 20,
            IsAntialias = true
        };

        var subtitle = "Music Visualizer";
        textPaint.MeasureText(subtitle, ref textBounds);
        x = (settings.Width - textBounds.Width) / 2 + 50;
        canvas.DrawText(subtitle, x, y + 40, subtitlePaint);
    }

    private void DrawOutro(SKCanvas canvas, ProjectData project, double frameTime, MusicSection section)
    {
        var settings = project.Settings;
        var progress = (frameTime - section.StartTime) / (section.EndTime - section.StartTime);

        // Fade out effect
        var alpha = (byte)(255 * (1 - progress));

        // Thanks message
        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(alpha),
            TextSize = 36,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        var text = "Thanks for watching!";
        var textBounds = new SKRect();
        textPaint.MeasureText(text, ref textBounds);

        var x = (settings.Width - textBounds.Width) / 2;
        var y = settings.Height / 2;

        canvas.DrawText(text, x, y, textPaint);

        // Fade to black overlay
        using var fadePaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha((byte)(255 * progress * 0.8))
        };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, fadePaint);
    }

    private void DrawEnhancedPixelCharacter(SKCanvas canvas, float x, float y, int frame, BeatInfo beatInfo, SKColor tintColor, string type)
    {
        var pixelSize = 5;

        // Different character designs based on type
        int[,] character = type switch
        {
            "dancer" => GetDancerPixels(frame),
            "jumper" => GetJumperPixels(frame),
            _ => GetRunnerPixels(frame)
        };

        // Beat glow effect
        if (beatInfo.OnBeat && beatInfo.Strength > 0.5f)
        {
            using var glowPaint = new SKPaint
            {
                Color = tintColor.WithAlpha((byte)(beatInfo.Strength * 150)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 15)
            };

            var glowSize = 15 * pixelSize;
            canvas.DrawRect(x - 30, y - 30, glowSize + 60, glowSize + 60, glowPaint);
        }

        // Draw character pixels with tint
        var colors = GetCharacterColors(tintColor);

        for (int py = 0; py < character.GetLength(0); py++)
        {
            for (int px = 0; px < character.GetLength(1); px++)
            {
                var colorIndex = character[py, px];
                if (colorIndex > 0 && colorIndex < colors.Length)
                {
                    using var paint = new SKPaint
                    {
                        Color = colors[colorIndex],
                        IsAntialias = false // Keep pixel art sharp
                    };

                    canvas.DrawRect(x + px * pixelSize, y + py * pixelSize, pixelSize - 1, pixelSize - 1, paint);
                }
            }
        }
    }

    private int[,] GetRunnerPixels(int frame)
    {
        // Animated runner with 4 frames
        var runFrame = (frame / 6) % 4;

        // Base character
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
            {0,5,5,0,0,5,5,0}, // Legs frame 0
            {0,5,5,0,0,5,5,0},
            {0,6,6,0,0,6,6,0}  // Feet
        };

        // Animate legs
        switch (runFrame)
        {
            case 1:
                character[9, 1] = 0; character[9, 2] = 0;
                character[9, 5] = 5; character[9, 6] = 5;
                character[10, 1] = 5; character[10, 2] = 5;
                character[10, 5] = 0; character[10, 6] = 0;
                break;
            case 2:
                character[9, 3] = 5; character[9, 4] = 5;
                break;
            case 3:
                character[9, 5] = 0; character[9, 6] = 0;
                character[9, 1] = 5; character[9, 2] = 5;
                character[10, 5] = 5; character[10, 6] = 5;
                character[10, 1] = 0; character[10, 2] = 0;
                break;
        }

        return character;
    }

    private int[,] GetDancerPixels(int frame)
    {
        var danceFrame = (frame / 8) % 4;

        int[,] character = new int[,] {
            {0,0,1,1,1,1,0,0},
            {0,1,2,2,2,2,1,0},
            {0,1,2,3,3,2,1,0},
            {0,1,2,2,2,2,1,0},
            {0,0,1,1,1,1,0,0},
            {7,4,4,4,4,4,4,7}, // Arms extended
            {0,4,4,4,4,4,4,0},
            {0,4,4,4,4,4,4,0},
            {0,0,4,4,4,4,0,0},
            {0,0,5,5,5,5,0,0},
            {0,0,5,5,5,5,0,0},
            {0,0,6,6,6,6,0,0}
        };

        // Animate arms
        if (danceFrame % 2 == 0)
        {
            character[5, 0] = 0;
            character[5, 7] = 0;
        }

        return character;
    }

    private int[,] GetJumperPixels(int frame)
    {
        // Similar to runner but with different poses
        return GetRunnerPixels(frame);
    }

    private SKColor[] GetCharacterColors(SKColor tintColor)
    {
        return new SKColor[] {
            SKColors.Transparent,
            SKColor.Parse("#8B4513"), // Brown outline
            SKColor.Parse("#FDBCB4"), // Skin
            SKColor.Parse("#000000"), // Eyes
            tintColor, // Shirt (tinted)
            SKColor.Parse("#0000FF"), // Pants
            SKColor.Parse("#654321"), // Shoes
            SKColor.Parse("#FDBCB4")  // Hands
        };
    }

    private BeatInfo GetBeatInfo(double frameTime, List<double> beatGrid)
    {
        var beatInfo = new BeatInfo { OnBeat = false, Strength = 0 };

        if (beatGrid.Count == 0) return beatInfo;

        var nearestBeat = beatGrid.OrderBy(bt => Math.Abs(bt - frameTime)).FirstOrDefault();
        var beatDistance = Math.Abs(nearestBeat - frameTime);

        if (beatDistance < 0.1) // Within 100ms of beat
        {
            beatInfo.OnBeat = true;
            beatInfo.Strength = 1f - (float)(beatDistance / 0.1);
        }

        return beatInfo;
    }

    private List<double> GenerateBeatGrid(AudioData audio, TimeSpan duration)
    {
        var grid = new List<double>();

        if (audio.BeatTimes != null && audio.BeatTimes.Length > 0)
        {
            grid.AddRange(audio.BeatTimes.Where(bt => bt <= duration.TotalSeconds));
        }
        else
        {
            // Generate from tempo
            var beatInterval = 60.0 / audio.Tempo;
            var time = 0.0;

            while (time < duration.TotalSeconds)
            {
                grid.Add(time);
                time += beatInterval;
            }
        }

        return grid;
    }

    private double GetEnergyAtTime(AudioData audio, double time)
    {
        if (audio.EnergyLevels.Length == 0) return 0.5;

        var index = (int)(time * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
        index = Math.Max(0, Math.Min(audio.EnergyLevels.Length - 1, index));

        return audio.EnergyLevels[index];
    }

    private SKColor GetSectionColor(ColorPalette palette, string sectionType, int index)
    {
        var colors = sectionType == "chorus" ? palette.Primary : palette.Secondary;
        var colorStr = colors[index % colors.Length];
        return SKColor.Parse(colorStr);
    }

    private float GetEqualizerHue(string sectionType, float frequency, double time)
    {
        var baseHue = sectionType switch
        {
            "intro" => 200f,   // Blue
            "verse" => 120f,   // Green
            "chorus" => 0f,    // Red
            "bridge" => 280f,  // Purple
            "outro" => 60f,    // Yellow
            _ => 180f          // Cyan
        };

        return (baseHue + frequency * 60f + (float)(time * 20)) % 360f;
    }

    private SKColor GetWaveformColor(ColorPalette palette, string sectionType, double time)
    {
        var colors = palette.Primary.Select(c => SKColor.Parse(c)).ToArray();
        var index = ((int)(time * 0.5)) % colors.Length;

        return colors[index];
    }

    private void DrawEnhancedWatermark(SKCanvas canvas, string watermark, VideoSettings settings, double time)
    {
        // Animated watermark with subtle pulse
        var pulse = (float)(Math.Sin(time * 2) * 0.1 + 1.0);

        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(180),
            TextSize = 24 * pulse,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        var textBounds = new SKRect();
        textPaint.MeasureText(watermark, ref textBounds);

        var x = settings.Width - textBounds.Width - 20;
        var y = settings.Height - 30;

        // Shadow
        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(100),
            TextSize = 24 * pulse,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        canvas.DrawText(watermark, x + 2, y + 2, shadowPaint);
        canvas.DrawText(watermark, x, y, textPaint);
    }

    private SKColor InterpolateColor(SKColor color1, SKColor color2, float t)
    {
        color1.ToHsl(out float h1, out float s1, out float l1);
        color2.ToHsl(out float h2, out float s2, out float l2);

        // Handle hue wrapping
        if (Math.Abs(h1 - h2) > 180)
        {
            if (h1 > h2) h2 += 360;
            else h1 += 360;
        }

        var h = (h1 + (h2 - h1) * t) % 360;
        var s = s1 + (s2 - s1) * t;
        var l = l1 + (l2 - l1) * t;

        return SKColor.FromHsl(h, s, l);
    }

    // ... (Additional helper methods like original but enhanced)

    private List<Star> GenerateStars(int count)
    {
        var stars = new List<Star>();
        for (int i = 0; i < count; i++)
        {
            stars.Add(new Star
            {
                X = (float)_random.NextDouble(),
                Y = (float)_random.NextDouble() * 0.6f,
                Size = (float)(_random.NextDouble() * 3 + 0.5),
                Brightness = (float)(_random.NextDouble() * 0.6 + 0.4),
                TwinkleSpeed = (float)(_random.NextDouble() * 3 + 1),
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
                Width = (float)(_random.NextDouble() * 0.1 + 0.05),
                Height = (float)(_random.NextDouble() * 0.6 + 0.4),
                WindowRows = _random.Next(4, 10),
                WindowCols = _random.Next(3, 6)
            };
            buildings.Add(building);
            x += building.Width + 0.02f;
        }

        return buildings;
    }

    private List<PixelCharacter> GeneratePixelCharacters(int count)
    {
        var characters = new List<PixelCharacter>();
        var types = new[] { "runner", "dancer", "jumper" };

        for (int i = 0; i < count; i++)
        {
            characters.Add(new PixelCharacter
            {
                Type = types[i % types.Length],
                Color = SKColors.White,
                Scale = 1.0f + i * 0.1f
            });
        }

        return characters;
    }

    private async Task<TemplateInfo> LoadTemplateAsync(string templateName)
    {
        // Same as original
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

                    // Quality presets
                    var crf = settings.Quality switch
                    {
                        "Draft" => "28",
                        "High" => "18",
                        _ => "23"
                    };

                    options.WithCustomArgument($"-crf {crf}")
                           .WithCustomArgument("-pix_fmt yuv420p")
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

    // Additional drawing methods for enhanced effects

    private void DrawParallaxCityscape(SKCanvas canvas, List<Building> buildings, VideoSettings settings, double time, double intensity)
    {
        // Multiple layers with different parallax speeds
        for (int layer = 2; layer >= 0; layer--)
        {
            var parallaxSpeed = 1.0f - layer * 0.3f;
            var offset = (float)(time * 10 * parallaxSpeed) % settings.Width;

            using var buildingPaint = new SKPaint
            {
                Color = SKColor.Parse("#0a1929").WithAlpha((byte)(100 + layer * 50))
            };

            foreach (var building in buildings)
            {
                var x = (building.X * settings.Width - offset + settings.Width) % settings.Width;
                var height = building.Height * settings.Height * 0.4f * (1 + layer * 0.2f);
                var y = settings.Height - 120 - height;
                var width = building.Width * settings.Width;

                canvas.DrawRect(x, y, width, height, buildingPaint);

                // Windows with varying brightness based on intensity
                if (layer == 0) // Only on front layer
                {
                    using var windowPaint = new SKPaint
                    {
                        Color = SKColors.Yellow.WithAlpha((byte)(100 + intensity * 100))
                    };

                    for (int wy = 0; wy < building.WindowRows; wy++)
                    {
                        for (int wx = 0; wx < building.WindowCols; wx++)
                        {
                            if (_random.NextDouble() > 0.4 - intensity * 0.3)
                            {
                                var windowX = x + 5 + wx * 15;
                                var windowY = y + 10 + wy * 20;
                                canvas.DrawRect(windowX, windowY, 10, 14, windowPaint);
                            }
                        }
                    }
                }
            }
        }
    }

    private void DrawAnimatedGround(SKCanvas canvas, VideoSettings settings, double time, ColorPalette palette, bool onBeat)
    {
        var groundY = settings.Height - 120;

        // Gradient ground with palette colors
        var groundColors = palette.Primary.Select(c => SKColor.Parse(c)).ToArray();
        var color1 = groundColors[0].WithAlpha(100);
        var color2 = groundColors[1].WithAlpha(50);

        using var groundGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, groundY),
            new SKPoint(0, settings.Height),
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);

        using var groundPaint = new SKPaint { Shader = groundGradient };
        canvas.DrawRect(0, groundY, settings.Width, 120, groundPaint);

        // Animated grid
        var gridOffset = (float)(time * 50) % 30;
        using var gridPaint = new SKPaint
        {
            Color = palette.Secondary.Length > 0 ?
                SKColor.Parse(palette.Secondary[0]).WithAlpha(onBeat ? (byte)150 : (byte)80) :
                SKColors.White.WithAlpha(80),
            StrokeWidth = onBeat ? 3 : 2,
            Style = SKPaintStyle.Stroke
        };

        // Horizontal lines with perspective
        for (int i = 0; i < 10; i++)
        {
            var y = groundY + i * 15 - gridOffset + 30;
            if (y > groundY && y < settings.Height)
            {
                canvas.DrawLine(0, y, settings.Width, y, gridPaint);
            }
        }

        // Vertical lines for depth
        for (int i = -10; i < 20; i++)
        {
            var x = settings.Width / 2 + i * 100 - gridOffset * 2;
            var topY = groundY;
            var bottomX = x + (x - settings.Width / 2) * 0.5f;

            canvas.DrawLine(x, topY, bottomX, settings.Height, gridPaint);
        }
    }

    private void DrawBeatSyncedParticles(SKCanvas canvas, VideoSettings settings, BeatInfo beatInfo, MusicSection section, ColorPalette palette)
    {
        if (!beatInfo.OnBeat) return;

        var particleCount = (int)(20 * beatInfo.Strength * section.Intensity);
        var colors = palette.Primary.Select(c => SKColor.Parse(c)).ToArray();

        using var particlePaint = new SKPaint();

        for (int i = 0; i < particleCount; i++)
        {
            var angle = i * Math.PI * 2 / particleCount;
            var radius = 50 + beatInfo.Strength * 200;
            var x = settings.Width / 2 + (float)(Math.Cos(angle) * radius);
            var y = settings.Height / 2 + (float)(Math.Sin(angle) * radius);

            particlePaint.Color = colors[i % colors.Length].WithAlpha((byte)(beatInfo.Strength * 255));

            var size = 3 + beatInfo.Strength * 5;
            canvas.DrawCircle(x, y, size, particlePaint);
        }
    }

    private void DrawIntensitySpeedLines(SKCanvas canvas, VideoSettings settings, double intensity, double time)
    {
        var lineCount = (int)(intensity * 20);

        using var linePaint = new SKPaint
        {
            StrokeWidth = 2,
            PathEffect = SKPathEffect.CreateDash(new float[] { 30, 10 }, (float)time * 10)
        };

        for (int i = 0; i < lineCount; i++)
        {
            var y = _random.Next(0, settings.Height);
            var alpha = (byte)(intensity * 100 * (1 - Math.Abs(y - settings.Height / 2) / (settings.Height / 2)));

            linePaint.Color = SKColors.White.WithAlpha(alpha);
            canvas.DrawLine(0, y, settings.Width, y, linePaint);
        }
    }

    private void DrawEnhancedHUD(SKCanvas canvas, AudioData audio, double time, VideoSettings settings, MusicSection section)
    {
        // Background panel with section-based styling
        var panelColor = section.Type == "chorus" ?
            SKColors.Black.WithAlpha(200) :
            SKColors.Black.WithAlpha(150);

        using var panelPaint = new SKPaint
        {
            Color = panelColor,
            Style = SKPaintStyle.Fill
        };

        var panelRect = new SKRoundRect(new SKRect(10, 10, 280, 100), 10, 10);
        canvas.DrawRoundRect(panelRect, panelPaint);

        // Border glow for chorus
        if (section.Type == "chorus")
        {
            using var borderPaint = new SKPaint
            {
                Color = SKColors.Cyan.WithAlpha(150),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5)
            };
            canvas.DrawRoundRect(panelRect, borderPaint);
        }

        // Text info
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        canvas.DrawText($"♪ {audio.Tempo:F0} BPM", 20, 35, textPaint);
        canvas.DrawText($"♫ {audio.Genre}", 20, 55, textPaint);
        canvas.DrawText($"⚡ {section.Type.ToUpper()}", 20, 75, textPaint);
        canvas.DrawText($"◷ {TimeSpan.FromSeconds(time):mm\\:ss}", 20, 95, textPaint);

        // Mini visualizer bars
        var barX = 160;
        for (int i = 0; i < 10; i++)
        {
            var barHeight = (float)(_random.NextDouble() * 30 + 10);
            using var barPaint = new SKPaint
            {
                Color = SKColors.Cyan.WithAlpha(200)
            };
            canvas.DrawRect(barX + i * 12, 70 - barHeight, 10, barHeight, barPaint);
        }
    }

    private void DrawPulsingGrid(SKCanvas canvas, VideoSettings settings, double time, BeatInfo beatInfo)
    {
        var gridSize = 40;
        var pulseScale = beatInfo.OnBeat ? 1.2f : 1.0f;

        using var gridPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(beatInfo.OnBeat ? (byte)40 : (byte)20),
            StrokeWidth = beatInfo.OnBeat ? 2 : 1,
            Style = SKPaintStyle.Stroke
        };

        // Pulsing grid effect
        var centerX = settings.Width / 2;
        var centerY = settings.Height / 2;

        for (int x = 0; x < settings.Width; x += gridSize)
        {
            var offsetX = (x - centerX) * pulseScale + centerX;
            canvas.DrawLine(offsetX, 0, offsetX, settings.Height, gridPaint);
        }

        for (int y = 0; y < settings.Height; y += gridSize)
        {
            var offsetY = (y - centerY) * pulseScale + centerY;
            canvas.DrawLine(0, offsetY, settings.Width, offsetY, gridPaint);
        }
    }

    private void DrawSpectrumAnalyzer(SKCanvas canvas, VideoSettings settings, AudioData audio, double time, MusicSection section)
    {
        // Circular spectrum analyzer overlay
        var centerX = settings.Width / 2f;
        var centerY = settings.Height / 2f;
        var radius = Math.Min(settings.Width, settings.Height) * 0.3f;

        using var spectrumPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };

        var segments = 60;
        for (int i = 0; i < segments; i++)
        {
            var angle = i * Math.PI * 2 / segments;
            var energy = GetEnergyAtTime(audio, time + i * 0.01);
            var lineLength = radius * (0.5f + energy * 0.5f * section.Intensity);

            var x1 = centerX + (float)(Math.Cos(angle) * radius);
            var y1 = centerY + (float)(Math.Sin(angle) * radius);
            var x2 = centerX + (float)(Math.Cos(angle) * lineLength);
            var y2 = centerY + (float)(Math.Sin(angle) * lineLength);

            var hue = (float)((i * 6 + time * 60) % 360);
            spectrumPaint.Color = SKColor.FromHsl(hue, 100f, 50f).WithAlpha(200);

            canvas.DrawLine(x1, y1, x2, y2, spectrumPaint);
        }
    }

    private void DrawCircularPulse(SKCanvas canvas, VideoSettings settings, double time, BeatInfo beatInfo, ColorPalette palette)
    {
        var centerX = settings.Width / 2f;
        var centerY = settings.Height / 2f;

        var colors = palette.Secondary.Select(c => SKColor.Parse(c)).ToArray();

        using var pulsePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = beatInfo.OnBeat ? 4 : 2,
            IsAntialias = true
        };

        for (int i = 0; i < 5; i++)
        {
            var radius = 50 + i * 50 + (float)(Math.Sin(time * 2 + i) * 20);

            if (beatInfo.OnBeat)
            {
                radius += beatInfo.Strength * 30;
            }

            var alpha = (byte)(200 - i * 30);
            pulsePaint.Color = colors[i % colors.Length].WithAlpha(alpha);

            canvas.DrawCircle(centerX, centerY, radius, pulsePaint);
        }
    }

    private void DrawTimeIndicator(SKCanvas canvas, VideoSettings settings, double time, BeatInfo beatInfo, SKColor color)
    {
        var x = settings.Width / 2;

        // Main indicator line
        using var indicatorPaint = new SKPaint
        {
            Color = color,
            StrokeWidth = beatInfo.OnBeat ? 4 : 2,
            PathEffect = SKPathEffect.CreateDash(new float[] { 10, 5 }, (float)time * 10)
        };

        canvas.DrawLine(x, 0, x, settings.Height, indicatorPaint);

        // Beat pulse circle
        if (beatInfo.OnBeat)
        {
            using var pulsePaint = new SKPaint
            {
                Color = color.WithAlpha((byte)(beatInfo.Strength * 150)),
                Style = SKPaintStyle.Fill
            };

            var radius = 10 + beatInfo.Strength * 20;
            canvas.DrawCircle(x, settings.Height / 2, radius, pulsePaint);
        }
    }

    private void DrawSectionOverlays(SKCanvas canvas, VideoSettings settings, MusicSection section, double time, BeatInfo beatInfo)
    {
        // Special effects for different sections
        switch (section.Type)
        {
            case "chorus":
                // Confetti effect
                if (beatInfo.OnBeat && beatInfo.Strength > 0.7f)
                {
                    DrawConfetti(canvas, settings, beatInfo.Strength);
                }
                break;

            case "bridge":
                // Subtle vignette
                DrawVignette(canvas, settings, 0.3f);
                break;

            case "outro":
                // Fade effect
                var progress = (time - section.StartTime) / (section.EndTime - section.StartTime);
                DrawFadeOverlay(canvas, settings, progress);
                break;
        }
    }

    private void DrawConfetti(SKCanvas canvas, VideoSettings settings, float intensity)
    {
        var confettiCount = (int)(intensity * 50);

        using var confettiPaint = new SKPaint();

        for (int i = 0; i < confettiCount; i++)
        {
            var x = _random.Next(0, settings.Width);
            var y = _random.Next(0, settings.Height / 2);
            var size = _random.Next(5, 15);

            var hue = (float)_random.Next(0, 360);
            confettiPaint.Color = SKColor.FromHsl(hue, 100f, 50f);

            canvas.Save();
            canvas.Translate(x, y);
            canvas.RotateDegrees(_random.Next(0, 360));
            canvas.DrawRect(-size / 2, -size / 2, size, size / 2, confettiPaint);
            canvas.Restore();
        }
    }

    private void DrawVignette(SKCanvas canvas, VideoSettings settings, float intensity)
    {
        using var vignettePaint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(settings.Width / 2, settings.Height / 2),
                Math.Max(settings.Width, settings.Height) * 0.7f,
                new[] { SKColors.Transparent, SKColors.Black.WithAlpha((byte)(intensity * 255)) },
                SKShaderTileMode.Clamp)
        };

        canvas.DrawRect(0, 0, settings.Width, settings.Height, vignettePaint);
    }

    private void DrawFadeOverlay(SKCanvas canvas, VideoSettings settings, double progress)
    {
        using var fadePaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha((byte)(progress * 200))
        };

        canvas.DrawRect(0, 0, settings.Width, settings.Height, fadePaint);
    }

    // Data structures
    private class EnhancedAnimationState
    {
        public ColorPalette Palette { get; set; } = new();
        public List<MusicSection> Sections { get; set; } = new();
        public List<double> BeatGrid { get; set; } = new();
        public List<Star> Stars { get; set; } = new();
        public List<Particle> Particles { get; set; } = new();
        public List<Building> Buildings { get; set; } = new();
        public List<PixelCharacter> PixelCharacters { get; set; } = new();
    }

    private class ColorPalette
    {
        public string[] Primary { get; set; } = Array.Empty<string>();
        public string[] Secondary { get; set; } = Array.Empty<string>();
        public string[] Background { get; set; } = Array.Empty<string>();
    }

    private class BeatInfo
    {
        public bool OnBeat { get; set; }
        public float Strength { get; set; }
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

    private class PixelCharacter
    {
        public string Type { get; set; } = "runner";
        public SKColor Color { get; set; }
        public float Scale { get; set; }
    }
}