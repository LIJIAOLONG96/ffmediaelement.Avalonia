namespace Unosquare.FFME
{
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;
    using Avalonia.Threading;
    using ClosedCaptions;
    using Common;
    using Engine;
    using Platform;
    using Primitives;
    using System;
    using System.Collections.Concurrent;

    public sealed partial class MediaElement : UserControl, IDisposable
    {
        public static event EventHandler<MediaLogMessageEventArgs> FFmpegMessageLogged;

        private readonly ConcurrentBag<string> PropertyUpdates = new();
        private readonly AtomicBoolean m_IsStateUpdating = new(false);
        private readonly DispatcherTimer UpdatesTimer;
        private readonly Image VideoView;
        private readonly TextBlock SubtitleView;
        private bool m_IsDisposed;

        static MediaElement()
        {
            MediaEngine.FFmpegMessageLogged += (sender, message) =>
                FFmpegMessageLogged?.Invoke(typeof(MediaElement), new MediaLogMessageEventArgs(message));
        }

        public MediaElement()
        {
            GuiContext = new GuiContext();
            VideoView = new Image
            {
                Stretch = Avalonia.Media.Stretch.Uniform,
                IsHitTestVisible = false,
            };
            SubtitleView = new TextBlock
            {
                FontSize = 28,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.LightYellow,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(24),
                IsHitTestVisible = false,
            };

            var layout = new Grid();
            layout.Children.Add(VideoView);
            layout.Children.Add(SubtitleView);
            Content = layout;

            MediaCore = new MediaEngine(this, new MediaConnector(this));
            MediaCore.State.PropertyChanged += (_, e) => PropertyUpdates.Add(e.PropertyName);

            UpdatesTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Background, OnUpdateTimerTick);
            UpdatesTimer.Start();

            DetachedFromVisualTree += (_, _) =>
            {
                if (UnloadedBehavior != MediaPlaybackState.Close)
                    return;

                Dispose();
            };
        }

        public static readonly StyledProperty<double> VolumeProperty =
            AvaloniaProperty.Register<MediaElement, double>(nameof(Volume), 1d, coerce: (_, value) => Math.Clamp(value, 0d, 1d));

        public static readonly StyledProperty<double> BalanceProperty =
            AvaloniaProperty.Register<MediaElement, double>(nameof(Balance), 0d, coerce: (_, value) => Math.Clamp(value, -1d, 1d));

        public static readonly StyledProperty<bool> IsMutedProperty =
            AvaloniaProperty.Register<MediaElement, bool>(nameof(IsMuted));

        public static readonly StyledProperty<bool> ScrubbingEnabledProperty =
            AvaloniaProperty.Register<MediaElement, bool>(nameof(ScrubbingEnabled));

        public static readonly StyledProperty<bool> VerticalSyncEnabledProperty =
            AvaloniaProperty.Register<MediaElement, bool>(nameof(VerticalSyncEnabled));

        public static readonly StyledProperty<double> SpeedRatioProperty =
            AvaloniaProperty.Register<MediaElement, double>(nameof(SpeedRatio), 1d, coerce: (_, value) => Math.Clamp(value, 0.1d, 10d));

        public static readonly StyledProperty<TimeSpan> PositionProperty =
            AvaloniaProperty.Register<MediaElement, TimeSpan>(nameof(Position));

        public static readonly StyledProperty<MediaPlaybackState> LoadedBehaviorProperty =
            AvaloniaProperty.Register<MediaElement, MediaPlaybackState>(nameof(LoadedBehavior), MediaPlaybackState.Play);

        public static readonly StyledProperty<MediaPlaybackState> UnloadedBehaviorProperty =
            AvaloniaProperty.Register<MediaElement, MediaPlaybackState>(nameof(UnloadedBehavior), MediaPlaybackState.Close);

        public static readonly StyledProperty<MediaPlaybackState> LoopingBehaviorProperty =
            AvaloniaProperty.Register<MediaElement, MediaPlaybackState>(nameof(LoopingBehavior), MediaPlaybackState.Pause);

        public static readonly StyledProperty<CaptionsChannel> ClosedCaptionsChannelProperty =
            AvaloniaProperty.Register<MediaElement, CaptionsChannel>(nameof(ClosedCaptionsChannel));

        public static readonly StyledProperty<Stretch> StretchProperty =
            AvaloniaProperty.Register<MediaElement, Stretch>(nameof(Stretch), Stretch.Uniform);

        public double Volume { get => GetValue(VolumeProperty); set => SetValue(VolumeProperty, value); }

        public double Balance { get => GetValue(BalanceProperty); set => SetValue(BalanceProperty, value); }

        public bool IsMuted { get => GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }

        public bool ScrubbingEnabled { get => GetValue(ScrubbingEnabledProperty); set => SetValue(ScrubbingEnabledProperty, value); }

        public bool VerticalSyncEnabled { get => GetValue(VerticalSyncEnabledProperty); set => SetValue(VerticalSyncEnabledProperty, value); }

        public double SpeedRatio { get => GetValue(SpeedRatioProperty); set => SetValue(SpeedRatioProperty, value); }

        public TimeSpan Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

        public MediaPlaybackState LoadedBehavior { get => GetValue(LoadedBehaviorProperty); set => SetValue(LoadedBehaviorProperty, value); }

        public MediaPlaybackState UnloadedBehavior { get => GetValue(UnloadedBehaviorProperty); set => SetValue(UnloadedBehaviorProperty, value); }

        public MediaPlaybackState LoopingBehavior { get => GetValue(LoopingBehaviorProperty); set => SetValue(LoopingBehaviorProperty, value); }

        public CaptionsChannel ClosedCaptionsChannel { get => GetValue(ClosedCaptionsChannelProperty); set => SetValue(ClosedCaptionsChannelProperty, value); }

        public Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }

        internal IGuiContext GuiContext { get; }

        internal bool IsStateUpdating
        {
            get => m_IsStateUpdating.Value;
            set => m_IsStateUpdating.Value = value;
        }

        internal void SetVideoBitmap(WriteableBitmap bitmap) => VideoView.Source = bitmap;

        internal void InvalidateVideo() => VideoView.InvalidateVisual();

        internal void SetSubtitleText(string text) => SubtitleView.Text = text ?? string.Empty;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (MediaCore == null || IsStateUpdating)
                return;

            if (change.Property == VolumeProperty) MediaCore.State.Volume = Volume;
            else if (change.Property == BalanceProperty) MediaCore.State.Balance = Balance;
            else if (change.Property == IsMutedProperty) MediaCore.State.IsMuted = IsMuted;
            else if (change.Property == ScrubbingEnabledProperty) MediaCore.State.ScrubbingEnabled = ScrubbingEnabled;
            else if (change.Property == VerticalSyncEnabledProperty) MediaCore.State.VerticalSyncEnabled = VerticalSyncEnabled;
            else if (change.Property == SpeedRatioProperty) MediaCore.State.SpeedRatio = SpeedRatio;
            else if (change.Property == PositionProperty && IsSeekable) _ = Seek(Position);
            else if (change.Property == StretchProperty) VideoView.Stretch = Stretch;
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;

            m_IsDisposed = true;
            UpdatesTimer?.Stop();
            MediaCore?.Dispose();
        }

        private void OnUpdateTimerTick(object sender, EventArgs e)
        {
            if (MediaCore == null || PropertyUpdates.IsEmpty)
                return;

            IsStateUpdating = true;
            try
            {
                while (PropertyUpdates.TryTake(out var propertyName))
                {
                    if (propertyName == nameof(Position) && !IsSeeking)
                        SetCurrentValue(PositionProperty, MediaCore.State.Position);
                    else if (propertyName == nameof(Volume)) SetCurrentValue(VolumeProperty, MediaCore.State.Volume);
                    else if (propertyName == nameof(Balance)) SetCurrentValue(BalanceProperty, MediaCore.State.Balance);
                    else if (propertyName == nameof(IsMuted)) SetCurrentValue(IsMutedProperty, MediaCore.State.IsMuted);

                    NotifyPropertyChangedEvent(propertyName);
                }
            }
            finally
            {
                IsStateUpdating = false;
            }
        }
    }
}