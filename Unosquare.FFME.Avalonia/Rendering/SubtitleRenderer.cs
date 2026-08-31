namespace Unosquare.FFME.Rendering
{
    using Avalonia.Threading;
    using Container;
    using Engine;
    using Platform;
    using System;
    using System.Linq;

    internal sealed class SubtitleRenderer : IMediaRenderer
    {
        private readonly MediaElement Parent;
        private readonly object SyncLock = new();
        private string BlockText = string.Empty;
        private string RequestedText = string.Empty;
        private TimeSpan? StartTime;
        private TimeSpan? EndTime;
        private long TextGeneration;

        public SubtitleRenderer(MediaEngine mediaCore)
        {
            MediaCore = mediaCore;
            Parent = mediaCore.Parent as MediaElement;
        }

        public MediaEngine MediaCore { get; }

        public void OnStarting() { }

        public void OnPlay() { }

        public void OnPause() { }

        public void OnStop() => Clear();

        public void OnClose() => Clear();

        public void OnSeek() => Clear();

        public void Update(TimeSpan clockPosition)
        {
            string text;
            lock (SyncLock)
            {
                text = StartTime.HasValue && EndTime.HasValue &&
                    clockPosition >= StartTime.Value && clockPosition <= EndTime.Value
                    ? BlockText
                    : string.Empty;
            }

            SetText(text);
        }

        public void Render(MediaBlock mediaBlock, TimeSpan clockPosition)
        {
            if (mediaBlock is SubtitleBlock block)
            {
                var text = string.Join(Environment.NewLine, block.Text.Where(line => !string.IsNullOrWhiteSpace(line)));
                lock (SyncLock)
                {
                    BlockText = text;
                    StartTime = block.StartTime;
                    EndTime = block.EndTime;
                }

                Update(clockPosition);
            }
        }

        private void Clear()
        {
            lock (SyncLock)
            {
                BlockText = string.Empty;
                StartTime = null;
                EndTime = null;
                TextGeneration++;
            }

            SetText(string.Empty, true);
        }

        private void SetText(string text, bool force = false)
        {
            long generation;
            lock (SyncLock)
            {
                if (!force && RequestedText == text)
                    return;

                RequestedText = text;
                generation = TextGeneration;
            }

            Dispatcher.UIThread.Post(() =>
            {
                lock (SyncLock)
                {
                    if (generation != TextGeneration || RequestedText != text)
                        return;

                    Parent?.SetSubtitleText(text);
                }
            });
        }
    }
}