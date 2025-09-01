using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PixBeat5.Models;
using PixBeat5.Services;
using System.Collections.ObjectModel;

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

                CurrentProject.Audio = await _audioService.AnalyzeAsync(dialog.FileName);
                HasAudioFile = true;
                HasAnalysis = true;
                UpdateCanRender();

                StatusMessage = $"Audio analysis complete - BPM: {CurrentProject.Audio.Tempo:F1}, Genre: {CurrentProject.Audio.Genre}";
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
    private async Task GenerateVideoAsync()
    {
        if (!CanRender) return;

        try
        {
            IsProcessing = true;
            StatusMessage = "Generating video...";

            var progress = new Progress<RenderProgress>(p =>
            {
                ProgressValue = p.Percentage;
                ProgressText = $"{p.Stage}: {p.CurrentFrame}/{p.TotalFrames}";
                StatusMessage = $"Rendering - {p.Stage} ({p.Percentage:F1}%)";
            });

            CurrentProject.Template = SelectedTemplate;
            var outputPath = await _renderService.RenderVideoAsync(CurrentProject, progress);

            StatusMessage = $"Video generated successfully: {Path.GetFileName(outputPath)}";
            CurrentProject.OutputPath = outputPath;

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
        if (!string.IsNullOrEmpty(CurrentProject.OutputPath) && File.Exists(CurrentProject.OutputPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{CurrentProject.OutputPath}\"");
        }
    }

    [RelayCommand]
    private void ResetProject()
    {
        CurrentProject = new ProjectData();
        HasAudioFile = false;
        HasAnalysis = false;
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