using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LogAnalyzer
{
    public class LevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var app = Application.Current;
            string level = (value as string ?? string.Empty).ToUpperInvariant();
            string key;
            switch (level)
            {
                case "ERROR": key = "ErrorBrush"; break;
                case "WARN":  key = "WarnBrush";  break;
                case "DEBUG": key = "DebugBrush"; break;
                default:      key = "InfoBrush";  break;
            }
            return app?.TryFindResource(key) as Brush ?? Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class LevelToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var app = Application.Current;
            string level = (value as string ?? string.Empty).ToUpperInvariant();
            if (level == "ERROR")
            {
                var brush = app?.TryFindResource("ErrorBrush") as SolidColorBrush;
                if (brush != null)
                    return new SolidColorBrush(Color.FromArgb(30, brush.Color.R, brush.Color.G, brush.Color.B));
            }
            return Brushes.Transparent;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class PercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return 0.0;
            if (values[0] is int count && values[1] is int total && total > 0)
                return (double)count / total * 200.0;
            if (values[0] is double d && values[1] is int t2 && t2 > 0)
                return d / t2 * 200.0;
            return 0.0;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class IntGreaterThanZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i) return i > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is long l) return l > 0 ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class DoubleToPercentStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double d ? $"{d:F1}%" : "0%";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// IsDragOver=true, IsDragOverValid=true/false 두 값을 받아
    /// parameter="valid" 이면 유효 드래그 색(Accent), 아니면 오류 색(Red)을 반환한다.
    /// </summary>
    public class DragOverBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isDragOver  = values.Length > 0 && values[0] is bool b0 && b0;
            bool isDragValid = values.Length > 1 && values[1] is bool b1 && b1;

            if (!isDragOver) return Brushes.Transparent;

            var app = Application.Current;
            if (isDragValid)
                return app?.TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;

            return Brushes.Crimson;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
