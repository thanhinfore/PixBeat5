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

                // Services
                services.AddSingleton<IAudioAnalysisService, AudioAnalysisService>();
                services.AddSingleton<IRenderService, RenderService>();

                // ViewModels
                services.AddTransient<MainViewModel>();
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