namespace Unosquare.FFME.Avalonia.Sample;

using global::Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithDeveloperTools()
            .LogToTrace();
}