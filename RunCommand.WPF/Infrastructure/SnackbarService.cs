using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;

namespace RunCommand.WPF.Infrastructure
{
    /// <summary>App-wide snackbar queue hosted on the main window.</summary>
    public static class SnackbarService
    {
        private static SnackbarMessageQueue? _queue;

        public static void Initialize(SnackbarMessageQueue queue) => _queue = queue;

        /// <summary>Temporarily route snackbars to another queue (e.g. a modal dialog).</summary>
        public static SnackbarMessageQueue? UseQueue(SnackbarMessageQueue queue)
        {
            var previous = _queue;
            _queue = queue;
            return previous;
        }

        public static void RestoreQueue(SnackbarMessageQueue? queue) => _queue = queue;

        public static void ShowSuccess(string message) =>
            Enqueue(message, PackIconKind.CheckCircle, TimeSpan.FromSeconds(4));

        public static void ShowError(string message) =>
            Enqueue(message, PackIconKind.AlertCircle, TimeSpan.FromSeconds(8));

        public static void ShowInfo(string message) =>
            Enqueue(message, PackIconKind.Information, TimeSpan.FromSeconds(5));

        private static void Enqueue(string message, PackIconKind icon, TimeSpan duration)
        {
            if (_queue is null || string.IsNullOrWhiteSpace(message))
                return;

            void Show()
            {
                var queue = _queue;
                if (queue is null) return;

                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new PackIcon
                {
                    Kind = icon,
                    Width = 20,
                    Height = 20,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                });
                content.Children.Add(new TextBlock
                {
                    Text = message,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });

                queue.Enqueue(content, null, null, null, false, true, duration);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                Show();
                return;
            }

            dispatcher.BeginInvoke(Show, DispatcherPriority.Normal);
        }
    }
}
