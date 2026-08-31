namespace Unosquare.FFME.Platform
{
    using Avalonia.Threading;
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;

    internal sealed class GuiContext : IGuiContext
    {
        public GuiContextType Type => GuiContextType.Avalonia;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ConfiguredTaskAwaitable InvokeAsync(Action callback) =>
            Dispatcher.UIThread.InvokeAsync(callback).GetTask().ConfigureAwait(true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueInvoke(Action callback) => Dispatcher.UIThread.Post(callback);
    }
}