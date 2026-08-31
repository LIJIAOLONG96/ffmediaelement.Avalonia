namespace Unosquare.FFME.Platform
{
    using Common;
    using Engine;
    using Rendering;
    using System;

    internal sealed partial class MediaConnector
    {
        public IMediaRenderer CreateRenderer(MediaType mediaType, MediaEngine mediaCore)
        {
            return mediaType switch
            {
                MediaType.Video => new VideoRenderer(mediaCore),
                MediaType.Audio => new AudioRenderer(mediaCore),
                MediaType.Subtitle => new SubtitleRenderer(mediaCore),
                _ => throw new NotSupportedException($"No Avalonia renderer is available for media type '{mediaType}'."),
            };
        }
    }
}