using Microsoft.Extensions.Logging;
using NAudio.Dsp;
using NAudio.Wave;
using PixBeat5.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixBeat5.Services;

public interface IAudioAnalysisService
{
    Task<AudioData> AnalyzeAsync(string audioPath, CancellationToken cancellationToken = default);
}

public class AudioAnalysisService : IAudioAnalysisService
{
    private readonly ILogger<AudioAnalysisService> _logger;
    private bool? _pythonAvailable = null;

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

            AudioData result;

            // Try Python analysis first
            if (await IsPythonAvailable())
            {
                try
                {
                    var aiAnalysis = await RunPythonAnalysisAsync(audioPath, cancellationToken);
                    result = new AudioData
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
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Python analysis failed, using fallback");
                    result = await FallbackAnalysisAsync(audioPath, basicInfo);
                }
            }
            else
            {
                _logger.LogWarning("Python not available, using fallback analysis");
                result = await FallbackAnalysisAsync(audioPath, basicInfo);
            }

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

    private async Task<bool> IsPythonAvailable()
    {
        if (_pythonAvailable.HasValue)
            return _pythonAvailable.Value;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                _pythonAvailable = process.ExitCode == 0;

                if (_pythonAvailable.Value)
                {
                    // Check if librosa is installed
                    startInfo.Arguments = "-c \"import librosa; print('OK')\"";
                    using var libProcess = Process.Start(startInfo);
                    if (libProcess != null)
                    {
                        await libProcess.WaitForExitAsync();
                        _pythonAvailable = libProcess.ExitCode == 0;
                    }
                }
            }
        }
        catch
        {
            _pythonAvailable = false;
        }

        return _pythonAvailable.Value;
    }

    private async Task<AudioData> FallbackAnalysisAsync(string audioPath, (TimeSpan Duration, int SampleRate) basicInfo)
    {
        return await Task.Run(() =>
        {
            _logger.LogInformation("Running fallback analysis with NAudio");

            using var reader = new AudioFileReader(audioPath);

            // Simple BPM detection
            var samples = new float[reader.Length / sizeof(float)];
            reader.Read(samples, 0, samples.Length);

            // Basic beat detection using energy peaks
            var tempo = DetectTempo(samples, reader.WaveFormat.SampleRate);
            var beatTimes = GenerateBeatTimes(tempo, basicInfo.Duration);
            var energyLevels = CalculateEnergyLevels(samples, reader.WaveFormat.SampleRate);

            // Simple genre detection based on spectral characteristics
            var genre = DetectGenreFromSpectrum(samples, reader.WaveFormat.SampleRate);

            return new AudioData
            {
                FilePath = audioPath,
                Duration = basicInfo.Duration,
                Tempo = tempo,
                BeatTimes = beatTimes,
                EnergyLevels = energyLevels,
                Genre = genre,
                Key = "C", // Default
                Mode = "major", // Default
                Mood = tempo > 120 ? "Energetic" : "Calm",
                Confidence = 0.6, // Lower confidence for fallback
                AnalyzedAt = DateTime.Now
            };
        });
    }

    private double DetectTempo(float[] samples, int sampleRate)
    {
        // Simple tempo detection using autocorrelation
        // This is a simplified implementation
        var windowSize = sampleRate * 2; // 2 second window
        var minBpm = 60.0;
        var maxBpm = 180.0;

        var minLag = (int)(sampleRate * 60.0 / maxBpm);
        var maxLag = (int)(sampleRate * 60.0 / minBpm);

        double maxCorrelation = 0;
        int bestLag = minLag;

        // Simplified autocorrelation
        for (int lag = minLag; lag < maxLag && lag < samples.Length / 2; lag++)
        {
            double correlation = 0;
            int count = Math.Min(windowSize, samples.Length - lag);

            for (int i = 0; i < count; i++)
            {
                correlation += samples[i] * samples[i + lag];
            }

            if (correlation > maxCorrelation)
            {
                maxCorrelation = correlation;
                bestLag = lag;
            }
        }

        var tempo = 60.0 * sampleRate / bestLag;

        // Clamp to reasonable range
        return Math.Max(minBpm, Math.Min(maxBpm, tempo));
    }

    private double[] GenerateBeatTimes(double tempo, TimeSpan duration)
    {
        var beatInterval = 60.0 / tempo;
        var beatCount = (int)(duration.TotalSeconds / beatInterval);
        var beatTimes = new double[beatCount];

        for (int i = 0; i < beatCount; i++)
        {
            beatTimes[i] = i * beatInterval;
        }

        return beatTimes;
    }

    private double[] CalculateEnergyLevels(float[] samples, int sampleRate)
    {
        var windowSize = sampleRate / 10; // 100ms windows
        var windowCount = samples.Length / windowSize;
        var energyLevels = new double[Math.Min(windowCount, 1000)]; // Limit to 1000 windows

        for (int i = 0; i < energyLevels.Length; i++)
        {
            double energy = 0;
            int start = i * windowSize;
            int end = Math.Min(start + windowSize, samples.Length);

            for (int j = start; j < end; j++)
            {
                energy += samples[j] * samples[j];
            }

            energyLevels[i] = Math.Sqrt(energy / windowSize);
        }

        // Normalize
        var maxEnergy = energyLevels.Max();
        if (maxEnergy > 0)
        {
            for (int i = 0; i < energyLevels.Length; i++)
            {
                energyLevels[i] /= maxEnergy;
            }
        }

        return energyLevels;
    }

    private string DetectGenreFromSpectrum(float[] samples, int sampleRate)
    {
        // Very simplified genre detection based on frequency content
        var fftSize = 4096;
        var fftSamples = samples.Take(Math.Min(fftSize, samples.Length)).ToArray();

        // Simple FFT using NAudio
        var fft = new Complex[fftSize];
        for (int i = 0; i < fftSamples.Length; i++)
        {
            fft[i].X = fftSamples[i] * (float)FastFourierTransform.HammingWindow(i, fftSize);
            fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, (int)Math.Log(fftSize, 2), fft);

        // Calculate spectral centroid
        double centroid = 0;
        double magnitude = 0;

        for (int i = 0; i < fftSize / 2; i++)
        {
            var mag = Math.Sqrt(fft[i].X * fft[i].X + fft[i].Y * fft[i].Y);
            centroid += i * mag;
            magnitude += mag;
        }

        if (magnitude > 0)
        {
            centroid = centroid / magnitude * sampleRate / fftSize;
        }

        // Simple genre classification based on spectral centroid
        if (centroid > 3000)
            return "Electronic";
        else if (centroid > 2000)
            return "Pop";
        else if (centroid > 1500)
            return "Rock";
        else
            return "Classical";
    }

    private (TimeSpan Duration, int SampleRate) GetBasicAudioInfo(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        return (reader.TotalTime, reader.WaveFormat.SampleRate);
    }

    private async Task<PythonAnalysisResult> RunPythonAnalysisAsync(string audioPath, CancellationToken cancellationToken)
    {
        var pythonScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Python", "analyze_audio.py");

        // Check if Python script exists
        if (!File.Exists(pythonScript))
        {
            // Create the Python directory and script if it doesn't exist
            var pythonDir = Path.GetDirectoryName(pythonScript)!;
            Directory.CreateDirectory(pythonDir);
            await CreatePythonScriptAsync(pythonScript);
        }

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
                CreateNoWindow = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

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
                _logger.LogError("Python error output: {Error}", error);
                throw new InvalidOperationException($"Python analysis failed with exit code {process.ExitCode}: {error}");
            }

            if (!File.Exists(outputFile))
            {
                throw new FileNotFoundException("Python analysis output not found");
            }

            var json = await File.ReadAllTextAsync(outputFile, cancellationToken);
            var result = JsonSerializer.Deserialize<PythonAnalysisResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

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

    private async Task CreatePythonScriptAsync(string scriptPath)
    {
        var script = @"#!/usr/bin/env python3
import sys
import json
import numpy as np
from pathlib import Path

# Simplified fallback if librosa not available
try:
    import librosa
    LIBROSA_AVAILABLE = True
except ImportError:
    LIBROSA_AVAILABLE = False
    print('Warning: librosa not installed, using simplified analysis', file=sys.stderr)

def analyze_audio(audio_path: str, output_path: str):
    if LIBROSA_AVAILABLE:
        analyze_with_librosa(audio_path, output_path)
    else:
        analyze_simple(audio_path, output_path)

def analyze_with_librosa(audio_path: str, output_path: str):
    try:
        y, sr = librosa.load(audio_path, sr=44100, mono=True)
        
        tempo, beat_frames = librosa.beat.beat_track(y=y, sr=sr, units='time')
        beat_times = beat_frames.tolist()[:1000]
        
        rms = librosa.feature.rms(y=y)[0]
        energy_levels = rms[::10].tolist()[:1000]
        
        spectral_centroids = librosa.feature.spectral_centroid(y=y, sr=sr)[0]
        avg_centroid = np.mean(spectral_centroids)
        
        if avg_centroid > 3000:
            genre = 'Electronic'
        elif avg_centroid > 2000:
            genre = 'Pop'
        elif avg_centroid > 1500:
            genre = 'Rock'
        else:
            genre = 'Classical'
        
        chroma = librosa.feature.chroma_stft(y=y, sr=sr)
        key_profiles = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
        key_index = np.argmax(np.sum(chroma, axis=1))
        key = key_profiles[key_index]
        
        avg_energy = np.mean(rms)
        if tempo > 120 and avg_energy > 0.1:
            mood = 'Energetic'
        elif tempo < 80:
            mood = 'Calm'
        elif avg_energy > 0.15:
            mood = 'Happy'
        else:
            mood = 'Neutral'
        
        mode = 'major' if avg_energy > 0.1 else 'minor'
        
        onset_strength = librosa.onset.onset_strength(y=y, sr=sr)
        confidence = min(np.mean(onset_strength) / 0.5, 1.0)
        
        results = {
            'tempo': float(tempo),
            'beat_times': beat_times,
            'energy_levels': energy_levels,
            'genre': genre,
            'key': key,
            'mode': mode,
            'mood': mood,
            'confidence': confidence
        }
        
        with open(output_path, 'w') as f:
            json.dump(results, f, indent=2)
        
        print(f'Analysis complete: BPM={tempo:.1f}, Genre={genre}, Key={key}')
        
    except Exception as e:
        print(f'Error during analysis: {e}', file=sys.stderr)
        sys.exit(1)

def analyze_simple(audio_path: str, output_path: str):
    # Simplified analysis without librosa
    results = {
        'tempo': 120.0,
        'beat_times': [i * 0.5 for i in range(120)],
        'energy_levels': [0.5 + 0.3 * np.sin(i * 0.1) for i in range(100)],
        'genre': 'Unknown',
        'key': 'C',
        'mode': 'major',
        'mood': 'Neutral',
        'confidence': 0.5
    }
    
    with open(output_path, 'w') as f:
        json.dump(results, f, indent=2)
    
    print('Simple analysis complete (librosa not available)')

if __name__ == '__main__':
    if len(sys.argv) != 3:
        print('Usage: python analyze_audio.py <input_audio> <output_json>')
        sys.exit(1)
    
    audio_path = sys.argv[1]
    output_path = sys.argv[2]
    
    if not Path(audio_path).exists():
        print(f'Error: Audio file not found: {audio_path}', file=sys.stderr)
        sys.exit(1)
    
    analyze_audio(audio_path, output_path)";

        await File.WriteAllTextAsync(scriptPath, script);
    }

    private class PythonAnalysisResult
    {
        public double Tempo { get; set; }

        [JsonPropertyName("beat_times")]
        public double[] BeatTimes { get; set; } = Array.Empty<double>();

        [JsonPropertyName("energy_levels")]
        public double[] EnergyLevels { get; set; } = Array.Empty<double>();

        public string Genre { get; set; } = "Unknown";
        public string Key { get; set; } = "C";
        public string Mode { get; set; } = "major";
        public string Mood { get; set; } = "Neutral";
        public double Confidence { get; set; }
    }
}