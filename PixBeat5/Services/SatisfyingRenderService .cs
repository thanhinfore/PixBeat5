using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using PixBeat5.Models;
using SkiaSharp;
using System.IO;
using System.Text.Json;

namespace PixBeat5.Services;

public class SatisfyingRenderService : IRenderService
{
    private readonly ILogger<SatisfyingRenderService> _logger;
    private readonly Random _random = new();

    // Satisfying color palettes
    private readonly Dictionary<string, SatisfyingPalette> _palettes = new()
    {
        ["Pastel"] = new SatisfyingPalette
        {
            Colors = new[] { "#FFB3BA", "#FFDFBA", "#FFFFBA", "#BAFFC9", "#BAE1FF", "#E0BBE4", "#FEC8D8", "#FFDFD3" },
            Background = "#2C2C2C"
        },
        ["Neon"] = new SatisfyingPalette
        {
            Colors = new[] { "#FF006E", "#FB5607", "#FFBE0B", "#06FFB4", "#8338EC", "#3A86FF", "#FF4365", "#00F5FF" },
            Background = "#0A0A0A"
        },
        ["Rainbow"] = new SatisfyingPalette
        {
            Colors = new[] { "#FF0000", "#FF7F00", "#FFFF00", "#00FF00", "#0000FF", "#4B0082", "#9400D3", "#FF1493" },
            Background = "#1A1A1A"
        },
        ["Candy"] = new SatisfyingPalette
        {
            Colors = new[] { "#FF69B4", "#FFB6C1", "#FFC0CB", "#FF1493", "#C71585", "#DB7093", "#FF6EC7", "#FFD700" },
            Background = "#2D1B2E"
        }
    };

    public SatisfyingRenderService(ILogger<SatisfyingRenderService> logger)
    {
        _logger = logger;
    }

    public async Task<string> RenderVideoAsync(ProjectData project, IProgress<RenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (project.Audio == null)
            throw new ArgumentException("Project must have audio data");

        _logger.LogInformation("Starting SATISFYING video render for: {ProjectName}", project.Name);

        var tempDir = Path.Combine(Path.GetTempPath(), "pixbeat_satisfying", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Select palette based on mood
            var palette = SelectPalette(project.Audio.Mood);

            // Initialize satisfying elements
            var satisfyingState = new SatisfyingState
            {
                Palette = palette,
                PixelBlocks = new List<PixelBlock>(),
                FallingBlocks = new List<FallingBlock>(),
                Particles = new List<SatisfyingParticle>(),
                WavePoints = new List<WavePoint>(),
                GridCells = InitializeGrid(20, 30),
                BeatGrid = GenerateBeatGrid(project.Audio)
            };

            // Calculate frames
            var totalFrames = (int)(project.Settings.Duration.TotalSeconds * project.Settings.Fps);
            var renderProgress = new RenderProgress { TotalFrames = totalFrames, Stage = "Generating Satisfying Frames" };

            // Generate frames
            var frameFiles = await GenerateSatisfyingFramesAsync(
                project, tempDir, satisfyingState, renderProgress, progress, cancellationToken);

            // Encode video
            renderProgress.Stage = "Encoding Satisfying Video";
            progress?.Report(renderProgress);

            var outputPath = await EncodeVideoAsync(frameFiles, project.Audio.FilePath, project.Settings, cancellationToken);

            _logger.LogInformation("Satisfying video complete: {OutputPath}", outputPath);
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

    private async Task<string[]> GenerateSatisfyingFramesAsync(
        ProjectData project,
        string outputDir,
        SatisfyingState state,
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

                GenerateSatisfyingFrame(project, frameTime, frameFile, state, frameIndex);
                frameFiles[frameIndex] = frameFile;

                if (frameIndex % 10 == 0)
                {
                    renderProgress.CurrentFrame = frameIndex;
                    renderProgress.Stage = $"Creating Satisfying Frame {frameIndex}/{renderProgress.TotalFrames}";
                    renderProgress.Elapsed = DateTime.Now - startTime;

                    if (frameIndex > 0)
                    {
                        var avgTimePerFrame = renderProgress.Elapsed.TotalMilliseconds / frameIndex;
                        renderProgress.Estimated = TimeSpan.FromMilliseconds((renderProgress.TotalFrames - frameIndex) * avgTimePerFrame);
                    }

                    progress?.Report(renderProgress);
                }
            });
        }, cancellationToken);

        return frameFiles;
    }

    private void GenerateSatisfyingFrame(
        ProjectData project,
        double frameTime,
        string outputPath,
        SatisfyingState state,
        int frameIndex)
    {
        using var surface = SKSurface.Create(new SKImageInfo(project.Settings.Width, project.Settings.Height));
        var canvas = surface.Canvas;

        // Parse background color
        var bgColor = SKColor.Parse(state.Palette.Background);
        canvas.Clear(bgColor);

        // Check if on beat
        var beatInfo = GetBeatInfo(frameTime, state.BeatGrid, project.Audio!);

        // Update physics and spawn new blocks on beat
        UpdateSatisfyingPhysics(state, frameTime, beatInfo, project.Settings);

        // Draw based on template
        switch (project.Template)
        {
            case "pixel_runner":
                DrawSatisfyingPixelRunner(canvas, project, state, frameTime, beatInfo, frameIndex);
                break;
            case "equalizer":
                DrawSatisfyingEqualizer(canvas, project, state, frameTime, beatInfo, frameIndex);
                break;
            case "waveform":
                DrawSatisfyingWaveform(canvas, project, state, frameTime, beatInfo, frameIndex);
                break;
            default:
                DrawSatisfyingPixelRunner(canvas, project, state, frameTime, beatInfo, frameIndex);
                break;
        }

        // Always draw satisfying elements on top
        DrawSatisfyingElements(canvas, state, project.Settings, frameTime);

        // Watermark
        if (!string.IsNullOrEmpty(project.Settings.Watermark))
        {
            DrawSatisfyingWatermark(canvas, project.Settings.Watermark, project.Settings);
        }

        // Save frame
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outputPath, data.ToArray());
    }

    private void DrawSatisfyingPixelRunner(
        SKCanvas canvas,
        ProjectData project,
        SatisfyingState state,
        double frameTime,
        BeatInfo beatInfo,
        int frameIndex)
    {
        var settings = project.Settings;

        // Draw pixel grid background
        DrawPixelGrid(canvas, settings, state, frameTime);

        // Draw stacked blocks (like Tetris)
        DrawStackedBlocks(canvas, state, settings);

        // Draw falling blocks
        DrawFallingBlocks(canvas, state, settings, beatInfo);

        // Draw bouncing balls
        DrawBouncingBalls(canvas, state, settings, frameTime, beatInfo);

        // Draw satisfying wave
        DrawSatisfyingWave(canvas, state, settings, frameTime);

        // Draw pixel character that jumps on beats
        if (beatInfo.OnBeat)
        {
            SpawnJumpingPixel(state, settings, beatInfo);
        }
        DrawJumpingPixels(canvas, state, settings, frameTime);
    }

    private void DrawSatisfyingEqualizer(
        SKCanvas canvas,
        ProjectData project,
        SatisfyingState state,
        double frameTime,
        BeatInfo beatInfo,
        int frameIndex)
    {
        var settings = project.Settings;
        var audio = project.Audio!;

        // Draw gradient background mesh
        DrawGradientMesh(canvas, settings, state, frameTime);

        // Draw satisfying equalizer bars that grow/shrink smoothly
        var barCount = 32;
        var barWidth = settings.Width / (float)barCount;
        var maxHeight = settings.Height * 0.7f;

        var currentEnergy = GetEnergyAtTime(audio, frameTime);

        for (int i = 0; i < barCount; i++)
        {
            // Smooth wave animation
            var frequency = (i + 1) / (float)barCount;
            var phase = frameTime * 2 + i * 0.3;
            var wave = (Math.Sin(phase) + 1) * 0.5; // 0 to 1

            // Beat pulse
            var beatPulse = beatInfo.OnBeat ? 1.0f + beatInfo.Strength * 0.5f : 1.0f;

            // Calculate height with smooth interpolation
            var targetHeight = (float)(currentEnergy * maxHeight * wave * beatPulse);
            targetHeight = Math.Max(50, targetHeight);

            // Store and smooth heights
            if (!state.BarHeights.ContainsKey(i))
                state.BarHeights[i] = targetHeight;
            else
                state.BarHeights[i] = Lerp(state.BarHeights[i], targetHeight, 0.15f);

            var barHeight = state.BarHeights[i];
            var x = i * barWidth + 4;
            var y = (settings.Height - barHeight) / 2;
            var width = barWidth - 8;

            // Pick color from palette
            var colorIndex = i % state.Palette.Colors.Length;
            var barColor = SKColor.Parse(state.Palette.Colors[colorIndex]);

            // Draw rounded bar with gradient
            using var barGradient = SKShader.CreateLinearGradient(
                new SKPoint(x, y),
                new SKPoint(x, y + barHeight),
                new[] { barColor, barColor.WithAlpha(150) },
                SKShaderTileMode.Clamp);

            using var barPaint = new SKPaint
            {
                Shader = barGradient,
                IsAntialias = true
            };

            var rect = new SKRoundRect(new SKRect(x, y, x + width, y + barHeight), 8, 8);
            canvas.DrawRoundRect(rect, barPaint);

            // Draw glow for tall bars
            if (barHeight > maxHeight * 0.6f)
            {
                using var glowPaint = new SKPaint
                {
                    Color = barColor.WithAlpha(60),
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12),
                    IsAntialias = true
                };
                canvas.DrawRoundRect(rect, glowPaint);
            }

            // Drop particles from top of high bars
            if (beatInfo.OnBeat && barHeight > maxHeight * 0.7f)
            {
                SpawnBarParticles(state, x + width / 2, y, barColor);
            }
        }

        // Draw falling particles
        DrawFallingParticles(canvas, state, settings);
    }

    private void DrawSatisfyingWaveform(
        SKCanvas canvas,
        ProjectData project,
        SatisfyingState state,
        double frameTime,
        BeatInfo beatInfo,
        int frameIndex)
    {
        var settings = project.Settings;
        var audio = project.Audio!;

        // Draw circular gradient background
        DrawCircularGradient(canvas, settings, state, frameTime);

        // Draw multiple waveform layers
        var centerY = settings.Height / 2f;

        for (int layer = 0; layer < 3; layer++)
        {
            using var waveformPath = new SKPath();
            var points = 200;
            var amplitude = settings.Height * 0.2f * (1 - layer * 0.2f);

            for (int i = 0; i < points; i++)
            {
                var x = (i / (float)(points - 1)) * settings.Width;
                var time = frameTime + (i - points / 2) * 0.02 + layer * 0.1;

                if (time >= 0 && time < audio.Duration.TotalSeconds)
                {
                    var energy = GetEnergyAtTime(audio, time);

                    // Multiple sine waves for organic feel
                    var wave1 = Math.Sin(time * 5 + i * 0.1) * 0.3;
                    var wave2 = Math.Cos(time * 3 + i * 0.05) * 0.2;
                    var wave3 = Math.Sin(time * 7) * 0.1;

                    // Beat enhancement
                    var beatEnhance = beatInfo.OnBeat ? beatInfo.Strength * 0.3 : 0;

                    var y = centerY + (float)(energy * amplitude * (1 + wave1 + wave2 + wave3 + beatEnhance));

                    if (i == 0)
                        waveformPath.MoveTo(x, y);
                    else
                    {
                        // Smooth cubic curves
                        var prevX = ((i - 1) / (float)(points - 1)) * settings.Width;
                        var midX = (prevX + x) / 2;
                        waveformPath.CubicTo(midX, y, midX, y, x, y);
                    }
                }
            }

            // Mirror waveform
            using var mirrorPath = new SKPath();
            mirrorPath.AddPath(waveformPath);
            mirrorPath.Transform(SKMatrix.CreateScale(1, -1, 0, centerY));

            // Draw with layer-specific color
            var colorIndex = (layer + (int)(frameTime * 0.5)) % state.Palette.Colors.Length;
            var waveColor = SKColor.Parse(state.Palette.Colors[colorIndex]);

            using var wavePaint = new SKPaint
            {
                Color = waveColor.WithAlpha((byte)(200 - layer * 50)),
                StrokeWidth = 4 - layer,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
                PathEffect = layer == 0 ? null : SKPathEffect.CreateDash(new[] { 10f, 5f }, (float)(frameTime * 20))
            };

            canvas.DrawPath(waveformPath, wavePaint);
            canvas.DrawPath(mirrorPath, wavePaint);

            // Fill gradient under waveform
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
        }

        // Draw orbiting particles
        DrawOrbitingParticles(canvas, state, settings, frameTime, beatInfo);

        // Center pulse on beat
        if (beatInfo.OnBeat)
        {
            DrawCenterPulse(canvas, settings, beatInfo, state);
        }
    }

    // === SATISFYING HELPER METHODS ===

    private void UpdateSatisfyingPhysics(SatisfyingState state, double frameTime, BeatInfo beatInfo, VideoSettings settings)
    {
        // Spawn new falling blocks on beat
        if (beatInfo.OnBeat && state.FallingBlocks.Count < 20)
        {
            var colorIndex = _random.Next(state.Palette.Colors.Length);
            state.FallingBlocks.Add(new FallingBlock
            {
                X = _random.Next(2, 18) * 40f, // Snap to grid
                Y = -50,
                VY = 0,
                Width = 40,
                Height = 40,
                Color = SKColor.Parse(state.Palette.Colors[colorIndex]),
                Rotation = 0
            });
        }

        // Update falling blocks
        var gravity = 500f; // pixels per second^2
        var deltaTime = 1f / 30f; // Assume 30 FPS

        for (int i = state.FallingBlocks.Count - 1; i >= 0; i--)
        {
            var block = state.FallingBlocks[i];

            // Apply gravity
            block.VY += gravity * deltaTime;
            block.Y += block.VY * deltaTime;

            // Rotation
            block.Rotation += 90 * deltaTime;

            // Check collision with bottom or stacked blocks
            var landingY = (float)(settings.Height - 100);

            // Check collision with stacked blocks
            foreach (var stacked in state.PixelBlocks)
            {
                if (Math.Abs(block.X - stacked.X) < 35 && block.Y + block.Height > stacked.Y)
                {
                    landingY = Math.Min(landingY, stacked.Y - block.Height);
                }
            }

            // Land and convert to stacked block
            if (block.Y >= landingY)
            {
                block.Y = landingY;

                // Add bounce effect
                if (block.VY > 100)
                {
                    // Create bounce particles
                    for (int p = 0; p < 5; p++)
                    {
                        state.Particles.Add(new SatisfyingParticle
                        {
                            X = block.X + block.Width / 2,
                            Y = block.Y + block.Height,
                            VX = (float)(_random.NextDouble() * 200 - 100),
                            VY = (float)(_random.NextDouble() * -300 - 100),
                            Life = 1.0f,
                            Color = block.Color,
                            Size = 5
                        });
                    }
                }

                // Convert to stacked block
                state.PixelBlocks.Add(new PixelBlock
                {
                    X = block.X,
                    Y = block.Y,
                    Width = block.Width,
                    Height = block.Height,
                    Color = block.Color
                });

                state.FallingBlocks.RemoveAt(i);

                // Remove old stacked blocks if too many
                if (state.PixelBlocks.Count > 50)
                {
                    state.PixelBlocks.RemoveAt(0);
                }
            }
        }

        // Update particles
        for (int i = state.Particles.Count - 1; i >= 0; i--)
        {
            var particle = state.Particles[i];

            particle.VY += gravity * deltaTime * 0.5f; // Half gravity for particles
            particle.X += particle.VX * deltaTime;
            particle.Y += particle.VY * deltaTime;
            particle.Life -= deltaTime * 2; // Fade out over 0.5 seconds

            if (particle.Life <= 0 || particle.Y > settings.Height)
            {
                state.Particles.RemoveAt(i);
            }
        }
    }

    private void DrawSatisfyingElements(SKCanvas canvas, SatisfyingState state, VideoSettings settings, double frameTime)
    {
        // Draw grid overlay
        using var gridPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(10),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke
        };

        var gridSize = 40;
        for (int x = 0; x < settings.Width; x += gridSize)
        {
            canvas.DrawLine(x, 0, x, settings.Height, gridPaint);
        }
        for (int y = 0; y < settings.Height; y += gridSize)
        {
            canvas.DrawLine(0, y, settings.Width, y, gridPaint);
        }
    }

    private void DrawPixelGrid(SKCanvas canvas, VideoSettings settings, SatisfyingState state, double frameTime)
    {
        var cellSize = 40;
        var pulse = (float)(Math.Sin(frameTime * 2) * 0.5 + 0.5);

        using var gridPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = false
        };

        for (int x = 0; x < settings.Width; x += cellSize)
        {
            for (int y = 0; y < settings.Height; y += cellSize)
            {
                var distance = Math.Sqrt(Math.Pow(x - settings.Width / 2, 2) + Math.Pow(y - settings.Height / 2, 2));
                var delay = distance / 1000;
                var localPulse = (float)(Math.Sin(frameTime * 2 - delay) * 0.5 + 0.5);

                gridPaint.Color = SKColors.White.WithAlpha((byte)(10 + localPulse * 20));
                canvas.DrawRect(x, y, cellSize, cellSize, gridPaint);
            }
        }
    }

    private void DrawStackedBlocks(SKCanvas canvas, SatisfyingState state, VideoSettings settings)
    {
        foreach (var block in state.PixelBlocks)
        {
            using var blockPaint = new SKPaint
            {
                Color = block.Color,
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };

            // Main block
            var rect = new SKRoundRect(new SKRect(block.X, block.Y, block.X + block.Width, block.Y + block.Height), 4, 4);
            canvas.DrawRoundRect(rect, blockPaint);

            // Inner highlight
            using var highlightPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(50),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(block.X + 4, block.Y + 4, 8, 8, highlightPaint);

            // Shadow
            using var shadowPaint = new SKPaint
            {
                Color = SKColors.Black.WithAlpha(30),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4)
            };
            canvas.DrawRoundRect(rect, shadowPaint);
        }
    }

    private void DrawFallingBlocks(SKCanvas canvas, SatisfyingState state, VideoSettings settings, BeatInfo beatInfo)
    {
        foreach (var block in state.FallingBlocks)
        {
            canvas.Save();
            canvas.Translate(block.X + block.Width / 2, block.Y + block.Height / 2);
            canvas.RotateDegrees(block.Rotation);

            using var blockPaint = new SKPaint
            {
                Color = block.Color,
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };

            // Draw block with slight transparency when falling fast
            var speed = Math.Min(Math.Abs(block.VY) / 500f, 1f);
            blockPaint.Color = block.Color.WithAlpha((byte)(255 - speed * 50));

            var rect = new SKRoundRect(
                new SKRect(-block.Width / 2, -block.Height / 2, block.Width / 2, block.Height / 2),
                4, 4);
            canvas.DrawRoundRect(rect, blockPaint);

            // Motion blur effect
            if (block.VY > 200)
            {
                using var blurPaint = new SKPaint
                {
                    Color = block.Color.WithAlpha(50),
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, block.VY / 50f)
                };
                canvas.DrawRoundRect(rect, blurPaint);
            }

            canvas.Restore();
        }
    }

    private void DrawBouncingBalls(SKCanvas canvas, SatisfyingState state, VideoSettings settings, double frameTime, BeatInfo beatInfo)
    {
        // Spawn bouncing balls on beat
        if (beatInfo.OnBeat && state.BouncingBalls.Count < 10)
        {
            var colorIndex = _random.Next(state.Palette.Colors.Length);
            state.BouncingBalls.Add(new BouncingBall
            {
                X = _random.Next(100, settings.Width - 100),
                Y = 100,
                VX = (float)(_random.NextDouble() * 400 - 200),
                VY = 0,
                Radius = 20,
                Color = SKColor.Parse(state.Palette.Colors[colorIndex]),
                Trail = new List<SKPoint>()
            });
        }

        // Update and draw balls
        var deltaTime = 1f / 30f;
        var gravity = 800f;
        var bounce = 0.8f;

        for (int i = state.BouncingBalls.Count - 1; i >= 0; i--)
        {
            var ball = state.BouncingBalls[i];

            // Physics
            ball.VY += gravity * deltaTime;
            ball.X += ball.VX * deltaTime;
            ball.Y += ball.VY * deltaTime;

            // Bounce off walls
            if (ball.X <= ball.Radius || ball.X >= settings.Width - ball.Radius)
            {
                ball.VX *= -bounce;
                ball.X = Math.Max(ball.Radius, Math.Min(settings.Width - ball.Radius, ball.X));
            }

            // Bounce off floor
            if (ball.Y >= settings.Height - ball.Radius - 50)
            {
                ball.VY *= -bounce;
                ball.Y = settings.Height - ball.Radius - 50;

                // Remove if too slow
                if (Math.Abs(ball.VY) < 50)
                {
                    state.BouncingBalls.RemoveAt(i);
                    continue;
                }
            }

            // Add to trail
            ball.Trail.Add(new SKPoint(ball.X, ball.Y));
            if (ball.Trail.Count > 10)
                ball.Trail.RemoveAt(0);

            // Draw trail
            for (int t = 0; t < ball.Trail.Count - 1; t++)
            {
                using var trailPaint = new SKPaint
                {
                    Color = ball.Color.WithAlpha((byte)(t * 255 / ball.Trail.Count / 2)),
                    StrokeWidth = ball.Radius * 2 * t / ball.Trail.Count,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };
                canvas.DrawLine(ball.Trail[t], ball.Trail[t + 1], trailPaint);
            }

            // Draw ball with gradient
            using var ballGradient = SKShader.CreateRadialGradient(
                new SKPoint(ball.X - ball.Radius / 3, ball.Y - ball.Radius / 3),
                ball.Radius,
                new[] { SKColors.White, ball.Color },
                SKShaderTileMode.Clamp);

            using var ballPaint = new SKPaint
            {
                Shader = ballGradient,
                IsAntialias = true
            };
            canvas.DrawCircle(ball.X, ball.Y, ball.Radius, ballPaint);
        }
    }

    private void DrawSatisfyingWave(SKCanvas canvas, SatisfyingState state, VideoSettings settings, double frameTime)
    {
        using var wavePath = new SKPath();
        var points = 100;
        var amplitude = 30;
        var frequency = 0.02;
        var y = settings.Height - 60;

        for (int i = 0; i <= points; i++)
        {
            var x = (i / (float)points) * settings.Width;
            var waveY = y + (float)(Math.Sin(i * frequency + frameTime * 3) * amplitude);

            if (i == 0)
                wavePath.MoveTo(x, waveY);
            else
                wavePath.LineTo(x, waveY);
        }

        // Close path for fill
        wavePath.LineTo(settings.Width, settings.Height);
        wavePath.LineTo(0, settings.Height);
        wavePath.Close();

        // Draw with gradient
        var colorIndex = ((int)(frameTime * 0.5)) % state.Palette.Colors.Length;
        var waveColor = SKColor.Parse(state.Palette.Colors[colorIndex]);

        using var waveGradient = SKShader.CreateLinearGradient(
            new SKPoint(0, y - amplitude),
            new SKPoint(0, settings.Height),
            new[] { waveColor.WithAlpha(100), waveColor.WithAlpha(20) },
            SKShaderTileMode.Clamp);

        using var wavePaint = new SKPaint
        {
            Shader = waveGradient,
            IsAntialias = true
        };
        canvas.DrawPath(wavePath, wavePaint);
    }

    private void SpawnJumpingPixel(SatisfyingState state, VideoSettings settings, BeatInfo beatInfo)
    {
        var colorIndex = _random.Next(state.Palette.Colors.Length);
        state.JumpingPixels.Add(new JumpingPixel
        {
            X = _random.Next(100, settings.Width - 100),
            Y = settings.Height - 100,
            VY = -500f * beatInfo.Strength,
            Size = 30,
            Color = SKColor.Parse(state.Palette.Colors[colorIndex])
        });
    }

    private void DrawJumpingPixels(SKCanvas canvas, SatisfyingState state, VideoSettings settings, double frameTime)
    {
        var deltaTime = 1f / 30f;
        var gravity = 1000f;

        for (int i = state.JumpingPixels.Count - 1; i >= 0; i--)
        {
            var pixel = state.JumpingPixels[i];

            // Update physics
            pixel.VY += gravity * deltaTime;
            pixel.Y += pixel.VY * deltaTime;

            // Remove if below screen
            if (pixel.Y > settings.Height)
            {
                state.JumpingPixels.RemoveAt(i);
                continue;
            }

            // Draw pixel character (simple face)
            using var pixelPaint = new SKPaint
            {
                Color = pixel.Color,
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };

            // Body
            canvas.DrawRect(pixel.X - pixel.Size / 2, pixel.Y - pixel.Size, pixel.Size, pixel.Size, pixelPaint);

            // Eyes
            using var eyePaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(pixel.X - pixel.Size / 3, pixel.Y - pixel.Size * 0.7f, 5, 5, eyePaint);
            canvas.DrawRect(pixel.X + pixel.Size / 3 - 5, pixel.Y - pixel.Size * 0.7f, 5, 5, eyePaint);

            // Smile
            using var smilePaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true
            };
            var smilePath = new SKPath();
            smilePath.MoveTo(pixel.X - pixel.Size / 4, pixel.Y - pixel.Size * 0.3f);
            smilePath.QuadTo(pixel.X, pixel.Y - pixel.Size * 0.2f, pixel.X + pixel.Size / 4, pixel.Y - pixel.Size * 0.3f);
            canvas.DrawPath(smilePath, smilePaint);
        }
    }

    private void DrawGradientMesh(SKCanvas canvas, VideoSettings settings, SatisfyingState state, double frameTime)
    {
        var meshSize = 100;
        var time = (float)frameTime;

        for (int x = 0; x < settings.Width; x += meshSize)
        {
            for (int y = 0; y < settings.Height; y += meshSize)
            {
                var wave = (float)(Math.Sin(x * 0.01 + time) * Math.Cos(y * 0.01 + time) * 0.5 + 0.5);
                var colorIndex = ((x / meshSize + y / meshSize + (int)time) % state.Palette.Colors.Length);
                var color = SKColor.Parse(state.Palette.Colors[colorIndex]);

                using var meshGradient = SKShader.CreateRadialGradient(
                    new SKPoint(x + meshSize / 2, y + meshSize / 2),
                    meshSize * wave,
                    new[] { color.WithAlpha(50), SKColors.Transparent },
                    SKShaderTileMode.Clamp);

                using var meshPaint = new SKPaint
                {
                    Shader = meshGradient
                };
                canvas.DrawRect(x, y, meshSize, meshSize, meshPaint);
            }
        }
    }

    private void SpawnBarParticles(SatisfyingState state, float x, float y, SKColor color)
    {
        for (int i = 0; i < 3; i++)
        {
            state.Particles.Add(new SatisfyingParticle
            {
                X = x + (float)(_random.NextDouble() * 20 - 10),
                Y = y,
                VX = (float)(_random.NextDouble() * 100 - 50),
                VY = (float)(_random.NextDouble() * -200 - 100),
                Life = 1.0f,
                Color = color,
                Size = _random.Next(3, 8)
            });
        }
    }

    private void DrawFallingParticles(SKCanvas canvas, SatisfyingState state, VideoSettings settings)
    {
        foreach (var particle in state.Particles)
        {
            using var particlePaint = new SKPaint
            {
                Color = particle.Color.WithAlpha((byte)(particle.Life * 255)),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            canvas.DrawCircle(particle.X, particle.Y, particle.Size * particle.Life, particlePaint);
        }
    }

    private void DrawCircularGradient(SKCanvas canvas, VideoSettings settings, SatisfyingState state, double frameTime)
    {
        var centerX = settings.Width / 2f;
        var centerY = settings.Height / 2f;
        var time = (float)frameTime;

        for (int i = 0; i < 5; i++)
        {
            var radius = 100 + i * 100 + (float)(Math.Sin(time + i) * 20);
            var colorIndex = (i + (int)time) % state.Palette.Colors.Length;
            var color = SKColor.Parse(state.Palette.Colors[colorIndex]);

            using var ringGradient = SKShader.CreateRadialGradient(
                new SKPoint(centerX, centerY),
                radius,
                new[] { color.WithAlpha(30), SKColors.Transparent },
                SKShaderTileMode.Clamp);

            using var ringPaint = new SKPaint
            {
                Shader = ringGradient
            };
            canvas.DrawCircle(centerX, centerY, radius, ringPaint);
        }
    }

    private void DrawOrbitingParticles(SKCanvas canvas, SatisfyingState state, VideoSettings settings, double frameTime, BeatInfo beatInfo)
    {
        var centerX = settings.Width / 2f;
        var centerY = settings.Height / 2f;
        var particleCount = 20;

        for (int i = 0; i < particleCount; i++)
        {
            var angle = i * Math.PI * 2 / particleCount + frameTime;
            var radius = 150 + Math.Sin(frameTime * 2 + i * 0.5) * 50;

            if (beatInfo.OnBeat)
                radius += beatInfo.Strength * 30;

            var x = centerX + (float)(Math.Cos(angle) * radius);
            var y = centerY + (float)(Math.Sin(angle) * radius);

            var colorIndex = i % state.Palette.Colors.Length;
            var color = SKColor.Parse(state.Palette.Colors[colorIndex]);

            using var particlePaint = new SKPaint
            {
                Color = color,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            var size = 5 + (float)(Math.Sin(frameTime * 3 + i) * 3);
            canvas.DrawCircle(x, y, size, particlePaint);

            // Trail
            for (int t = 1; t < 5; t++)
            {
                var trailAngle = angle - t * 0.1;
                var trailX = centerX + (float)(Math.Cos(trailAngle) * radius);
                var trailY = centerY + (float)(Math.Sin(trailAngle) * radius);

                using var trailPaint = new SKPaint
                {
                    Color = color.WithAlpha((byte)(100 - t * 20)),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawCircle(trailX, trailY, size - t, trailPaint);
            }
        }
    }

    private void DrawCenterPulse(SKCanvas canvas, VideoSettings settings, BeatInfo beatInfo, SatisfyingState state)
    {
        var centerX = settings.Width / 2f;
        var centerY = settings.Height / 2f;
        var radius = 50 * beatInfo.Strength;

        var colorIndex = _random.Next(state.Palette.Colors.Length);
        var color = SKColor.Parse(state.Palette.Colors[colorIndex]);

        using var pulseGradient = SKShader.CreateRadialGradient(
            new SKPoint(centerX, centerY),
            radius,
            new[] { color.WithAlpha((byte)(beatInfo.Strength * 150)), SKColors.Transparent },
            SKShaderTileMode.Clamp);

        using var pulsePaint = new SKPaint
        {
            Shader = pulseGradient
        };
        canvas.DrawCircle(centerX, centerY, radius, pulsePaint);
    }

    private void DrawSatisfyingWatermark(SKCanvas canvas, string watermark, VideoSettings settings)
    {
        using var textPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(120),
            TextSize = 20,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        var textBounds = new SKRect();
        textPaint.MeasureText(watermark, ref textBounds);

        var x = settings.Width - textBounds.Width - 20;
        var y = settings.Height - 20;

        canvas.DrawText(watermark, x, y, textPaint);
    }

    // Helper methods

    private SatisfyingPalette SelectPalette(string mood)
    {
        return mood.ToLower() switch
        {
            "energetic" => _palettes["Neon"],
            "happy" => _palettes["Rainbow"],
            "calm" => _palettes["Pastel"],
            _ => _palettes["Candy"]
        };
    }

    private GridCell[,] InitializeGrid(int cols, int rows)
    {
        var grid = new GridCell[cols, rows];
        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                grid[x, y] = new GridCell { IsOccupied = false };
            }
        }
        return grid;
    }

    private List<double> GenerateBeatGrid(AudioData audio)
    {
        var grid = new List<double>();
        if (audio.BeatTimes != null && audio.BeatTimes.Length > 0)
        {
            grid.AddRange(audio.BeatTimes);
        }
        else
        {
            var beatInterval = 60.0 / audio.Tempo;
            var time = 0.0;
            while (time < audio.Duration.TotalSeconds)
            {
                grid.Add(time);
                time += beatInterval;
            }
        }
        return grid;
    }

    private BeatInfo GetBeatInfo(double frameTime, List<double> beatGrid, AudioData audio)
    {
        var beatInfo = new BeatInfo { OnBeat = false, Strength = 0 };

        if (beatGrid.Count == 0) return beatInfo;

        var nearestBeat = beatGrid.OrderBy(bt => Math.Abs(bt - frameTime)).FirstOrDefault();
        var beatDistance = Math.Abs(nearestBeat - frameTime);

        if (beatDistance < 0.1)
        {
            beatInfo.OnBeat = true;
            beatInfo.Strength = 1f - (float)(beatDistance / 0.1);
        }

        return beatInfo;
    }

    private double GetEnergyAtTime(AudioData audio, double time)
    {
        if (audio.EnergyLevels.Length == 0) return 0.5;

        var index = (int)(time * audio.EnergyLevels.Length / audio.Duration.TotalSeconds);
        index = Math.Max(0, Math.Min(audio.EnergyLevels.Length - 1, index));

        return audio.EnergyLevels[index];
    }

    private float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private async Task<string> EncodeVideoAsync(string[] frameFiles, string audioPath, VideoSettings settings, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            $"pixbeat_satisfying_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
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

    // Data structures

    private class SatisfyingState
    {
        public SatisfyingPalette Palette { get; set; } = new();
        public List<PixelBlock> PixelBlocks { get; set; } = new();
        public List<FallingBlock> FallingBlocks { get; set; } = new();
        public List<SatisfyingParticle> Particles { get; set; } = new();
        public List<WavePoint> WavePoints { get; set; } = new();
        public List<BouncingBall> BouncingBalls { get; set; } = new();
        public List<JumpingPixel> JumpingPixels { get; set; } = new();
        public GridCell[,] GridCells { get; set; } = new GridCell[0, 0];
        public List<double> BeatGrid { get; set; } = new();
        public Dictionary<int, float> BarHeights { get; set; } = new();
    }

    private class SatisfyingPalette
    {
        public string[] Colors { get; set; } = Array.Empty<string>();
        public string Background { get; set; } = "#1A1A1A";
    }

    private class PixelBlock
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public SKColor Color { get; set; }
    }

    private class FallingBlock
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VY { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Rotation { get; set; }
        public SKColor Color { get; set; }
    }

    private class SatisfyingParticle
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public float Life { get; set; }
        public float Size { get; set; }
        public SKColor Color { get; set; }
    }

    private class BouncingBall
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VX { get; set; }
        public float VY { get; set; }
        public float Radius { get; set; }
        public SKColor Color { get; set; }
        public List<SKPoint> Trail { get; set; } = new();
    }

    private class JumpingPixel
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VY { get; set; }
        public float Size { get; set; }
        public SKColor Color { get; set; }
    }

    private class WavePoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Phase { get; set; }
    }

    private class GridCell
    {
        public bool IsOccupied { get; set; }
        public SKColor Color { get; set; }
    }

    private class BeatInfo
    {
        public bool OnBeat { get; set; }
        public float Strength { get; set; }
    }
}