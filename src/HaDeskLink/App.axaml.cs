using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HaDeskLink.Views;

namespace HaDeskLink;

public class App : Application
{
    public static Config? CurrentConfig { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CurrentConfig = Config.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.Title = $"HA DeskLink Linux v{HaApiClient.GetVersion()}";
            if (CurrentConfig != null)
            {
                ((MainWindow)desktop.MainWindow).HaUrl = CurrentConfig.HaUrl;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}