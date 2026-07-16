using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RunCommand.WPF.Models;

namespace RunCommand.WPF.Converters
{
    public class StatusToBrushConverter : IValueConverter
    {
        private static readonly Brush Online = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4A));    // green
        private static readonly Brush Offline = new SolidColorBrush(Color.FromRgb(0xD9, 0x33, 0x33));   // red
        private static readonly Brush Checking = new SolidColorBrush(Color.FromRgb(0xE8, 0xA6, 0x1E));  // amber
        private static readonly Brush AuthFailed = new SolidColorBrush(Color.FromRgb(0xE8, 0x6A, 0x1E));// orange
        private static readonly Brush Unknown = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));   // gray

        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            ServerStatus.Online => Online,
            ServerStatus.Offline => Offline,
            ServerStatus.Checking => Checking,
            ServerStatus.AuthFailed => AuthFailed,
            _ => Unknown
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
