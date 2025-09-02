using Microsoft.Extensions.DependencyInjection;
using PixBeat5.ViewModels;

namespace PixBeat5;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider.GetRequiredService<EnhancedMainViewModel>();
    }
}