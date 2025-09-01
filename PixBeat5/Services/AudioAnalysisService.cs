using Microsoft.Extensions.Logging;
using NAudio.Wave;
using PixBeat5.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PixBeat5.Services;

public interface IAudioAnalysisService
{
    Task<AudioData> AnalyzeAsync(string audioPath, CancellationToken cancellationToken = default);
}

public class AudioAnalysisService : IAudioAnalysisService
{
    private readonly ILogger<AudioAnalysisService> _logger;

    public AudioAnalysisService(ILogger<AudioAnalysisService> logger)
    {
        _logger = logger;
    }

    public async Task<AudioData> AnalyzeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting audio analysis for: {AudioPath}", audioPath);

        try
        {
            // Basic audio info with NAudio
            var basicInfo = GetBasicAudioInfo(audioPath);

            // AI analysis with Python
            var aiAnalysis = await RunPythonAnalysisAsync(audioPath, cancellationToken);

            var result = new AudioData
            {
                FilePath = audioPath,
                Duration = basicInfo.Duration,
                Tempo = aiAnalysis.Tempo,
                BeatTimes = aiAnalysis.BeatTimes,
                EnergyLevels = aiAnalysis.EnergyLevels,
                Genre = aiAnalysis.Genre,
                Key = aiAnalysis.Key,
                Mode = aiAnalysis.Mode,
                Mood = aiAnalysis.Mood,
                Confidence = aiAnalysis.Confidence
            };

            _logger.LogInformation("Audio analysis complete. BPM: {Tempo:F1}, Genre: {Genre}",
                result.Tempo, result.Genre);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio analysis failed");
            throw;
        }
    }

    private (TimeSpan Duration, int SampleRate) GetBasicAudioInfo(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        return (reader.TotalTime, reader.WaveFormat.SampleRate);
    }

    private async Task<PythonAnalysisResult> RunPythonAnalysisAsync(string audioPath, CancellationToken cancellationToken)
    {
        var pythonScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Python", "analyze_audio.py");
        var outputFile = Path.GetTempFileName() + ".json";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{pythonScript}\" \"{audioPath}\" \"{outputFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
            var processTask = process.WaitForExitAsync(cancellationToken);

            var completedTask = await Task.WhenAny(processTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                process.Kill();
                throw new TimeoutException("Python analysis timed out");
            }

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"Python analysis failed: {error}");
            }

            if (!File.Exists(outputFile))
            {
                throw new FileNotFoundException("Python analysis output not found");
            }

            var json = await File.ReadAllTextAsync(outputFile, cancellationToken);
            var result = JsonSerializer.Deserialize<PythonAnalysisResult>(json);

            return result ?? throw new InvalidOperationException("Failed to deserialize analysis result");
        }
        finally
        {
            if (File.Exists(outputFile))
            {
                try { File.Delete(outputFile); } catch { }
            }
        }
    }

    private class PythonAnalysisResult
    {
        public double Tempo { get; set; }
        public double[] BeatTimes { get; set; } = Array.Empty<double>();
        public double[] EnergyLevels { get; set; } = Array.Empty<double>();
        public string Genre { get; set; } = "Unknown";
        public string Key { get; set; } = "C";
        public string Mode { get; set; } = "major";
        public string Mood { get; set; } = "Neutral";
        public double Confidence { get; set; }
    }
}