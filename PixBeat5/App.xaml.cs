using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PixBeat5.Services;
using PixBeat5.ViewModels;
using System.Windows;

namespace PixBeat5;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // Services - Register multiple render services
                services.AddSingleton<IAudioAnalysisService, AudioAnalysisService>();

                // Register all render services
                services.AddSingleton<EnhancedRenderService>();
                services.AddSingleton<SatisfyingRenderService>();
                services.AddSingleton<SquareBoomRenderService>();
                services.AddSingleton<EnhancedSquareBoomRenderService>();

                // Factory to select render service based on style
                services.AddSingleton<IRenderService>(provider =>
                {
                    // Default to SquareBoom for now
                    // TODO: Make this configurable via settings
                    return provider.GetRequiredService<SquareBoomRenderService>();
                });

                // ViewModels - Use Enhanced version
                services.AddTransient<EnhancedMainViewModel>();
            })
            .Build();

        await _host.StartAsync();
        ServiceProvider = _host.Services;

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}