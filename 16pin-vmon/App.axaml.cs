using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using _16pin_vmon.ViewModels;
using _16pin_vmon.Views;
using _16pin_vmon.Implementations;
using _16pin_vmon.Core.Interfaces;

namespace _16pin_vmon;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            // T0-1: Show disclaimer window first
            var disclaimerVm = new DisclaimerViewModel();
            var disclaimerWindow = new DisclaimerWindow(disclaimerVm);

            disclaimerWindow.Closed += (_, _) =>
            {
                if (disclaimerWindow.IsAccepted)
                {
                    // User accepted - show main window
                    var gpuProvider = CreateGpuProvider();
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = new MainViewModel(gpuProvider)
                    };
                    desktop.MainWindow.Show();
                }
                else
                {
                    // User declined - exit application
                    desktop.Shutdown();
                }
            };

            // Show disclaimer as the initial window
            desktop.MainWindow = disclaimerWindow;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            // For mobile/browser - skip disclaimer for now, use demo provider
            var gpuProvider = new DemoGpuProvider();
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel(gpuProvider)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Creates the appropriate IGpuProvider based on platform.
    /// TODO (T1-1): Replace with proper DI container setup.
    /// </summary>
    private static IGpuProvider CreateGpuProvider()
    {
        // For now, use DemoGpuProvider
        // In the future, this will check RuntimeInformation and create NvmlWindowsProvider on Windows
        return new DemoGpuProvider();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
