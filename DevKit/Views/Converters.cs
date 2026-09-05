using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DevKit.Views;

/// <summary>布尔取反</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>布尔取反转 Visibility</summary>
public class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Collapsed;
}

/// <summary>取字符串首字母（大写）</summary>
public class InitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrEmpty(s)) return "?";
        return s[0].ToString().ToUpper();
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>颜色字符串(#RRGGBB)转同色浅色背景 Brush</summary>
public class ColorToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && s.StartsWith('#') && s.Length >= 7)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(s);
                return new SolidColorBrush(Color.FromArgb(0x1A, c.R, c.G, c.B));
            }
            catch { }
        }
        return new SolidColorBrush(Color.FromArgb(0x1A, 0x99, 0x99, 0x99));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
