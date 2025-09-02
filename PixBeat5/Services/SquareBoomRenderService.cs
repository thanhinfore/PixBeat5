using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using PixBeat5.Models;
using SkiaSharp;
using System.IO;
using System.Text.Json;

namespace PixBeat5.Services;

/// <summary>
/// Square.Boom style renderer - Grid cells pulsing to music beats
/// Based on PRD spec for TikTok-style satisfying grid videos
/// </summary>
public class SquareBoomRenderService : IRenderService
{
    private readonly ILogger<SquareBoomRenderService> _logger;
    private readonly Random _random;

    // Square.Boom Color Palettes
    private readonly Dictionary<string, SquareBoomPalette> _palettes = new()
    {
        ["Classic"] = new SquareBoomPalette
        {
            Background = "#000000",
            GridLines = "#1A1A1A",
            CellColors = new[] { "#FFFFFF", "#00FFA3", "#00C2FF", "#FFD60A", "#FF4D4D", "#FF00FF", "#00FF00", "#FF8C00" }
        },
        ["Neon"] = new SquareBoomPalette
        {
            Background = "#0A0A0A",
            GridLines = "#222222",
            CellColors = new[] { "#FF006E", "#FB5607", "#FFBE0B", "#06FFB4", "#8338EC", "#3A86FF", "#FF4365", "#00F5FF" }
        },
        ["Pastel"] = new SquareBoomPalette
        {
            Background = "#1E1E1E",
            GridLines = "#2A2A2A",
            CellColors = new[] { "#FFB3BA", "#FFDFBA", "#FFFFBA", "#BAFFC9", "#BAE1FF", "#E0BBE4", "#FEC8D8", "#FFDFD3" }
        },
        ["Monochrome"] = new SquareBoomPalette
        {
            Background = "#000000",
            GridLines = "#111111",
            CellColors = new[] { "#FFFFFF", "#E0E0E0", "#C0C0C0", "#A0A0A0", "#808080", "#606060", "#404040", "#202020" }
        }
    };

    public SquareBoomRenderService(ILogger<SquareBoomRenderService> logger)
    {
        _logger = logger;
        _random = new Random();
    }

    public async Task<string> RenderVideoAsync(ProjectData project, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (project.Audio == null)
            throw new ArgumentException("Project must have audio data");

        _logger.LogInformation("Starting Square.Boom render for: {ProjectName}", project.Name);

        var tempDir = Path.Combine(Path.GetTempPath(), "pixbeat_squareboom", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Generate Timeline from audio analysis
            var timeline = GenerateTimeline(project.Audio);

            // 2. Select template configuration
            var template = CreateSquareBoomTemplate(project);

            // 3. Map timeline events to graphics events
            var graphicsEvents = MapTimelineToGraphics(timeline, template);

            // 4. Initialize grid state
            var gridState = new SquareBoomState
            {
                Template = template,
                Timeline = timeline,
                GraphicsEvents = graphicsEvents,
                Grid = InitializeGrid(template.Grid),
                Palette = SelectPalette(project.Audio.Mood),
                RandomSeed = template.RandomSeed
            };

            // 5. Calculate total frames
            var totalFrames = (int)(project.Settings.Duration.TotalSeconds * project.Settings.Fps);
            var renderProgress = new RenderProgress
            {
                TotalFrames = totalFrames,
                Stage = "Generating Square.Boom Frames"
            };

            // 6. Generate frames
            var frameFiles = await GenerateSquareBoomFramesAsync(
                project, tempDir, gridState, renderProgress, progress, cancellationToken);

            // 7. Encode video with audio
            renderProgress.Stage = "Encoding Square.Boom Video";
            progress?.Report(renderProgress);

            var outputPath = await EncodeVideoAsync(
                frameFiles, project.Audio.FilePath, project.Settings, cancellationToken);

            _logger.LogInformation("Square.Boom video complete: {OutputPath}", outputPath);
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

    // === TIMELINE GENERATION (per PRD spec) ===

    private Timeline GenerateTimeline(AudioData audio)
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

        // Generate sections (simplified - detect energy changes)
        timeline.Sections = DetectSections(audio.EnergyLevels, audio.Duration);

        // Generate loudness timeline
        timeline.Loudness = GenerateLoudnessTimeline(audio.EnergyLevels, audio.Duration);

        return timeline;
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

            // Detect sudden energy increases as onsets
            if (energyDiff > 0.2)
            {
                onsets.Add(new OnsetEvent
                {
                    Time = i * timeStep,
                    Strength = Math.Min(1.0, energyDiff * 2),
                    FrequencyProxy = 2000 + _random.Next(0, 4000) // Simplified frequency estimation
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

        // Simple section detection based on energy levels
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

        // Add outro
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
                LUFS = -30 + energyLevels[i] * 20 // Convert energy to pseudo-LUFS
            });
        }

        return loudness;
    }

    // === TEMPLATE CREATION ===

    private SquareBoomTemplate CreateSquareBoomTemplate(ProjectData project)
    {
        var template = new SquareBoomTemplate
        {
            Name = "square-boom-grid",
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
                FPS = project.Settings.Fps,
                Size = new[] { project.Settings.Width, project.Settings.Height },
                Background = "#000000"
            },
            RandomSeed = _random.Next(10000, 99999)
        };

        return template;
    }

    // === EVENT MAPPING (Timeline → Graphics) ===

    private List<GraphicsEvent> MapTimelineToGraphics(Timeline timeline, SquareBoomTemplate template)
    {
        var events = new List<GraphicsEvent>();
        var rand = new Random(template.RandomSeed);

        // Map beats to pulse events
        foreach (var beat in timeline.Beats)
        {
            var cellCount = rand.Next(4, 9); // 4-8 cells
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
            // Map frequency to row (high freq = top rows)
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
                    ["direction"] = "lr"
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

        // Sort events by time
        events.Sort((a, b) => a.Time.CompareTo(b.Time));

        return events;
    }

    private int MapFrequencyToRow(double frequency, int rows)
    {
        // Map frequency range 100-8000 Hz to row index
        // High frequencies = top rows (0), low frequencies = bottom rows (rows-1)
        var normalized = Math.Log10(frequency / 100) / Math.Log10(80); // 0 to 1
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

    // === FRAME GENERATION ===

    private async Task<string[]> GenerateSquareBoomFramesAsync(
        ProjectData project,
        string outputDir,
        SquareBoomState state,
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

                GenerateSquareBoomFrame(project, state, frameTime, frameFile, frameIndex);
                frameFiles[frameIndex] = frameFile;

                if (frameIndex % 10 == 0)
                {
                    renderProgress.CurrentFrame = frameIndex;
                    renderProgress.Stage = $"Rendering Square.Boom Frame {frameIndex}/{renderProgress.TotalFrames}";
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

    private void GenerateSquareBoomFrame(
        ProjectData project,
        SquareBoomState state,
        double frameTime,
        string outputPath,
        int frameIndex)
    {
        using var surface = SKSurface.Create(new SKImageInfo(project.Settings.Width, project.Settings.Height));
        var canvas = surface.Canvas;

        // Clear background
        canvas.Clear(SKColor.Parse(state.Palette.Background));

        // Update grid state based on active events
        UpdateGridState(state, frameTime);

        // Draw the grid
        DrawSquareBoomGrid(canvas, state, project.Settings, frameTime);

        // Draw overlay elements (optional)
        if (project.Settings.Watermark != null)
        {
            DrawGuessTheSongOverlay(canvas, project.Settings, frameTime, state);
        }

        // Watermark
        DrawWatermark(canvas, project.Settings.Watermark ?? "PixBeat", project.Settings);

        // Save frame
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outputPath, data.ToArray());
    }

    private void UpdateGridState(SquareBoomState state, double frameTime)
    {
        // Reset grid animations
        foreach (var cell in state.Grid)
        {
            cell.CurrentScale = 1.0f;
            cell.CurrentAlpha = 1.0f;
            cell.IsAnimating = false;
        }

        // Apply active events
        foreach (var evt in state.GraphicsEvents)
        {
            if (frameTime >= evt.Time && frameTime < evt.Time + evt.Duration)
            {
                var progress = (frameTime - evt.Time) / evt.Duration;
                ApplyEventToGrid(state, evt, progress);
            }
        }
    }

    private void ApplyEventToGrid(SquareBoomState state, GraphicsEvent evt, double progress)
    {
        var easedProgress = ApplyEasing(progress, evt.Parameters.GetValueOrDefault("easing", "linear").ToString()!);

        switch (evt.Type)
        {
            case "pulse":
                foreach (var target in evt.Targets)
                {
                    var cell = state.Grid[target.Row, target.Col];
                    var scale = (float)evt.Parameters.GetValueOrDefault("scale", 1.6f);
                    cell.CurrentScale = 1.0f + (scale - 1.0f) * (1.0f - (float)easedProgress);
                    cell.IsAnimating = true;
                }
                break;

            case "flash":
                foreach (var target in evt.Targets)
                {
                    var cell = state.Grid[target.Row, target.Col];
                    var alpha = (float)evt.Parameters.GetValueOrDefault("alpha", 1.0f);

                    // Flash: fade in then out
                    if (progress < 0.5)
                        cell.CurrentAlpha = (float)(alpha * progress * 2);
                    else
                        cell.CurrentAlpha = (float)(alpha * (2 - progress * 2));

                    cell.IsAnimating = true;
                }
                break;

            case "wipe":
                var direction = evt.Parameters.GetValueOrDefault("direction", "lr").ToString();
                ApplyWipeEffect(state, direction!, easedProgress);
                break;

            case "swell":
                var swellScale = (float)evt.Parameters.GetValueOrDefault("scale", 1.1f);
                foreach (var target in evt.Targets)
                {
                    var cell = state.Grid[target.Row, target.Col];
                    cell.CurrentScale = 1.0f + (swellScale - 1.0f) * (float)Math.Sin(easedProgress * Math.PI);
                    cell.IsAnimating = true;
                }
                break;
        }
    }

    private void ApplyWipeEffect(SquareBoomState state, string direction, double progress)
    {
        var rows = state.Template.Grid.Rows;
        var cols = state.Template.Grid.Cols;

        switch (direction)
        {
            case "lr": // Left to right
                for (int col = 0; col < cols; col++)
                {
                    var colProgress = (double)col / cols;
                    if (progress > colProgress)
                    {
                        for (int row = 0; row < rows; row++)
                        {
                            var cell = state.Grid[row, col];
                            cell.CurrentAlpha = (float)Math.Min(1, (progress - colProgress) * cols);
                            cell.IsAnimating = true;
                        }
                    }
                }
                break;

            case "tb": // Top to bottom
                for (int row = 0; row < rows; row++)
                {
                    var rowProgress = (double)row / rows;
                    if (progress > rowProgress)
                    {
                        for (int col = 0; col < cols; col++)
                        {
                            var cell = state.Grid[row, col];
                            cell.CurrentAlpha = (float)Math.Min(1, (progress - rowProgress) * rows);
                            cell.IsAnimating = true;
                        }
                    }
                }
                break;
        }
    }

    private double ApplyEasing(double t, string easing)
    {
        return easing switch
        {
            "outCubic" => 1 - Math.Pow(1 - t, 3),
            "inOutCubic" => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            "outElastic" => t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * ((2 * Math.PI) / 3)) + 1,
            _ => t // linear
        };
    }

    // === RENDERING ===

    private void DrawSquareBoomGrid(SKCanvas canvas, SquareBoomState state, VideoSettings settings, double frameTime)
    {
        var grid = state.Template.Grid;
        var cellWidth = (settings.Width - grid.Border * 2 - grid.Gap * (grid.Cols - 1)) / (float)grid.Cols;
        var cellHeight = (settings.Height - grid.Border * 2 - grid.Gap * (grid.Rows - 1)) / (float)grid.Rows;

        // Draw grid background
        using var bgPaint = new SKPaint
        {
            Color = SKColor.Parse(state.Palette.GridLines),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(0, 0, settings.Width, settings.Height, bgPaint);

        // Draw cells
        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Cols; col++)
            {
                var cell = state.Grid[row, col];
                var x = grid.Border + col * (cellWidth + grid.Gap);
                var y = grid.Border + row * (cellHeight + grid.Gap);

                // Calculate cell center for scaling
                var centerX = x + cellWidth / 2;
                var centerY = y + cellHeight / 2;

                // Apply scaling transform if animating
                if (cell.IsAnimating && Math.Abs(cell.CurrentScale - 1.0f) > 0.01f)
                {
                    canvas.Save();
                    canvas.Translate(centerX, centerY);
                    canvas.Scale(cell.CurrentScale, cell.CurrentScale);
                    canvas.Translate(-centerX, -centerY);
                }

                // Select color based on cell state
                var colorIndex = (row * grid.Cols + col + (int)(frameTime * 2)) % state.Palette.CellColors.Length;
                var cellColor = SKColor.Parse(state.Palette.CellColors[colorIndex]);

                // Apply alpha if flashing
                if (cell.IsAnimating && cell.CurrentAlpha < 1.0f)
                {
                    cellColor = cellColor.WithAlpha((byte)(255 * cell.CurrentAlpha));
                }

                // Draw cell with rounded corners
                using var cellPaint = new SKPaint
                {
                    Color = cellColor,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };

                var cellRect = new SKRoundRect(new SKRect(x, y, x + cellWidth, y + cellHeight), 4, 4);
                canvas.DrawRoundRect(cellRect, cellPaint);

                // Add subtle gradient overlay for depth
                if (cell.IsAnimating)
                {
                    using var gradientPaint = new SKPaint
                    {
                        Shader = SKShader.CreateLinearGradient(
                            new SKPoint(x, y),
                            new SKPoint(x + cellWidth, y + cellHeight),
                            new[] { SKColors.White.WithAlpha(40), SKColors.Transparent },
                            SKShaderTileMode.Clamp),
                        BlendMode = SKBlendMode.Overlay
                    };
                    canvas.DrawRoundRect(cellRect, gradientPaint);
                }

                // Restore transform
                if (cell.IsAnimating && Math.Abs(cell.CurrentScale - 1.0f) > 0.01f)
                {
                    canvas.Restore();
                }
            }
        }

        // Draw grid lines (subtle)
        using var linePaint = new SKPaint
        {
            Color = SKColor.Parse(state.Palette.GridLines).WithAlpha(30),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        // Vertical lines
        for (int col = 1; col < grid.Cols; col++)
        {
            var x = grid.Border + col * (cellWidth + grid.Gap) - grid.Gap / 2;
            canvas.DrawLine(x, 0, x, settings.Height, linePaint);
        }

        // Horizontal lines
        for (int row = 1; row < grid.Rows; row++)
        {
            var y = grid.Border + row * (cellHeight + grid.Gap) - grid.Gap / 2;
            canvas.DrawLine(0, y, settings.Width, y, linePaint);
        }
    }

    private void DrawGuessTheSongOverlay(SKCanvas canvas, VideoSettings settings, double frameTime, SquareBoomState state)
    {
        // Find if we're in a chorus section
        var currentSection = state.Timeline.Sections
            .LastOrDefault(s => s.Time <= frameTime);

        if (currentSection?.Label == "chorus")
        {
            // Draw "Guess the Song" text with animation
            var progress = Math.Sin(frameTime * 2) * 0.5 + 0.5;

            using var textPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(200 + progress * 55)),
                TextSize = 48,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };

            var text = "🎵 Guess the Song! 🎵";
            var textBounds = new SKRect();
            textPaint.MeasureText(text, ref textBounds);

            var x = (settings.Width - textBounds.Width) / 2;
            var y = 100;

            // Shadow
            using var shadowPaint = new SKPaint
            {
                Color = SKColors.Black.WithAlpha(150),
                TextSize = 48,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5)
            };
            canvas.DrawText(text, x + 3, y + 3, shadowPaint);

            // Main text
            canvas.DrawText(text, x, y, textPaint);
        }
    }

    private void DrawWatermark(SKCanvas canvas, string watermark, VideoSettings settings)
    {
        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(100),
            TextSize = 18,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        var textBounds = new SKRect();
        textPaint.MeasureText(watermark, ref textBounds);

        var x = settings.Width - textBounds.Width - 15;
        var y = settings.Height - 15;

        canvas.DrawText(watermark, x, y, textPaint);
    }

    // === HELPER METHODS ===

    private SquareBoomPalette SelectPalette(string mood)
    {
        return mood.ToLower() switch
        {
            "energetic" => _palettes["Neon"],
            "happy" => _palettes["Classic"],
            "calm" => _palettes["Pastel"],
            _ => _palettes["Monochrome"]
        };
    }

    private GridCellState[,] InitializeGrid(GridConfig config)
    {
        var grid = new GridCellState[config.Rows, config.Cols];

        for (int row = 0; row < config.Rows; row++)
        {
            for (int col = 0; col < config.Cols; col++)
            {
                grid[row, col] = new GridCellState
                {
                    Row = row,
                    Col = col,
                    CurrentScale = 1.0f,
                    CurrentAlpha = 1.0f,
                    IsAnimating = false
                };
            }
        }

        return grid;
    }

    private async Task<string> EncodeVideoAsync(
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

                    // Quality based on settings
                    var crf = settings.Quality switch
                    {
                        "Draft" => "28",
                        "High" => "18",
                        _ => "22" // Standard
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

    // === DATA STRUCTURES (per PRD spec) ===

    private class SquareBoomState
    {
        public SquareBoomTemplate Template { get; set; } = new();
        public Timeline Timeline { get; set; } = new();
        public List<GraphicsEvent> GraphicsEvents { get; set; } = new();
        public GridCellState[,] Grid { get; set; } = new GridCellState[0, 0];
        public SquareBoomPalette Palette { get; set; } = new();
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

    private class GridCellState : GridCell
    {
        public float CurrentScale { get; set; }
        public float CurrentAlpha { get; set; }
        public bool IsAnimating { get; set; }
    }

    private class SquareBoomPalette
    {
        public string Background { get; set; } = "#000000";
        public string GridLines { get; set; } = "#1A1A1A";
        public string[] CellColors { get; set; } = Array.Empty<string>();
    }
}