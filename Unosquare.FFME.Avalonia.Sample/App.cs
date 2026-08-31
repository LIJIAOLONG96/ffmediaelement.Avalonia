namespace Unosquare.FFME.Avalonia.Sample;

using global::Avalonia;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Themes.Fluent;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args?.FirstOrDefault());

        base.OnFrameworkInitializationCompleted();
    }
}