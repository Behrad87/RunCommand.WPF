using System;
using System.Windows;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;

namespace RunCommand.WPF.Infrastructure
{
    /// <summary>App-wide snackbar queue hosted on the main window.</summary>
    public static class SnackbarService
    {
        private static SnackbarMessageQueue? _queue;

        public static void Initialize(SnackbarMessageQueue queue) => _queue = queue;

        public static void ShowSuccess(string message) => Enqueue(message, TimeSpan.FromSeconds(4));

        public static void ShowError(string message) => Enqueue(message, TimeSpan.FromSeconds(8));

        public static void ShowInfo(string message) => Enqueue(message, TimeSpan.FromSeconds(5));

        private static void Enqueue(string message, TimeSpan duration)
        {
            if (_queue is null || string.IsNullOrWhiteSpace(message))
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                _queue.Enqueue(message, null, null, null, false, true, duration);
                return;
            }

            dispatcher.BeginInvoke(() =>
                _queue.Enqueue(message, null, null, null, false, true, duration),
                DispatcherPriority.Normal);
        }
    }
}
