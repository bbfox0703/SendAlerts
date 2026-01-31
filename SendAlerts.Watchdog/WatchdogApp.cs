using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace SendAlerts.Watchdog;

public class WatchdogApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Program.InitializeTrayIcon();
    }
}
