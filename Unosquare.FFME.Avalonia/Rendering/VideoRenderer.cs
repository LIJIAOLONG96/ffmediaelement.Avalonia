namespace Unosquare.FFME.Rendering
{
    using Avalonia;
    using Avalonia.Media.Imaging;
    using Avalonia.Platform;
    using Avalonia.Threading;
    using Container;
    using Engine;
    using Platform;
    using System;
    using System.Runtime.InteropServices;

    internal sealed class VideoRenderer : IMediaRenderer
    {
        private readonly MediaElement Parent;
        private readonly object SyncLock = new();
        private PendingFrame LatestFrame;
        private bool IsPresentationScheduled;
        private bool IsClosed;
        private long FrameGeneration;
        private WriteableBitmap TargetBitmap;

        public VideoRenderer(MediaEngine mediaCore)
        {
            MediaCore = mediaCore;
            Parent = mediaCore.Parent as MediaElement;
        }

        public MediaEngine MediaCore { get; }

        public void OnStarting() { }

        public void OnPlay() { }

        public void OnPause() { }

        public void OnStop() => OnSeek();

        public void OnSeek()
        {
            lock (SyncLock)
            {
                FrameGeneration++;
                LatestFrame = null;
            }
        }

        public void OnClose()
        {
            lock (SyncLock)
            {
                IsClosed = true;
                FrameGeneration++;
                LatestFrame = null;
            }

            Dispatcher.UIThread.Post(() =>
            {
                Parent?.SetVideoBitmap(null);
                TargetBitmap?.Dispose();
                TargetBitmap = null;
            });
        }

        public void Update(TimeSpan clockPosition) { }

        public void Render(MediaBlock mediaBlock, TimeSpan clockPosition)
        {
            if (mediaBlock is not VideoBlock block || block.IsDisposed || !block.TryAcquireReaderLock(out var readLock))
                return;

            byte[] pixels;
            int width;
            int height;
            int sourceStride;
            try
            {
                width = block.PixelWidth;
                height = block.PixelHeight;
                sourceStride = block.PictureBufferStride;
                if (width <= 0 || height <= 0 || sourceStride <= 0)
                    return;

                pixels = new byte[Math.Min(block.BufferLength, sourceStride * height)];
                Marshal.Copy(block.Buffer, pixels, 0, pixels.Length);
            }
            finally
            {
                readLock.Dispose();
            }

            lock (SyncLock)
            {
                if (IsClosed)
                    return;

                LatestFrame = new PendingFrame(pixels, width, height, sourceStride, FrameGeneration);
                if (IsPresentationScheduled)
                    return;

                IsPresentationScheduled = true;
                Dispatcher.UIThread.Post(PresentPendingFrame, DispatcherPriority.Render);
            }
        }

        private void PresentPendingFrame()
        {
            PendingFrame frame;
            lock (SyncLock)
            {
                frame = LatestFrame;
                LatestFrame = null;
            }

            if (frame != null)
            {
                lock (SyncLock)
                {
                    if (!IsClosed && frame.Generation == FrameGeneration)
                        Present(frame.Pixels, frame.Width, frame.Height, frame.SourceStride);
                }
            }

            lock (SyncLock)
            {
                if (LatestFrame != null && !IsClosed)
                {
                    Dispatcher.UIThread.Post(PresentPendingFrame, DispatcherPriority.Render);
                    return;
                }

                IsPresentationScheduled = false;
            }
        }

        private unsafe void Present(byte[] pixels, int width, int height, int sourceStride)
        {
            if (TargetBitmap == null || TargetBitmap.PixelSize.Width != width || TargetBitmap.PixelSize.Height != height)
            {
                TargetBitmap?.Dispose();
                TargetBitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Unpremul);
                Parent?.SetVideoBitmap(TargetBitmap);
            }

            using var framebuffer = TargetBitmap.Lock();
            fixed (byte* source = pixels)
            {
                var rowLength = Math.Min(sourceStride, framebuffer.RowBytes);
                for (var row = 0; row < height; row++)
                {
                    Buffer.MemoryCopy(
                        source + (row * sourceStride),
                        (byte*)framebuffer.Address + (row * framebuffer.RowBytes),
                        framebuffer.RowBytes,
                        rowLength);
                }
            }
        }

        private sealed class PendingFrame
        {
            public PendingFrame(byte[] pixels, int width, int height, int sourceStride, long generation)
            {
                Pixels = pixels;
                Width = width;
                Height = height;
                SourceStride = sourceStride;
                Generation = generation;
            }

            public byte[] Pixels { get; }

            public int Width { get; }

            public int Height { get; }

            public int SourceStride { get; }

            public long Generation { get; }
        }
    }
}