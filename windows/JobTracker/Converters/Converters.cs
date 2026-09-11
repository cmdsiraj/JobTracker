// Shared value converters for XAML bindings.

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using JobTracker.Models;

namespace JobTracker.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, Brush> Brushes = new()
    {
        ["Orange"] = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        ["Blue"] = new SolidColorBrush(Color.FromRgb(0x35, 0x7A, 0xBD)),
        ["Cyan"] = new SolidColorBrush(Color.FromRgb(0x22, 0xA6, 0xB3)),
        ["Teal"] = new SolidColorBrush(Color.FromRgb(0x0E, 0x8E, 0x80)),
        ["Purple"] = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6)),
        ["Indigo"] = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)),
        ["Green"] = new SolidColorBrush(Color.FromRgb(0x22, 0xA5, 0x5D)),
        ["Red"] = new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45)),
        ["Gray"] = System.Windows.Media.Brushes.Gray,
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ApplicationStatus status => status.ColorKey(),
            LeadStage stage => stage.ColorKey(),
            string s => s,
            _ => "Gray",
        };
        return Brushes.GetValueOrDefault(key, System.Windows.Media.Brushes.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ApplicationStatus status ? status.IconGlyph() : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusToDisplayNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ApplicationStatus status => status.DisplayName(),
        LeadStage stage => stage.DisplayName(),
        LeadType leadType => leadType.DisplayName(),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (parameter is string s && s == "Invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasText = value is string s && s.Length > 0;
        if (parameter is string p && p == "Invert") hasText = !hasText;
        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = value switch
        {
            int i => i > 0,
            System.Collections.ICollection c => c.Count > 0,
            System.Collections.IEnumerable e => e.Cast<object>().Any(),
            _ => false,
        };
        if (parameter is string p && p == "Invert") hasItems = !hasItems;
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter is string p && p == "Invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

/// "2 hours ago" style relative time, matching SwiftUI's `.relative(presentation: .named)`.
public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset date) return "";
        var delta = DateTimeOffset.Now - date;
        if (delta.TotalSeconds < 0) return "just now";
        if (delta.TotalSeconds < 45) return "just now";
        if (delta.TotalMinutes < 1.5) return "1 minute ago";
        if (delta.TotalMinutes < 45) return $"{(int)Math.Round(delta.TotalMinutes)} minutes ago";
        if (delta.TotalHours < 1.5) return "1 hour ago";
        if (delta.TotalHours < 22) return $"{(int)Math.Round(delta.TotalHours)} hours ago";
        if (delta.TotalDays < 1.5) return "1 day ago";
        if (delta.TotalDays < 25) return $"{(int)Math.Round(delta.TotalDays)} days ago";
        if (delta.TotalDays < 45) return "1 month ago";
        if (delta.TotalDays < 320) return $"{(int)Math.Round(delta.TotalDays / 30)} months ago";
        var years = Math.Max(1, (int)Math.Round(delta.TotalDays / 365));
        return years == 1 ? "1 year ago" : $"{years} years ago";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? $"{Math.Round(d * 100)}%" : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? parameter : Binding.DoNothing;
}
