# FFMediaElement.Avalonia

[![NuGet](https://img.shields.io/nuget/v/FFMediaElement.Avalonia.svg)](https://www.nuget.org/packages/FFMediaElement.Avalonia)
[![NuGet Downloads](https://img.shields.io/nuget/dt/FFMediaElement.Avalonia.svg)](https://www.nuget.org/packages/FFMediaElement.Avalonia)
[![Build](https://github.com/LIJIAOLONG96/ffmediaelement.Avalonia/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/LIJIAOLONG96/ffmediaelement.Avalonia/actions/workflows/publish-nuget.yml)

`FFMediaElement.Avalonia` is an Avalonia port of
[Unosquare FFME](https://github.com/unosquare/ffmediaelement), adapted from its
WPF implementation for cross-platform Avalonia desktop applications.

The project retains the FFME media container, FFmpeg decoding, command,
buffering, timing, seeking, and worker pipeline. The WPF-specific control and
renderers are replaced with an Avalonia `MediaElement`, Avalonia software video
rendering, timed-text subtitle rendering, and PortAudio output.

Refer to the upstream [FFME repository](https://github.com/unosquare/ffmediaelement)
for the media engine architecture, supported formats, stream options, custom
input streams, media events, and other common FFME concepts. This README focuses
on installing and using the Avalonia port and on behavior that differs from the
upstream WPF package.

## Install

```bash
dotnet add package FFMediaElement.Avalonia
```

```xml
<PackageReference Include="FFMediaElement.Avalonia" Version="0.1.1" />
```

The package targets .NET 8 and uses Avalonia 11.

## Differences from FFME.Windows

| Area | Upstream FFME.Windows | FFMediaElement.Avalonia |
| --- | --- | --- |
| NuGet package | `FFME.Windows` | `FFMediaElement.Avalonia` |
| UI framework | WPF | Avalonia 11 |
| Target framework | Windows targets | .NET 8 desktop |
| Assembly | `ffme.win` | `ffme.avalonia` |
| CLR namespace | `Unosquare.FFME` | `Unosquare.FFME` |
| XAML namespace | `assembly=ffme.win` | `assembly=ffme.avalonia` |
| Bindable properties | WPF dependency properties | Avalonia styled properties |
| Video output | WPF/interop renderers | Avalonia `WriteableBitmap` software renderer |
| Audio output | Windows audio renderer, optional SoundTouch | PortAudio, 48 kHz 16-bit stereo PCM |
| Platforms | Windows | Windows, Linux, and macOS desktop |
| Player controls | Supplied by the application | Supplied by the application |

The CLR namespace intentionally remains `Unosquare.FFME` to preserve the shared
FFME API. The NuGet package name and assembly name are different, so Avalonia
XAML must reference `ffme.avalonia`.

The Avalonia port currently does not provide every WPF renderer-specific API.
In particular, WPF rendering callbacks, DirectSound integration, SoundTouch,
closed-caption presentation, screenshot helpers, and interop video rendering
are not part of the Avalonia package.

Changing `SpeedRatio` currently changes audio pitch because the PortAudio
renderer adjusts playback by sampling frames rather than using SoundTouch.

## FFmpeg Native Libraries

Like upstream FFME, this package uses `FFmpeg.AutoGen` and requires FFmpeg shared
libraries. The native FFmpeg binaries are not included in the NuGet package.

Install a compatible FFmpeg 7 shared build for the current operating system and
architecture, then set `Library.FFmpegDirectory` before opening media. A
standalone `ffmpeg` executable is not sufficient.

For example, a Windows FFmpeg 7.1 directory contains:

```text
avcodec-61.dll
avformat-61.dll
avutil-59.dll
swresample-5.dll
swscale-8.dll
```

This repository includes a helper for verified Windows and Linux builds:

```powershell
.\Support\download-ffmpeg.ps1
```

The script installs `win-x64`, `win-arm64`, `linux-x64`, and `linux-arm64` under
`external/ffmpegs`. macOS requires a separately obtained compatible shared build
containing the FFmpeg `.dylib` files.

PortAudio native assets are restored through the NuGet dependencies for Windows,
Linux, and macOS.

## Avalonia Usage

Use the Avalonia assembly in XAML:

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ffme="clr-namespace:Unosquare.FFME;assembly=ffme.avalonia">
    <Grid RowDefinitions="*,Auto">
        <ffme:MediaElement
            x:Name="Media"
            Stretch="Uniform" />

        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="8">
            <Button Content="Play" Click="PlayClicked" />
            <Button Content="Pause" Click="PauseClicked" />
            <Button Content="Stop" Click="StopClicked" />
        </StackPanel>
    </Grid>
</Window>
```

Configure FFmpeg before opening media:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Unosquare.FFME;
using Unosquare.FFME.Common;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Library.FFmpegDirectory = @"C:\ffmpeg\win-x64";
        Media.LoadedBehavior = MediaPlaybackState.Play;
        Media.MediaFailed += (_, eventArgs) =>
            Console.Error.WriteLine(eventArgs.ErrorException);
    }

    public async void OpenMedia(string path) =>
        await Media.Open(new Uri(path));

    private async void PlayClicked(object? sender, RoutedEventArgs eventArgs) =>
        await Media.Play();

    private async void PauseClicked(object? sender, RoutedEventArgs eventArgs) =>
        await Media.Pause();

    private async void StopClicked(object? sender, RoutedEventArgs eventArgs) =>
        await Media.Stop();
}
```

`LoadedBehavior` defaults to `Play`. Set it to `Pause` when media should open
without starting playback. `Library.LoadFFmpeg()` can be called during startup
to validate the native installation early; otherwise FFmpeg loads when media is
first opened.

The control renders video and subtitles only. It intentionally does not contain
a built-in transport bar, file picker, or play button. Add those controls in the
application and call `Play()`, `Pause()`, `Stop()`, and `Seek()` as shown above.

## Avalonia Properties and Events

The Avalonia control exposes styled properties for `Volume`, `Balance`,
`IsMuted`, `SpeedRatio`, `Position`, `Stretch`, `LoadedBehavior`,
`UnloadedBehavior`, and `LoopingBehavior`.

Shared FFME state and events remain available under the `Unosquare.FFME` and
`Unosquare.FFME.Common` namespaces, including:

- `MediaState`, `ActualPosition`, `NaturalDuration`, and `MediaInfo`
- `IsPlaying`, `IsPaused`, `IsSeekable`, and buffering/seeking state
- `HasAudio`, `HasVideo`, `HasSubtitles`, and codec/stream information
- `MediaOpened`, `MediaReady`, `MediaEnded`, `MediaClosed`, and `MediaFailed`
- `MediaStateChanged`, `PositionChanged`, buffering, seeking, and logging events

Some FFME decoding and logging events run on worker threads. Dispatch UI work
to Avalonia's `Dispatcher.UIThread` from those handlers.

## Run the Avalonia Sample

Download FFmpeg and run the sample player:

```powershell
.\Support\download-ffmpeg.ps1
dotnet run --project .\Unosquare.FFME.Avalonia.Sample\Unosquare.FFME.Avalonia.Sample.csproj
```

Pass a local file or URL to open it at startup:

```powershell
dotnet run --project .\Unosquare.FFME.Avalonia.Sample\Unosquare.FFME.Avalonia.Sample.csproj -- "C:\media\video.mp4"
```

The sample searches for `external/ffmpegs/<runtime-identifier>` automatically
and also allows the FFmpeg directory to be entered manually.

## Build

```powershell
dotnet build .\Unosquare.FFME.sln --configuration Debug
dotnet pack .\Unosquare.FFME.Avalonia\Unosquare.FFME.Avalonia.csproj --configuration Release
```

Pushes to `main` and `develop` produce package artifacts. Tags matching `v*`
publish `FFMediaElement.Avalonia` to NuGet.org through trusted publishing.

## Attribution and License

This is a modified Avalonia port of
[Unosquare FFME](https://github.com/unosquare/ffmediaelement), not an official
Unosquare release. The FFME engine and shared `MediaElement` code remain subject
to the upstream Microsoft Public License (Ms-PL).

The Ms-PL permits using, modifying, creating derivative works from, and
redistributing FFME, subject to its conditions. In particular, existing
copyright and attribution notices must be retained, and source distributions
must include the license. For that reason, the upstream license notices are
intentionally retained in [LICENSE](LICENSE) and included in the NuGet package.

FFmpeg, FFmpeg.AutoGen, PortAudio, Avalonia, and other dependencies remain under
their respective licenses. See [LICENSE](LICENSE) and the dependency packages
for the applicable notices. This section is a project description, not legal
advice.
