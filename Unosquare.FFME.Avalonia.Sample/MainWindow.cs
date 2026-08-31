namespace Unosquare.FFME.Avalonia.Sample;

using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Threading;
using System.Globalization;
using System.Runtime.InteropServices;
using Unosquare.FFME.Common;
using MediaElement = Unosquare.FFME.MediaElement;

public sealed class MainWindow : Window
{
    private readonly MediaElement Media = new();
    private readonly TextBox FfmpegPath = new() { Watermark = "FFmpeg native library directory" };
    private readonly TextBox SourcePath = new() { Watermark = "Media URL or local file path" };
    private readonly TextBlock Status = new() { Text = "Ready (video, audio, and subtitles enabled)." };
    private readonly TextBlock Time = new() { Text = "00:00 / 00:00" };
    private readonly Slider PositionSlider = new() { Minimum = 0, Maximum = 1 };
    private readonly DispatcherTimer UiTimer;
    private bool IsDraggingPosition;

    public MainWindow(string? startupSource = null)
    {
        Title = "FFME Avalonia Player";
        Width = 1100;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#101417"));

        var bundledFfmpegDirectory = FindBundledFfmpegDirectory();
        if (bundledFfmpegDirectory is not null)
        {
            FfmpegPath.Text = bundledFfmpegDirectory;
            Library.FFmpegDirectory = bundledFfmpegDirectory;
        }

        Media.LoadedBehavior = MediaPlaybackState.Play;
        Media.MediaOpened += (_, _) => SetStatus(
            $"Opened: {Media.MediaFormat ?? "unknown"}; audio={Media.HasAudio}; " +
            $"codec={Media.AudioCodec ?? "none"}; stream={Media.AudioStreamIndex}");
        Media.MediaFailed += (_, e) => SetStatus($"Failed: {e.ErrorException.Message}", true);
        Media.MediaEnded += (_, _) => SetStatus("Playback ended");
        Media.MediaStateChanged += (_, e) => SetStatus($"State: {e.MediaState}");
        Media.MessageLogged += (_, e) =>
        {
            if (e.AspectName == "Element.Audio" ||
                e.MessageType is MediaLogMessageType.Warning or MediaLogMessageType.Error)
            {
                Console.WriteLine(e);
            }
        };

        var openUrl = CreateButton("Open", async (_, _) => await OpenSourceAsync());
        var browse = CreateButton("Browse", async (_, _) => await BrowseAsync());
        var play = CreateButton("Play", async (_, _) => await Media.Play());
        var pause = CreateButton("Pause", async (_, _) => await Media.Pause());
        var stop = CreateButton("Stop", async (_, _) => await Media.Stop());

        var sourceRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8,
        };
        sourceRow.Children.Add(SourcePath);
        Grid.SetColumn(browse, 1);
        sourceRow.Children.Add(browse);
        Grid.SetColumn(openUrl, 2);
        sourceRow.Children.Add(openUrl);

        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { play, pause, stop, Time },
        };

        PositionSlider.PointerPressed += (_, _) => IsDraggingPosition = true;
        PositionSlider.PointerReleased += async (_, _) =>
        {
            IsDraggingPosition = false;
            if (Media.IsSeekable)
                await Media.Seek(TimeSpan.FromSeconds(PositionSlider.Value));
        };

        var controls = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16, 12),
            Children = { FfmpegPath, sourceRow, PositionSlider, transport, Status },
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        root.Children.Add(Media);
        Grid.SetRow(controls, 1);
        root.Children.Add(controls);
        Content = root;

        UiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, UpdatePosition);
        UiTimer.Start();
        Opened += async (_, _) =>
        {
            if (bundledFfmpegDirectory is not null)
            {
                try
                {
                    await Task.Run(Library.LoadFFmpeg);
                    SetStatus($"FFmpeg {Library.FFmpegVersionInfo} loaded from {bundledFfmpegDirectory}");
                }
                catch (Exception exception)
                {
                    SetStatus($"Unable to load bundled FFmpeg: {exception.Message}", true);
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(startupSource))
            {
                SourcePath.Text = startupSource;
                await OpenSourceAsync();
            }
        };

        Closed += (_, _) =>
        {
            UiTimer.Stop();
            Media.Dispose();
        };
    }

    private static Button CreateButton(string text, EventHandler<global::Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button { Content = text, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += handler;
        return button;
    }

    private void SetStatus(string message, bool isError = false)
    {
        Status.Text = message;
        if (isError)
            Console.Error.WriteLine(message);
        else
            Console.WriteLine(message);
    }

    private async Task BrowseAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open media",
            AllowMultiple = false,
        });

        var file = files.FirstOrDefault();
        if (file?.TryGetLocalPath() is { } path)
        {
            SourcePath.Text = path;
            await OpenSourceAsync();
        }
    }

    private async Task OpenSourceAsync()
    {
        var source = SourcePath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            Status.Text = "Enter a media URL or choose a local file.";
            return;
        }

        var ffmpegDirectory = FfmpegPath.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
            Library.FFmpegDirectory = ffmpegDirectory;

        var uri = Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(Path.GetFullPath(source));

        Status.Text = $"Opening {uri}...";
        await Media.Open(uri);
    }

    private void UpdatePosition(object? sender, EventArgs e)
    {
        var current = Media.ActualPosition ?? TimeSpan.Zero;
        var duration = Media.NaturalDuration ?? TimeSpan.Zero;
        if (!IsDraggingPosition)
        {
            PositionSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            PositionSlider.Value = Math.Clamp(current.TotalSeconds, PositionSlider.Minimum, PositionSlider.Maximum);
        }

        Time.Text = $"{FormatTime(current)} / {FormatTime(duration)}";
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private static string? FindBundledFfmpegDirectory()
    {
        var requiredLibrary = OperatingSystem.IsWindows()
            ? "avcodec-61.dll"
            : OperatingSystem.IsLinux()
                ? "libavcodec.so.61"
                : OperatingSystem.IsMacOS()
                    ? "libavcodec.61.dylib"
                    : null;

        if (requiredLibrary is null)
            return null;

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "external", "ffmpegs", RuntimeInformation.RuntimeIdentifier);
            if (File.Exists(Path.Combine(candidate, requiredLibrary)))
                return candidate;
        }

        return null;
    }
}