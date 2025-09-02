using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixBeat5.Models;
using PixBeat5.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace PixBeat5.ViewModels;

public partial class EnhancedMainViewModel : ObservableObject
{
    private readonly ILogger<EnhancedMainViewModel> _logger;
    private readonly IAudioAnalysisService _audioService;
    private readonly IRenderService _renderService;

    [ObservableProperty]
    private ProjectData _currentProject = new();

    [ObservableProperty]
    private bool _isProcessing = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _selectedTemplate = "pixel_runner";

    [ObservableProperty]
    private bool _hasAudioFile = false;

    [ObservableProperty]
    private bool _hasAnalysis = false;

    [ObservableProperty]
    private bool _canRender = false;

    [ObservableProperty]
    private bool _hasOutputFile = false;

    // UI binding properties
    [ObservableProperty]
    private string _audioFilePath = "No audio file selected";

    [ObservableProperty]
    private double _tempo = 0;

    [ObservableProperty]
    private string _genre = "Unknown";

    [ObservableProperty]
    private string _key = "C";

    [ObservableProperty]
    private string _mode = "major";

    [ObservableProperty]
    private TimeSpan _duration = TimeSpan.Zero;

    [ObservableProperty]
    private string _watermarkText = "LuyenAI.vn";

    [ObservableProperty]
    private string _outputPath = "";

    // New properties for enhanced settings
    [ObservableProperty]
    private string _videoQuality = "Standard";

    [ObservableProperty]
    private int _videoDuration = 15; // seconds (0 = full song)

    [ObservableProperty]
    private string _aspectRatio = "9:16";

    public ObservableCollection<TemplateInfo> AvailableTemplates { get; } = new();
    public ObservableCollection<string> QualityOptions { get; } = new() { "Draft", "Standard", "High" };
    public ObservableCollection<DurationOption> DurationOptions { get; } = new();
    public ObservableCollection<AspectRatioOption> AspectRatioOptions { get; } = new();

    public EnhancedMainViewModel(
        ILogger<EnhancedMainViewModel> logger,
        IAudioAnalysisService audioService,
        IRenderService renderService)
    {
        _logger = logger;
        _audioService = audioService;
        _renderService = renderService;

        InitializeOptions();
        LoadTemplates();
    }

    private void InitializeOptions()
    {
        // Duration options
        DurationOptions.Add(new DurationOption { Display = "5 seconds", Value = 5 });
        DurationOptions.Add(new DurationOption { Display = "15 seconds", Value = 15 });
        DurationOptions.Add(new DurationOption { Display = "30 seconds", Value = 30 });
        DurationOptions.Add(new DurationOption { Display = "60 seconds", Value = 60 });
        DurationOptions.Add(new DurationOption { Display = "Full song", Value = 0 });

        // Aspect ratio options
        AspectRatioOptions.Add(new AspectRatioOption { Display = "9:16 (TikTok/Shorts)", Value = "9:16", Width = 1080, Height = 1920 });
        AspectRatioOptions.Add(new AspectRatioOption { Display = "1:1 (Instagram)", Value = "1:1", Width = 1080, Height = 1080 });
        AspectRatioOptions.Add(new AspectRatioOption { Display = "16:9 (YouTube)", Value = "16:9", Width = 1920, Height = 1080 });
    }

    [RelayCommand]
    private async Task SelectAudioFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Audio File",
            Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.flac|All Files|*.*",
            FilterIndex = 1
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                IsProcessing = true;
                StatusMessage = "🎵 Analyzing audio with AI...";
                ProgressValue = 0;

                var audioData = await _audioService.AnalyzeAsync(dialog.FileName);

                // Update project
                CurrentProject.Audio = audioData;

                // Update UI properties
                AudioFilePath = Path.GetFileName(audioData.FilePath);
                Tempo = audioData.Tempo;
                Genre = audioData.Genre;
                Key = audioData.Key;
                Mode = audioData.Mode;
                Duration = audioData.Duration;

                HasAudioFile = true;
                HasAnalysis = true;
                UpdateCanRender();

                StatusMessage = $"✨ Analysis complete - {Genre} • {Tempo:F0} BPM • {Key} {Mode} • Mood: {audioData.Mood}";
                _logger.LogInformation("Audio file loaded and analyzed: {FileName}", dialog.FileName);

                // Auto-select best template based on genre
                AutoSelectTemplate(audioData);
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error analyzing audio: {ex.Message}";
                _logger.LogError(ex, "Failed to analyze audio file");
            }
            finally
            {
                IsProcessing = false;
                ProgressValue = 0;
            }
        }
    }

    private void AutoSelectTemplate(AudioData audioData)
    {
        // Smart template selection based on genre and mood
        var recommendedTemplate = audioData.Genre.ToLower() switch
        {
            "electronic" => "equalizer",
            "pop" => "pixel_runner",
            "rock" => "pixel_runner",
            "classical" => "waveform",
            _ => audioData.Mood.ToLower() switch
            {
                "energetic" => "pixel_runner",
                "calm" => "waveform",
                "happy" => "equalizer",
                _ => "pixel_runner"
            }
        };

        SelectedTemplate = recommendedTemplate;
        CurrentProject.Template = recommendedTemplate;
        StatusMessage += $" | 🎨 Recommended template: {GetTemplateName(recommendedTemplate)}";
    }

    private string GetTemplateName(string templateId)
    {
        return templateId switch
        {
            "pixel_runner" => "Pixel Runner",
            "equalizer" => "Audio Equalizer",
            "waveform" => "Waveform Visualizer",
            _ => templateId
        };
    }

    [RelayCommand]
    private void SelectTemplate(string templateId)
    {
        SelectedTemplate = templateId;
        CurrentProject.Template = templateId;
        StatusMessage = $"🎨 Selected template: {GetTemplateName(templateId)}";
        UpdateCanRender();
    }

    [RelayCommand]
    private async Task GenerateVideoAsync()
    {
        if (!CanRender) return;

        try
        {
            IsProcessing = true;
            StatusMessage = "🎬 Generating pixel art video...";

            // Update project settings based on UI selections
            UpdateProjectSettings();

            var progress = new Progress<RenderProgress>(p =>
            {
                ProgressValue = p.Percentage;

                // Enhanced progress messages
                var emoji = p.Stage switch
                {
                    var s when s.Contains("Frame") => "🎨",
                    var s when s.Contains("Encoding") => "🎥",
                    var s when s.Contains("Audio") => "🎵",
                    _ => "⚡"
                };

                ProgressText = $"{emoji} {p.Stage}: {p.CurrentFrame}/{p.TotalFrames}";

                if (p.Estimated.TotalSeconds > 0)
                {
                    var eta = p.Estimated.TotalSeconds < 60 ?
                        $"{p.Estimated.TotalSeconds:F0}s" :
                        $"{p.Estimated.TotalMinutes:F1}m";
                    ProgressText += $" | ETA: {eta}";
                }

                StatusMessage = $"Rendering {p.Percentage:F0}% - {p.Stage}";
            });

            CurrentProject.Template = SelectedTemplate;
            var outputPath = await _renderService.RenderVideoAsync(CurrentProject, progress);

            StatusMessage = $"✅ Video generated successfully!";
            OutputPath = outputPath;
            CurrentProject.OutputPath = outputPath;
            HasOutputFile = true;

            _logger.LogInformation("Video generation completed: {OutputPath}", outputPath);

            // Show success notification with options
            ShowSuccessDialog(outputPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error generating video: {ex.Message}";
            _logger.LogError(ex, "Video generation failed");

            System.Windows.MessageBox.Show(
                $"Error generating video:\n{ex.Message}\n\nPlease check:\n• FFmpeg is installed\n• Audio file is valid\n• Sufficient disk space",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }

    private void UpdateProjectSettings()
    {
        // Update watermark
        CurrentProject.Settings.Watermark = WatermarkText;

        // Update quality
        CurrentProject.Settings.Quality = VideoQuality;

        // Update duration
        if (VideoDuration > 0)
        {
            CurrentProject.Settings.Duration = TimeSpan.FromSeconds(VideoDuration);
        }
        else
        {
            // Full song
            CurrentProject.Settings.Duration = CurrentProject.Audio?.Duration ?? TimeSpan.FromSeconds(30);
        }

        // Update aspect ratio
        var aspectOption = AspectRatioOptions.FirstOrDefault(a => a.Value == AspectRatio);
        if (aspectOption != null)
        {
            CurrentProject.Settings.Width = aspectOption.Width;
            CurrentProject.Settings.Height = aspectOption.Height;
        }

        // FPS based on quality
        CurrentProject.Settings.Fps = VideoQuality switch
        {
            "Draft" => 24,
            "High" => 60,
            _ => 30
        };
    }

    private void ShowSuccessDialog(string outputPath)
    {
        var fileName = Path.GetFileName(outputPath);
        var fileSize = new FileInfo(outputPath).Length / (1024.0 * 1024.0); // MB

        var message = $"🎉 Video generated successfully!\n\n" +
                     $"📁 File: {fileName}\n" +
                     $"💾 Size: {fileSize:F1} MB\n" +
                     $"⏱️ Duration: {CurrentProject.Settings.Duration.TotalSeconds}s\n" +
                     $"📐 Resolution: {CurrentProject.Settings.Width}x{CurrentProject.Settings.Height}\n\n" +
                     $"What would you like to do?";

        var result = System.Windows.MessageBox.Show(
            message,
            "Success! 🎊",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Information);

        switch (result)
        {
            case System.Windows.MessageBoxResult.Yes:
                // Open folder and select file
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
                break;
            case System.Windows.MessageBoxResult.No:
                // Play video
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outputPath,
                    UseShellExecute = true
                });
                break;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(OutputPath) && File.Exists(OutputPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{OutputPath}\"");
        }
    }

    [RelayCommand]
    private void ResetProject()
    {
        CurrentProject = new ProjectData();

        // Reset UI properties
        AudioFilePath = "No audio file selected";
        Tempo = 0;
        Genre = "Unknown";
        Key = "C";
        Mode = "major";
        Duration = TimeSpan.Zero;
        OutputPath = "";

        // Reset settings to defaults
        VideoQuality = "Standard";
        VideoDuration = 15;
        AspectRatio = "9:16";
        WatermarkText = "LuyenAI.vn";

        HasAudioFile = false;
        HasAnalysis = false;
        HasOutputFile = false;
        UpdateCanRender();

        StatusMessage = "🔄 Project reset - Ready for new video";
        ProgressValue = 0;
        ProgressText = "";
    }

    partial void OnSelectedTemplateChanged(string value)
    {
        CurrentProject.Template = value;
        StatusMessage = $"🎨 Selected template: {GetTemplateName(value)}";
        UpdateCanRender();
    }

    private void UpdateCanRender()
    {
        CanRender = HasAudioFile && HasAnalysis && !IsProcessing && !string.IsNullOrEmpty(SelectedTemplate);
    }

    partial void OnIsProcessingChanged(bool value)
    {
        UpdateCanRender();
    }

    private void LoadTemplates()
    {
        // Load available templates with enhanced descriptions
        AvailableTemplates.Clear();

        var templates = new[]
        {
            new TemplateInfo
            {
                Id = "pixel_runner",
                Name = "Pixel Runner",
                Description = "🏃 Retro pixel art characters jumping to the beat! Perfect for gaming content and upbeat music.",
                Tags = new[] { "pixel", "game", "retro", "energetic", "fun" }
            },
            new TemplateInfo
            {
                Id = "equalizer",
                Name = "Audio Equalizer",
                Description = "🎚️ Classic equalizer bars dancing with your music. Great for electronic and dance tracks.",
                Tags = new[] { "equalizer", "bars", "music", "electronic", "professional" }
            },
            new TemplateInfo
            {
                Id = "waveform",
                Name = "Waveform Visualizer",
                Description = "〰️ Smooth waveforms with mesmerizing particle effects. Ideal for chill and ambient music.",
                Tags = new[] { "waveform", "smooth", "particles", "ambient", "elegant" }
            }
        };

        foreach (var template in templates)
        {
            AvailableTemplates.Add(template);
        }

        SelectedTemplate = "pixel_runner";
    }

    // Helper classes for options
    public class DurationOption
    {
        public string Display { get; set; } = "";
        public int Value { get; set; } // seconds, 0 = full song
    }

    public class AspectRatioOption
    {
        public string Display { get; set; } = "";
        public string Value { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
    }
}