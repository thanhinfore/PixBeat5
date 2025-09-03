// PixBeat5/Services/EnhancedSquareBoomRenderService.cs
using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using PixBeat5.Models;
using SkiaSharp;
using System.IO;
using System.Text.Json;

namespace PixBeat5.Services;

/// <summary>
/// Enhanced Square.Boom renderer với visual polish và smooth animations
/// Tối ưu cho TikTok/Shorts với grid pulsing effects
/// </summary>
public class EnhancedSquareBoomRenderService : IRenderService
{
    private readonly ILogger<EnhancedSquareBoomRenderService> _logger;
    private readonly Random _random;

    // Enhanced color palettes với gradient support
    private readonly Dictionary<string, EnhancedPalette> _palettes = new()
    {
        ["Vibrant"] = new EnhancedPalette
        {
            Background = new[] { "#000000", "#0a0e27" },
            GridLines = "#1A1A1A",
            CellColors = new[] { "#FFFFFF", "#00FFA3", "#00C2FF", "#FFD60A", "#FF4D4D", "#FF00FF", "#00FF00", "#FF8C00" },
            GlowColors = new[] { "#00FFA366", "#00C2FF66", "#FFD60A66", "#FF4D4D66" }
        },
        ["Neon"] = new EnhancedPalette
        {
            Background = new[] { "#0A0A0A", "#1a0033" },
            GridLines = "#222222",
            CellColors = new[] { "#FF006E", "#FB5607", "#FFBE0B", "#06FFB4", "#8338EC", "#3A86FF", "#FF4365", "#00F5FF" },
            GlowColors = new[] { "#FF006E88", "#06FFB488", "#8338EC88", "#00F5FF88" }
        },
        ["Sunset"] = new EnhancedPalette
        {
            Background = new[] { "#1a0033", "#330033" },
            GridLines = "#2A1A3A",
            CellColors = new[] { "#FF6B6B", "#FFE66D", "#4ECDC4", "#1A535C", "#FFD93D", "#6BCB77", "#4D96FF", "#FF6B9D" },
            GlowColors = new[] { "#FF6B6B66", "#FFE66D66", "#4ECDC466", "#FF6B9D66" }
        }
    };

    public EnhancedSquareBoomRenderService(ILogger<EnhancedSquareBoomRenderService> logger)
    {
        _logger = logger;
        _random = new Random();
    }

    public async Task<string> RenderVideoAsync(ProjectData project, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (project.Audio == null)
            throw new ArgumentException("Project must have audio data");

        _logger.LogInformation("Starting Enhanced Square.Boom render for: {ProjectName}", project.Name);

        var tempDir = Path.Combine(Path.GetTempPath(), "pixbeat_squareboom_enhanced", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Generate enhanced timeline
            var timeline = await GenerateEnhancedTimelineAsync(project.Audio);

            // 2. Create enhanced template
            var template = CreateEnhancedTemplate(project, timeline);

            // 3. Map with smooth transitions
            var graphicsEvents = MapTimelineWithSmoothing(timeline, template);

            // 4. Initialize enhanced grid state
            var gridState = new EnhancedGridState
            {
                Template = template,
                Timeline = timeline,
                GraphicsEvents = graphicsEvents,
                Grid = InitializeEnhancedGrid(template.Grid),
                Palette = SelectEnhancedPalette(project.Audio.Mood, project.Audio.Genre),
                AnimParams = new AnimationParams(),
                Particles = new List<Particle>(),
                RandomSeed = template.RandomSeed
            };

            // 5. Calculate frames (60fps for smooth animation)
            project.Settings.Fps = 60;
            var totalFrames = (int)(project.Settings.Duration.TotalSeconds * project.Settings.Fps);
            var renderProgress = new RenderProgress
            {
                TotalFrames = totalFrames,
                Stage = "Generating Enhanced Square.Boom Frames"
            };

            // 6. Generate frames with enhanced visuals
            var frameFiles = await GenerateEnhancedFramesAsync(
                project, tempDir, gridState, renderProgress, progress, cancellationToken);

            // 7. Encode with high quality
            renderProgress.Stage = "Encoding HD Square.Boom Video";
            progress?.Report(renderProgress);

            var outputPath = await EncodeHighQualityVideoAsync(
                frameFiles, project.Audio.FilePath, project.Settings, cancellationToken);

            _logger.LogInformation("Enhanced Square.Boom video complete: {OutputPath}", outputPath);
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
                _logger.LogWarning(ex, "Failed to cleanup: {TempDir}", tempDir);
            }
        }
    }

    // === TIMELINE GENERATION ===

    private async Task<Timeline> GenerateEnhancedTimelineAsync(AudioData audio)
    {
        return await Task.Run(() =>
        {
            var timeline = new Timeline
            {
                SampleRate = 44100,
                Duration = audio.Duration.TotalSeconds,
                BPM = audio.Tempo,
                Beats = audio.BeatTimes ?? GenerateBeatsFromTempo(audio.Tempo, audio.Duration),
                Key = $"{audio.Key}:{audio.Mode}"
            };

            // Generate onsets from energy levels
            timeline.Onsets = GenerateOnsetsFromEnergy(audio.EnergyLevels, audio.Duration);

            // Generate sections
            timeline.Sections = DetectSections(audio.EnergyLevels, audio.Duration);

            // Generate loudness timeline
            timeline.Loudness = GenerateLoudnessTimeline(audio.EnergyLevels, audio.Duration);

            return timeline;
        });
    }

    private double[] GenerateBeatsFromTempo(double bpm, TimeSpan duration)
    {
        var beats = new List<double>();
        var beatInterval = 60.0 / bpm;
        var time = 0.0;

        while (time < duration.TotalSeconds)
        {
            beats.Add(time);
            time += beatInterval;
        }

        return beats.ToArray();
    }

    private List<OnsetEvent> GenerateOnsetsFromEnergy(double[] energyLevels, TimeSpan duration)
    {
        var onsets = new List<OnsetEvent>();
        if (energyLevels.Length == 0) return onsets;

        var timeStep = duration.TotalSeconds / energyLevels.Length;

        for (int i = 1; i < energyLevels.Length; i++)
        {
            var energyDiff = energyLevels[i] - energyLevels[i - 1];

            if (energyDiff > 0.2)
            {
                onsets.Add(new OnsetEvent
                {
                    Time = i * timeStep,
                    Strength = Math.Min(1.0, energyDiff * 2),
                    FrequencyProxy = 2000 + _random.Next(0, 4000)
                });
            }
        }

        return onsets;
    }

    private List<SectionEvent> DetectSections(double[] energyLevels, TimeSpan duration)
    {
        var sections = new List<SectionEvent>();
        sections.Add(new SectionEvent { Time = 0, Label = "intro" });

        if (energyLevels.Length == 0) return sections;

        var timeStep = duration.TotalSeconds / energyLevels.Length;
        var avgEnergy = energyLevels.Average();

        bool inHighEnergy = false;

        for (int i = 0; i < energyLevels.Length; i++)
        {
            var time = i * timeStep;

            if (!inHighEnergy && energyLevels[i] > avgEnergy * 1.2 && time > 5)
            {
                sections.Add(new SectionEvent { Time = time, Label = "chorus" });
                inHighEnergy = true;
            }
            else if (inHighEnergy && energyLevels[i] < avgEnergy * 0.9 && time > sections.Last().Time + 5)
            {
                sections.Add(new SectionEvent { Time = time, Label = "verse" });
                inHighEnergy = false;
            }
        }

        if (duration.TotalSeconds > 20)
        {
            sections.Add(new SectionEvent
            {
                Time = duration.TotalSeconds - 5,
                Label = "outro"
            });
        }

        return sections;
    }

    private List<LoudnessEvent> GenerateLoudnessTimeline(double[] energyLevels, TimeSpan duration)
    {
        var loudness = new List<LoudnessEvent>();
        if (energyLevels.Length == 0) return loudness;

        var timeStep = duration.TotalSeconds / energyLevels.Length;

        for (int i = 0; i < energyLevels.Length; i++)
        {
            loudness.Add(new LoudnessEvent
            {
                Time = i * timeStep,
                LUFS = -30 + energyLevels[i] * 20
            });
        }

        return loudness;
    }

    // === TEMPLATE CREATION ===

    private SquareBoomTemplate CreateEnhancedTemplate(ProjectData project, Timeline timeline)
    {
        return new SquareBoomTemplate
        {
            Name = "enhanced-square-boom",
            Grid = new GridConfig
            {
                Rows = 8,
                Cols = 8,
                Gap = 8,
                Border = 4
            },
            Rules = new List<MappingRule>
            {
                new MappingRule
                {
                    When = "beat",
                    Action = "pulse",
                    Cells = "random:6",
                    ScaleRange = new[] { 1.0f, 1.6f },
                    Duration = 0.18f,
                    Easing = "outCubic"
                },
                new MappingRule
                {
                    When = "onset>0.7",
                    Action = "flash",
                    Cells = "freqBandRow",
                    AlphaRange = new[] { 0.3f, 1.0f },
                    Duration = 0.12f
                },
                new MappingRule
                {
                    When = "sectionChange",
                    Action = "wipe",
                    Direction = "lr",
                    Duration = 0.6f
                },
                new MappingRule
                {
                    When = "loudnessUp>1.5",
                    Action = "swell",
                    Cells = "all",
                    ScaleRange = new[] { 1.0f, 1.1f },
                    Duration = 0.4f
                }
            },
            Export = new ExportConfig
            {
                FPS = 60,
                Size = new[] { project.Settings.Width, project.Settings.Height },
                Background = "#000000"
            },
            RandomSeed = _random.Next(10000, 99999)
        };
    }

    // === EVENT MAPPING ===

    private List<GraphicsEvent> MapTimelineWithSmoothing(Timeline timeline, SquareBoomTemplate template)
    {
        var events = new List<GraphicsEvent>();
        var rand = new Random(template.RandomSeed);

        // Map beats to pulse events
        foreach (var beat in timeline.Beats)
        {
            var cellCount = rand.Next(4, 9);
            var targets = new List<GridCell>();

            for (int i = 0; i < cellCount; i++)
            {
                targets.Add(new GridCell
                {
                    Row = rand.Next(template.Grid.Rows),
                    Col = rand.Next(template.Grid.Cols)
                });
            }

            events.Add(new GraphicsEvent
            {
                Time = beat,
                Type = "pulse",
                Targets = targets,
                Duration = 0.18f,
                Parameters = new Dictionary<string, object>
                {
                    ["scale"] = 1.6f,
                    ["easing"] = "outCubic"
                }
            });
        }

        // Map strong onsets to flash events
        foreach (var onset in timeline.Onsets.Where(o => o.Strength > 0.7))
        {
            var row = MapFrequencyToRow(onset.FrequencyProxy, template.Grid.Rows);

            events.Add(new GraphicsEvent
            {
                Time = onset.Time,
                Type = "flash",
                Targets = GenerateRowTargets(row, template.Grid.Cols),
                Duration = 0.12f,
                Parameters = new Dictionary<string, object>
                {
                    ["alpha"] = 1.0f
                }
            });
        }

        // Map section changes to wipe events
        for (int i = 1; i < timeline.Sections.Count; i++)
        {
            events.Add(new GraphicsEvent
            {
                Time = timeline.Sections[i].Time,
                Type = "wipe",
                Targets = GenerateAllCells(template.Grid),
                Duration = 0.6f,
                Parameters = new Dictionary<string, object>
                {
                    ["direction"] = i % 2 == 0 ? "lr" : "tb"
                }
            });
        }

        // Map loudness increases to swell events
        for (int i = 1; i < timeline.Loudness.Count; i++)
        {
            var loudnessIncrease = timeline.Loudness[i].LUFS - timeline.Loudness[i - 1].LUFS;

            if (loudnessIncrease > 1.5)
            {
                events.Add(new GraphicsEvent
                {
                    Time = timeline.Loudness[i].Time,
                    Type = "swell",
                    Targets = GenerateAllCells(template.Grid),
                    Duration = 0.4f,
                    Parameters = new Dictionary<string, object>
                    {
                        ["scale"] = 1.1f
                    }
                });
            }
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        return events;
    }

    private int MapFrequencyToRow(double frequency, int rows)
    {
        var normalized = Math.Log10(frequency / 100) / Math.Log10(80);
        var row = (int)((1 - normalized) * (rows - 1));
        return Math.Max(0, Math.Min(rows - 1, row));
    }

    private List<GridCell> GenerateRowTargets(int row, int cols)
    {
        var targets = new List<GridCell>();
        for (int col = 0; col < cols; col++)
        {
            targets.Add(new GridCell { Row = row, Col = col });
        }
        return targets;
    }

    private List<GridCell> GenerateAllCells(GridConfig grid)
    {
        var targets = new List<GridCell>();
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Cols; col++)
            {
                targets.Add(new GridCell { Row = row, Col = col });
            }
        }
        return targets;
    }

    // === GRID INITIALIZATION ===

    private EnhancedGridCell[,] InitializeEnhancedGrid(GridConfig config)
    {
        var grid = new EnhancedGridCell[config.Rows, config.Cols];

        for (int row = 0; row < config.Rows; row++)
        {
            for (int col = 0; col < config.Cols; col++)
            {
                grid[row, col] = new EnhancedGridCell
                {
                    Row = row,
                    Col = col,
                    CurrentScale = 1.0f,
                    TargetScale = 1.0f,
                    CurrentAlpha = 1.0f,
                    TargetAlpha = 1.0f,
                    Brightness = 1.0f,
                    IsAnimating = false,
                    IsFlashing = false,
                    FlashIntensity = 0
                };
            }
        }

        return grid;
    }

    // === PALETTE SELECTION ===

    private EnhancedPalette SelectEnhancedPalette(string mood, string genre)
    {
        // Select based on mood and genre
        if (mood.ToLower().Contains("energetic") || genre.ToLower().Contains("electronic"))
            return _palettes["Neon"];
        else if (mood.ToLower().Contains("happy") || genre.ToLower().Contains("pop"))
            return _palettes["Vibrant"];
        else
            return _palettes["Sunset"];
    }

    // === FRAME GENERATION ===

    private async Task<string[]> GenerateEnhancedFramesAsync(
        ProjectData project,
        string outputDir,
        EnhancedGridState state,
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

                GenerateEnhancedFrame(project, state, frameTime, frameFile, frameIndex);
                frameFiles[frameIndex] = frameFile;

                if (frameIndex % 30 == 0)
                {
                    renderProgress.CurrentFrame = frameIndex;
                    renderProgress.Stage = $"Rendering Frame {frameIndex}/{renderProgress.TotalFrames}";
                    renderProgress.Elapsed = DateTime.Now - startTime;

                    if (frameIndex > 0)
                    {
                        var avgTimePerFrame = renderProgress.Elapsed.TotalMilliseconds / frameIndex;
                        renderProgress.Estimated = TimeSpan.FromMilliseconds(
                            (renderProgress.TotalFrames - frameIndex) * avgTimePerFrame);
                    }

                    progress?.Report(renderProgress);
                }
            });
        }, cancellationToken);

        return frameFiles;
    }

    private void GenerateEnhancedFrame(
        ProjectData project,
        EnhancedGridState state,
        double frameTime,
        string outputPath,
        int frameIndex)
    {
        using var surface = SKSurface.Create(new SKImageInfo(project.Settings.Width, project.Settings.Height));
        var canvas = surface.Canvas;

        // Draw animated background
        DrawAnimatedBackground(canvas, state, project.Settings, frameTime);

        // Update grid state
        UpdateEnhancedGridState(state, frameTime);

        // Update particles
        UpdateParticles(state, frameTime, project.Settings);

        // Draw particles
        DrawBackgroundParticles(canvas, state, project.Settings);

        // Draw main grid
        DrawEnhancedGrid(canvas, state, project.Settings, frameTime);

        // Draw overlay effects
        DrawOverlayEffects(canvas, state, project.Settings, frameTime);

        // Draw section indicator
        DrawSectionIndicator(canvas, state, project.Settings, frameTime);

        // Draw beat flash
        if (IsBeatFrame(state, frameTime))
        {
            DrawBeatFlash(canvas, state, project.Settings);
        }

        // Watermark
        DrawStyledWatermark(canvas, project.Settings.Watermark ?? "PixBeat", project.Settings);

        // Save frame
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outputPath, data.ToArray());
    }

    // === RENDERING METHODS ===

    private void DrawAnimatedBackground(SKCanvas canvas, EnhancedGridState state, VideoSettings settings, double frameTime)
    {
        var bgColors = state.Palette.Background.Select(c => SKColor.Parse(c)).ToArray();

        var t = (float)(Math.Sin(frameTime * 0.1) * 0.5 + 0.5);
        var color1 = InterpolateColor(bgColors[0], bgColors.Length > 1 ? bgColors[1] : bgColors[0], t);
        var color2 = InterpolateColor(bgColors.Length > 1 ? bgColors[1] : bgColors[0], bgColors[0], t);

        using var gradient = SKShader.CreateRadialGradient(
            new SKPoint(settings.Width / 2, settings.Height / 2),
            Math.Max(settings.Width, settings.Height) * 0.8f,
            new[] { color1, color2 },
            SKShaderTileMode.Clamp);

        using var bgPaint = new SKPaint { Shader = gradient };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);

        // Add noise texture
        using var noisePaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(5),
            BlendMode = SKBlendMode.Overlay
        };

        for (int i = 0; i < 200; i++)
        {
            var x = _random.Next(0, settings.Width);
            var y = _random.Next(0, settings.Height);
            canvas.DrawCircle(x, y, 1, noisePaint);
        }
    }

    private void UpdateEnhancedGridState(EnhancedGridState state, double frameTime)
    {
        foreach (var cell in state.Grid)
        {
            // Smooth interpolation
            if (cell.TargetScale != cell.CurrentScale)
            {
                cell.CurrentScale = Lerp(cell.CurrentScale, cell.TargetScale, 0.15f);
                if (Math.Abs(cell.CurrentScale - cell.TargetScale) < 0.01f)
                    cell.CurrentScale = cell.TargetScale;
            }

            if (cell.TargetAlpha != cell.CurrentAlpha)
            {
                cell.CurrentAlpha = Lerp(cell.CurrentAlpha, cell.TargetAlpha, 0.2f);
                if (Math.Abs(cell.CurrentAlpha - cell.TargetAlpha) < 0.01f)
                    cell.CurrentAlpha = cell.TargetAlpha;
            }

            cell.Brightness = 0.85f + (float)(Math.Sin(frameTime * 2 + cell.Row * 0.1 + cell.Col * 0.1) * 0.15);
            cell.IsAnimating = Math.Abs(cell.CurrentScale - 1.0f) > 0.01f || Math.Abs(cell.CurrentAlpha - 1.0f) > 0.01f;
            cell.IsFlashing = false;
            cell.FlashIntensity = 0;
        }

        // Apply active events
        foreach (var evt in state.GraphicsEvents)
        {
            if (frameTime >= evt.Time && frameTime < evt.Time + evt.Duration)
            {
                var progress = (frameTime - evt.Time) / evt.Duration;
                ApplyEnhancedEvent(state, evt, progress);
            }
        }
    }

    private void ApplyEnhancedEvent(EnhancedGridState state, GraphicsEvent evt, double progress)
    {
        var easedProgress = ApplyEasing(progress, evt.Parameters.GetValueOrDefault("easing", "outCubic").ToString()!);

        switch (evt.Type)
        {
            case "pulse":
                foreach (var target in evt.Targets)
                {
                    var cell = state.Grid[target.Row, target.Col];
                    var scale = (float)evt.Parameters.GetValueOrDefault("scale", 1.6f);

                    if (progress < 0.5)
                        cell.TargetScale = 1.0f + (scale - 1.0f) * (float)(easedProgress * 2);
                    else
                        cell.TargetScale = scale - (scale - 1.0f) * (float)((easedProgress - 0.5) * 2);

                    cell.IsAnimating = true;
                }
                break;

            case "flash":
                foreach (var target in evt.Targets)
                {
                    var cell = state.Grid[target.Row, target.Col];
                    cell.IsFlashing = true;
                    cell.FlashIntensity = (float)(1.0 - easedProgress);
                    cell.TargetAlpha = 0.3f + 0.7f * (float)(1.0 - easedProgress);
                    cell.IsAnimating = true;
                }
                break;

            case "wipe":
                var direction = evt.Parameters.GetValueOrDefault("direction", "lr").ToString()!;
                ApplyEnhancedWipeEffect(state, direction, easedProgress);
                break;

            case "swell":
                var swellScale = (float)evt.Parameters.GetValueOrDefault("scale", 1.1f);
                foreach (var target in evt.Targets)
                {
                    var cell = state.Grid[target.Row, target.Col];
                    cell.TargetScale = 1.0f + (swellScale - 1.0f) * (float)Math.Sin(easedProgress * Math.PI);
                    cell.IsAnimating = true;
                }
                break;
        }
    }

    private void ApplyEnhancedWipeEffect(EnhancedGridState state, string direction, double progress)
    {
        var rows = state.Template.Grid.Rows;
        var cols = state.Template.Grid.Cols;

        if (direction == "lr")
        {
            for (int col = 0; col < cols; col++)
            {
                var colProgress = (double)col / cols;
                var waveOffset = Math.Sin(col * 0.5) * 0.1;

                if (progress > colProgress - waveOffset)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        var cell = state.Grid[row, col];
                        var localProgress = Math.Min(1, (progress - colProgress + waveOffset) * cols);

                        cell.TargetAlpha = (float)localProgress;
                        cell.TargetScale = 1.0f + (float)(0.2 * Math.Sin(localProgress * Math.PI));
                        cell.IsAnimating = true;
                    }
                }
            }
        }
        else
        {
            for (int row = 0; row < rows; row++)
            {
                var rowProgress = (double)row / rows;

                if (progress > rowProgress)
                {
                    for (int col = 0; col < cols; col++)
                    {
                        var cell = state.Grid[row, col];
                        var localProgress = Math.Min(1, (progress - rowProgress) * rows);

                        cell.TargetAlpha = (float)localProgress;
                        cell.TargetScale = 1.0f + (float)(0.2 * Math.Sin(localProgress * Math.PI));
                        cell.IsAnimating = true;
                    }
                }
            }
        }
    }

    private void UpdateParticles(EnhancedGridState state, double frameTime, VideoSettings settings)
    {
        var deltaTime = 1.0 / settings.Fps;

        for (int i = state.Particles.Count - 1; i >= 0; i--)
        {
            var p = state.Particles[i];
            p.X += p.VX * (float)deltaTime;
            p.Y += p.VY * (float)deltaTime;
            p.Life -= (float)deltaTime * 0.5f;

            if (p.Life <= 0 || p.Y > settings.Height || p.X < 0 || p.X > settings.Width)
            {
                state.Particles.RemoveAt(i);
            }
        }

        if (IsBeatFrame(state, frameTime) && state.Particles.Count < 50)
        {
            SpawnBeatParticles(state, settings);
        }
    }

    private void SpawnBeatParticles(EnhancedGridState state, VideoSettings settings)
    {
        var count = _random.Next(3, 8);

        for (int i = 0; i < count; i++)
        {
            var colorIndex = _random.Next(state.Palette.CellColors.Length);
            var color = SKColor.Parse(state.Palette.CellColors[colorIndex]);

            state.Particles.Add(new Particle
            {
                X = _random.Next(0, settings.Width),
                Y = _random.Next(0, settings.Height / 2),
                VX = (float)(_random.NextDouble() * 100 - 50),
                VY = (float)(_random.NextDouble() * 50 + 20),
                Life = 1.0f,
                Size = _random.Next(2, 6),
                Color = color
            });
        }
    }

    private void DrawBackgroundParticles(SKCanvas canvas, EnhancedGridState state, VideoSettings settings)
    {
        foreach (var particle in state.Particles)
        {
            using var particlePaint = new SKPaint
            {
                Color = particle.Color.WithAlpha((byte)(particle.Life * 150)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2),
                IsAntialias = true
            };

            canvas.DrawCircle(particle.X, particle.Y, particle.Size * particle.Life, particlePaint);
        }
    }

    private void DrawEnhancedGrid(SKCanvas canvas, EnhancedGridState state, VideoSettings settings, double frameTime)
    {
        var grid = state.Template.Grid;
        var cellWidth = (settings.Width - grid.Border * 2 - grid.Gap * (grid.Cols - 1)) / (float)grid.Cols;
        var cellHeight = (settings.Height - grid.Border * 2 - grid.Gap * (grid.Rows - 1)) / (float)grid.Rows;

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Cols; col++)
            {
                var cell = state.Grid[row, col];
                var x = grid.Border + col * (cellWidth + grid.Gap);
                var y = grid.Border + row * (cellHeight + grid.Gap);

                var centerX = x + cellWidth / 2;
                var centerY = y + cellHeight / 2;

                if (cell.IsAnimating && Math.Abs(cell.CurrentScale - 1.0f) > 0.01f)
                {
                    canvas.Save();
                    canvas.Translate(centerX, centerY);

                    if (cell.CurrentScale > 1.2f)
                    {
                        var rotation = (float)(Math.Sin(frameTime * 10) * 2);
                        canvas.RotateDegrees(rotation);
                    }

                    canvas.Scale(cell.CurrentScale, cell.CurrentScale);
                    canvas.Translate(-centerX, -centerY);
                }

                var colorIndex = GetDynamicColorIndex(row, col, frameTime, state);
                var cellColor = SKColor.Parse(state.Palette.CellColors[colorIndex]);
                cellColor = cellColor.WithAlpha((byte)(255 * cell.CurrentAlpha * cell.Brightness));

                if (cell.IsAnimating && cell.CurrentScale > 1.1f)
                {
                    DrawCellGlow(canvas, x, y, cellWidth, cellHeight, cellColor, cell.CurrentScale);
                }

                DrawGradientCell(canvas, x, y, cellWidth, cellHeight, cellColor, cell.IsAnimating);

                if (cell.IsFlashing)
                {
                    DrawShimmerEffect(canvas, x, y, cellWidth, cellHeight, cell.FlashIntensity);
                }

                if (cell.IsAnimating && Math.Abs(cell.CurrentScale - 1.0f) > 0.01f)
                {
                    canvas.Restore();
                }
            }
        }

        DrawSubtleGridLines(canvas, grid, cellWidth, cellHeight, settings, state.Palette.GridLines);
    }

    private void DrawGradientCell(SKCanvas canvas, float x, float y, float width, float height, SKColor color, bool isAnimating)
    {
        var lightColor = LightenColor(color, 0.3f);
        var darkColor = DarkenColor(color, 0.3f);

        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(x, y),
            new SKPoint(x + width, y + height),
            new[] { lightColor, color, darkColor },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            Shader = gradient,
            IsAntialias = true
        };

        var cornerRadius = isAnimating ? 6f : 4f;
        var rect = new SKRoundRect(new SKRect(x, y, x + width, y + height), cornerRadius, cornerRadius);
        canvas.DrawRoundRect(rect, paint);

        if (isAnimating)
        {
            using var highlightPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(30),
                BlendMode = SKBlendMode.Overlay
            };
            canvas.DrawRoundRect(
                new SKRoundRect(new SKRect(x + 2, y + 2, x + width * 0.3f, y + height * 0.3f), 2, 2),
                highlightPaint);
        }
    }

    private void DrawCellGlow(SKCanvas canvas, float x, float y, float width, float height, SKColor color, float scale)
    {
        var glowRadius = Math.Max(width, height) * 0.5f * (scale - 1.0f);
        var glowColor = color.WithAlpha((byte)(100 * (scale - 1.0f)));

        using var glowPaint = new SKPaint
        {
            Color = glowColor,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, glowRadius),
            IsAntialias = true
        };

        var centerX = x + width / 2;
        var centerY = y + height / 2;
        canvas.DrawCircle(centerX, centerY, width * 0.6f * scale, glowPaint);
    }

    private void DrawShimmerEffect(SKCanvas canvas, float x, float y, float width, float height, float intensity)
    {
        using var shimmerPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(x - width, y),
                new SKPoint(x + width * 2, y + height),
                new[] {
                    SKColors.Transparent,
                    SKColors.White.WithAlpha((byte)(intensity * 150)),
                    SKColors.Transparent
                },
                new[] { 0f, 0.5f, 1f },
                SKShaderTileMode.Clamp),
            BlendMode = SKBlendMode.Overlay
        };

        canvas.DrawRect(x, y, width, height, shimmerPaint);
    }

    private void DrawSubtleGridLines(SKCanvas canvas, GridConfig grid, float cellWidth, float cellHeight, VideoSettings settings, string gridLineColor)
    {
        using var linePaint = new SKPaint
        {
            Color = SKColor.Parse(gridLineColor).WithAlpha(30),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        for (int col = 1; col < grid.Cols; col++)
        {
            var x = grid.Border + col * (cellWidth + grid.Gap) - grid.Gap / 2;
            canvas.DrawLine(x, 0, x, settings.Height, linePaint);
        }

        for (int row = 1; row < grid.Rows; row++)
        {
            var y = grid.Border + row * (cellHeight + grid.Gap) - grid.Gap / 2;
            canvas.DrawLine(0, y, settings.Width, y, linePaint);
        }
    }

    private void DrawOverlayEffects(SKCanvas canvas, EnhancedGridState state, VideoSettings settings, double frameTime)
    {
        using var vignettePaint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(settings.Width / 2, settings.Height / 2),
                Math.Max(settings.Width, settings.Height) * 0.9f,
                new[] { SKColors.Transparent, SKColors.Black.WithAlpha(50) },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, vignettePaint);
    }

    private void DrawSectionIndicator(SKCanvas canvas, EnhancedGridState state, VideoSettings settings, double frameTime)
    {
        var currentSection = GetCurrentSection(state.Timeline, frameTime);
        if (currentSection == null) return;

        var alpha = (byte)(200 + Math.Sin(frameTime * 3) * 55);

        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(alpha),
            TextSize = 24,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        var text = currentSection.Label.ToUpper();
        var textBounds = new SKRect();
        textPaint.MeasureText(text, ref textBounds);

        var x = settings.Width - textBounds.Width - 30;
        var y = 50;

        using var bgPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(100),
            IsAntialias = true
        };

        var pillRect = new SKRoundRect(
            new SKRect(x - 10, y - textBounds.Height - 5, x + textBounds.Width + 10, y + 5),
            20, 20);
        canvas.DrawRoundRect(pillRect, bgPaint);

        canvas.DrawText(text, x, y, textPaint);
    }

    private void DrawBeatFlash(SKCanvas canvas, EnhancedGridState state, VideoSettings settings)
    {
        using var flashPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(30),
            BlendMode = SKBlendMode.Overlay
        };

        canvas.DrawRect(0, 0, settings.Width, settings.Height, flashPaint);
    }

    private void DrawStyledWatermark(SKCanvas canvas, string watermark, VideoSettings settings)
    {
        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(120),
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal)
        };

        var textBounds = new SKRect();
        textPaint.MeasureText(watermark, ref textBounds);

        var x = settings.Width - textBounds.Width - 20;
        var y = settings.Height - 20;

        using var shadowPaint = new SKPaint
        {
            Color = SKColors.Black.WithAlpha(80),
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2)
        };
        canvas.DrawText(watermark, x + 1, y + 1, shadowPaint);

        canvas.DrawText(watermark, x, y, textPaint);
    }

    // === HELPER METHODS ===

    private bool IsBeatFrame(EnhancedGridState state, double frameTime)
    {
        return state.Timeline.Beats.Any(beat => Math.Abs(beat - frameTime) < 0.05);
    }

    private int GetDynamicColorIndex(int row, int col, double frameTime, EnhancedGridState state)
    {
        var wave = Math.Sin(row * 0.3 + col * 0.3 + frameTime * 2) * 0.5 + 0.5;
        var baseIndex = (row * state.Template.Grid.Cols + col) % state.Palette.CellColors.Length;
        var offset = (int)(wave * 3);

        return (baseIndex + offset + (int)(frameTime * 0.5)) % state.Palette.CellColors.Length;
    }

    private SectionEvent? GetCurrentSection(Timeline timeline, double frameTime)
    {
        return timeline.Sections
            .Where(s => s.Time <= frameTime)
            .OrderByDescending(s => s.Time)
            .FirstOrDefault();
    }

    private double ApplyEasing(double t, string easing)
    {
        return easing switch
        {
            "outCubic" => 1 - Math.Pow(1 - t, 3),
            "inOutCubic" => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            _ => t
        };
    }

    private float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private SKColor InterpolateColor(SKColor c1, SKColor c2, float t)
    {
        return new SKColor(
            (byte)(c1.Red + (c2.Red - c1.Red) * t),
            (byte)(c1.Green + (c2.Green - c1.Green) * t),
            (byte)(c1.Blue + (c2.Blue - c1.Blue) * t),
            (byte)(c1.Alpha + (c2.Alpha - c1.Alpha) * t)
        );
    }

    private SKColor LightenColor(SKColor color, float amount)
    {
        return new SKColor(
            (byte)Math.Min(255, color.Red + 255 * amount),
            (byte)Math.Min(255, color.Green + 255 * amount),
            (byte)Math.Min(255, color.Blue + 255 * amount),
            color.Alpha
        );
    }

    private SKColor DarkenColor(SKColor color, float amount)
    {
        return new SKColor(
            (byte)Math.Max(0, color.Red - 255 * amount),
            (byte)Math.Max(0, color.Green - 255 * amount),
            (byte)Math.Max(0, color.Blue - 255 * amount),
            color.Alpha
        );
    }

    // === VIDEO ENCODING ===

    private async Task<string> EncodeHighQualityVideoAsync(
        string[] frameFiles,
        string audioPath,
        VideoSettings settings,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            $"pixbeat_squareboom_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
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

                    var crf = settings.Quality switch
                    {
                        "Draft" => "28",
                        "High" => "18",
                        _ => "22"
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

    // === DATA STRUCTURES ===

    private class EnhancedGridState
    {
        public SquareBoomTemplate Template { get; set; } = new();
        public Timeline Timeline { get; set; } = new();
        public List<GraphicsEvent> GraphicsEvents { get; set; } = new();
        public EnhancedGridCell[,] Grid { get; set; } = new EnhancedGridCell[0, 0];
        public EnhancedPalette Palette { get; set; } = new();
        public AnimationParams AnimParams { get; set; } = new();
        public List<Particle> Particles { get; set; } = new();
        public int RandomSeed { get; set; }
    }

    private class Timeline
    {
        public int SampleRate { get; set; }
        public double Duration { get; set; }
        public double BPM { get; set; }
        public double[] Beats { get; set; } = Array.Empty<double>();
        public List<OnsetEvent> Onsets { get; set; } = new();
        public List<SectionEvent> Sections { get; set; } = new();
        public List<LoudnessEvent> Loudness { get; set; } = new();
        public string Key { get; set; } = "C:maj";
    }

    private class OnsetEvent
    {
        public double Time { get; set; }
        public double Strength { get; set; }
        public double FrequencyProxy { get; set; }
    }

    private class SectionEvent
    {
        public double Time { get; set; }
        public string Label { get; set; } = "";
    }

    private class LoudnessEvent
    {
        public double Time { get; set; }
        public double LUFS { get; set; }
    }

    private class SquareBoomTemplate
    {
        public string Name { get; set; } = "";
        public GridConfig Grid { get; set; } = new();
        public List<MappingRule> Rules { get; set; } = new();
        public ExportConfig Export { get; set; } = new();
        public int RandomSeed { get; set; }
    }

    private class GridConfig
    {
        public int Rows { get; set; }
        public int Cols { get; set; }
        public int Gap { get; set; }
        public int Border { get; set; }
    }

    private class MappingRule
    {
        public string When { get; set; } = "";
        public string Action { get; set; } = "";
        public string Cells { get; set; } = "";
        public float[] ScaleRange { get; set; } = Array.Empty<float>();
        public float[] AlphaRange { get; set; } = Array.Empty<float>();
        public float Duration { get; set; }
        public string Direction { get; set; } = "";
        public string Easing { get; set; } = "linear";
    }

    private class ExportConfig
    {
        public int FPS { get; set; }
        public int[] Size { get; set; } = Array.Empty<int>();
        public string Background { get; set; } = "#000000";
    }

    private class GraphicsEvent
    {
        public double Time { get; set; }
        public string Type { get; set; } = "";
        public List<GridCell> Targets { get; set; } = new();
        public float Duration { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    private class GridCell
    {
        public int Row { get; set; }
        public int Col { get; set; }
    }

    private class EnhancedGridCell : GridCell
    {
        public float CurrentScale { get; set; } = 1.0f;
        public float TargetScale { get; set; } = 1.0f;
        public float CurrentAlpha { get; set; } = 1.0f;
        public float TargetAlpha { get; set; } = 1.0f;
        public float Brightness { get; set; } = 1.0f;
        public bool IsAnimating { get; set; }
        public bool IsFlashing { get; set; }
        public float FlashIntensity { get; set; }
    }

    private class EnhancedPalette
    {
        public string[] Background { get; set; } = Array.Empty<string>();
        public string GridLines { get; set; } = "#1A1A1A";
        public string[] CellColors { get; set; } = Array.Empty<string>();
        public string[] GlowColors { get; set; } = Array.Empty<string>();
    }

    private class AnimationParams
    {
        public float PulseSpeed { get; set; } = 0.18f;
        public float FlashSpeed { get; set; } = 0.12f;
        public float WipeSpeed { get; set; } = 0.6f;
        public float SwellSpeed { get; set; } = 0.4f;
        public float GlowIntensity { get; set; } = 1.0f;
        public float ParticleCount { get; set; } = 20f;
    }

    private class Particle
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public float Life { get; set; }
        public float Size { get; set; }
        public SKColor Color { get; set; }
    }
}