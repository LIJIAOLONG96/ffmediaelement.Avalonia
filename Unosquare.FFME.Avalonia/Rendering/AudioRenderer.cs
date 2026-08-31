namespace Unosquare.FFME.Rendering
{
    using Common;
    using Container;
    using Diagnostics;
    using Engine;
    using Platform;
    using PortAudioSharp;
    using Primitives;
    using System;
    using PortAudioStream = PortAudioSharp.Stream;

    internal sealed class AudioRenderer : IMediaRenderer, ILoggingSource, IDisposable
    {
        private const uint FramesPerBuffer = 1024;
        private static readonly int FrameSize = Constants.AudioBytesPerSample * Constants.AudioChannelCount;
        private static readonly object InitializationLock = new();
        private static bool IsPortAudioInitialized;

        private readonly object SyncLock = new();
        private readonly byte[] SourceBuffer = new byte[FramesPerBuffer * FrameSize * 10];
        private readonly PortAudioStream.Callback AudioCallback;
        private CircularBuffer AudioBuffer;
        private PortAudioStream AudioStream;
        private bool IsClosing;
        private bool IsDisposed;

        public AudioRenderer(MediaEngine mediaCore)
        {
            MediaCore = mediaCore ?? throw new ArgumentNullException(nameof(mediaCore));
            AudioCallback = ReadAudio;

            if (!MediaCore.State.HasAudio)
                return;

            try
            {
                Initialize();
            }
            catch (Exception exception)
            {
                Destroy();
                this.LogWarning(Aspects.AudioRenderer, $"Unable to initialize PortAudio output: {exception.Message}");
            }
        }

        ILoggingHandler ILoggingSource.LoggingHandler => MediaCore;

        public MediaEngine MediaCore { get; }

        public void OnStarting() { }

        public void OnPlay() { }

        public void OnPause() { }

        public void OnStop() => OnSeek();

        public void OnClose()
        {
            lock (SyncLock)
            {
                IsClosing = true;
                Destroy();
            }
        }

        public void OnSeek()
        {
            lock (SyncLock)
                AudioBuffer?.Clear();
        }

        public void Render(MediaBlock mediaBlock, TimeSpan clockPosition)
        {
            if (IsClosing || MediaCore.State.IsSeeking || mediaBlock is not AudioBlock audioBlock)
                return;

            lock (SyncLock)
            {
                if (AudioBuffer == null || AudioStream == null)
                    return;

                var audioBlocks = MediaCore.Blocks[MediaType.Audio];
                while (audioBlock != null && AudioBuffer.CapacityPercent < 0.5)
                {
                    if (!audioBlock.TryAcquireReaderLock(out var readLock))
                        return;

                    using (readLock)
                    {
                        if (AudioBuffer.WriteTag < audioBlock.EndTime &&
                            audioBlock.SamplesBufferLength <= AudioBuffer.WritableCount)
                        {
                            AudioBuffer.Write(
                                audioBlock.Buffer,
                                audioBlock.SamplesBufferLength,
                                audioBlock.EndTime,
                                false);
                        }

                        audioBlock = audioBlocks.Next(audioBlock) as AudioBlock;
                    }
                }
            }
        }

        public void Update(TimeSpan clockPosition) { }

        public void Dispose()
        {
            lock (SyncLock)
            {
                if (IsDisposed)
                    return;

                IsDisposed = true;
                IsClosing = true;
                Destroy();
            }
        }

        private void Initialize()
        {
            lock (InitializationLock)
            {
                if (!IsPortAudioInitialized)
                {
                    PortAudio.Initialize();
                    IsPortAudioInitialized = true;
                }
            }

            var deviceIndex = PortAudio.DefaultOutputDevice;
            if (deviceIndex == PortAudio.NoDevice)
            {
                this.LogWarning(Aspects.AudioRenderer, "No default audio output device was found.");
                return;
            }

            var deviceInfo = PortAudio.GetDeviceInfo(deviceIndex);
            var outputParameters = new StreamParameters
            {
                device = deviceIndex,
                channelCount = Constants.AudioChannelCount,
                sampleFormat = SampleFormat.Int16,
                suggestedLatency = deviceInfo.defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero,
            };

            AudioBuffer = new CircularBuffer(Constants.AudioSampleRate * FrameSize * 2);
            AudioStream = new PortAudioStream(
                inParams: null,
                outParams: outputParameters,
                sampleRate: Constants.AudioSampleRate,
                framesPerBuffer: FramesPerBuffer,
                streamFlags: StreamFlags.ClipOff,
                callback: AudioCallback,
                userData: this);
            AudioStream.Start();
            this.LogInfo(
                Aspects.AudioRenderer,
                $"PortAudio output started on device {deviceIndex}: " +
                $"{Constants.AudioSampleRate} Hz, {Constants.AudioBitsPerSample}-bit, {Constants.AudioChannelCount} channels.");
        }

        private void Destroy()
        {
            if (AudioStream != null)
            {
                try { AudioStream.Abort(); }
                catch { }

                AudioStream.Dispose();
                AudioStream = null;
            }

            AudioBuffer?.Dispose();
            AudioBuffer = null;
        }

        private unsafe StreamCallbackResult ReadAudio(
            IntPtr input,
            IntPtr output,
            uint frameCount,
            ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags,
            IntPtr userData)
        {
            var outputFrameCount = checked((int)frameCount);
            var outputBytes = checked(outputFrameCount * FrameSize);
            new Span<byte>(output.ToPointer(), outputBytes).Clear();

            if (IsClosing || !MediaCore.State.IsPlaying || AudioBuffer == null)
                return StreamCallbackResult.Continue;

            var speedRatio = Math.Clamp(MediaCore.State.SpeedRatio, 0.1, 10.0);
            var sourceFrameCount = Math.Max(1, checked((int)Math.Ceiling(outputFrameCount * speedRatio)));
            var sourceBytes = checked(sourceFrameCount * FrameSize);
            if (sourceBytes > SourceBuffer.Length || AudioBuffer.ReadableCount < sourceBytes)
                return StreamCallbackResult.Continue;

            try
            {
                AudioBuffer.Read(sourceBytes, SourceBuffer, 0);
                WriteAdjustedSamples(output, outputFrameCount, sourceFrameCount, speedRatio);
            }
            catch
            {
                new Span<byte>(output.ToPointer(), outputBytes).Clear();
            }

            return StreamCallbackResult.Continue;
        }

        private unsafe void WriteAdjustedSamples(
            IntPtr output,
            int outputFrameCount,
            int sourceFrameCount,
            double speedRatio)
        {
            var volume = MediaCore.State.IsMuted ? 0d : Math.Clamp(MediaCore.State.Volume, 0d, 1d);
            var balance = Math.Clamp(MediaCore.State.Balance, -1d, 1d);
            var leftGain = volume * (balance > 0d ? 1d - balance : 1d);
            var rightGain = volume * (balance < 0d ? 1d + balance : 1d);
            var target = new Span<short>(output.ToPointer(), outputFrameCount * Constants.AudioChannelCount);

            fixed (byte* sourceBytes = SourceBuffer)
            {
                var source = (short*)sourceBytes;
                for (var outputFrame = 0; outputFrame < outputFrameCount; outputFrame++)
                {
                    var sourceFrame = Math.Min(sourceFrameCount - 1, (int)(outputFrame * speedRatio));
                    var sourceOffset = sourceFrame * Constants.AudioChannelCount;
                    var targetOffset = outputFrame * Constants.AudioChannelCount;
                    target[targetOffset] = ScaleSample(source[sourceOffset], leftGain);
                    target[targetOffset + 1] = ScaleSample(source[sourceOffset + 1], rightGain);
                }
            }
        }

        private static short ScaleSample(short sample, double gain) =>
            (short)Math.Clamp((int)Math.Round(sample * gain), short.MinValue, short.MaxValue);
    }
}