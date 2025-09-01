using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixBeat5.Models;
using PixBeat5.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace PixBeat5.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
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

    public ObservableCollection<TemplateInfo> AvailableTemplates { get; } = new();

    public MainViewModel(
        ILogger<MainViewModel> logger,
        IAudioAnalysisService audioService,
        IRenderService renderService)
    {
        _logger = logger;
        _audioService = audioService;
        _renderService = renderService;

        LoadTemplates();
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
                StatusMessage = "Analyzing audio...";
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

                StatusMessage = $"Audio analysis complete - BPM: {Tempo:F1}, Genre: {Genre}";
                _logger.LogInformation("Audio file loaded and analyzed: {FileName}", dialog.FileName);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error analyzing audio: {ex.Message}";
                _logger.LogError(ex, "Failed to analyze audio file");
            }
            finally
            {
                IsProcessing = false;
                ProgressValue = 0;
            }
        }
    }

    [RelayCommand]
    private void SelectTemplate(string templateId)
    {
        SelectedTemplate = templateId;
        CurrentProject.Template = templateId;
        StatusMessage = $"Selected template: {templateId}";
        UpdateCanRender();
    }

    [RelayCommand]
    private async Task GenerateVideoAsync()
    {
        if (!CanRender) return;

        try
        {
            IsProcessing = true;
            StatusMessage = "Generating video...";

            // Update project settings
            CurrentProject.Settings.Watermark = WatermarkText;

            var progress = new Progress<RenderProgress>(p =>
            {
                ProgressValue = p.Percentage;
                ProgressText = $"{p.Stage}: {p.CurrentFrame}/{p.TotalFrames}";
                StatusMessage = $"Rendering - {p.Stage} ({p.Percentage:F1}%)";
            });

            CurrentProject.Template = SelectedTemplate;
            var outputPath = await _renderService.RenderVideoAsync(CurrentProject, progress);

            StatusMessage = $"Video generated successfully: {Path.GetFileName(outputPath)}";
            OutputPath = outputPath;
            CurrentProject.OutputPath = outputPath;
            HasOutputFile = true;

            _logger.LogInformation("Video generation completed: {OutputPath}", outputPath);

            // Ask if user wants to open the output folder
            if (System.Windows.MessageBox.Show(
                "Video generated successfully! Do you want to open the output folder?",
                "Success",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputPath}\"");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error generating video: {ex.Message}";
            _logger.LogError(ex, "Video generation failed");
            System.Windows.MessageBox.Show(
                $"Error generating video:\n{ex.Message}",
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

        HasAudioFile = false;
        HasAnalysis = false;
        HasOutputFile = false;
        UpdateCanRender();
        StatusMessage = "Project reset";
        ProgressValue = 0;
        ProgressText = "";
    }

    partial void OnSelectedTemplateChanged(string value)
    {
        CurrentProject.Template = value;
        StatusMessage = $"Selected template: {value}";
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
        // Load available templates
        AvailableTemplates.Clear();

        var templates = new[]
        {
            new TemplateInfo
            {
                Id = "pixel_runner",
                Name = "Pixel Runner",
                Description = "Retro pixel art runner with beat-synced jumping",
                Tags = new[] { "pixel", "game", "retro" }
            },
            new TemplateInfo
            {
                Id = "equalizer",
                Name = "Audio Equalizer",
                Description = "Classic audio equalizer visualization",
                Tags = new[] { "equalizer", "bars", "music" }
            },
            new TemplateInfo
            {
                Id = "waveform",
                Name = "Waveform Visualizer",
                Description = "Smooth waveform with particle effects",
                Tags = new[] { "waveform", "smooth", "particles" }
            }
        };

        foreach (var template in templates)
        {
            AvailableTemplates.Add(template);
        }

        SelectedTemplate = "pixel_runner";
    }
}