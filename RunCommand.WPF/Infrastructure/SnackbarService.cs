using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            Enqueue(message, PackIconKind.CheckCircle, "#2EA04A", TimeSpan.FromSeconds(4));

        public static void ShowError(string message) =>
            Enqueue(message, PackIconKind.AlertCircle, "#D93333", TimeSpan.FromSeconds(8));

        public static void ShowInfo(string message) =>
            Enqueue(message, PackIconKind.InformationOutline, "#1565C0", TimeSpan.FromSeconds(5));

        private static void Enqueue(string message, PackIconKind icon, string iconColor, TimeSpan duration)
        {
            if (_queue is null || string.IsNullOrWhiteSpace(message))
                return;

            void Show()
            {
                var queue = _queue;
                if (queue is null) return;

                var brush = (Brush)new BrushConverter().ConvertFromString(iconColor)!;
                var content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    MaxWidth = 720
                };
                content.Children.Add(new PackIcon
                {
                    Kind = icon,
                    Width = 22,
                    Height = 22,
                    Foreground = brush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                });
                content.Children.Add(new TextBlock
                {
                    Text = message,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 640
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
