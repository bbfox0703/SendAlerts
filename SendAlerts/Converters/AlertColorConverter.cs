using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SendAlerts.Converters;

/// <summary>
/// 警報顏色轉換器 - 當值為 true 時顯示紅色（警報狀態）
/// </summary>
public class AlertColorConverter : IValueConverter
{
    /// <summary>
    /// 當 IsVoltageAlert 為 true 時，回傳紅色；否則回傳預設顏色（白色或灰色）。
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isAlert && isAlert)
        {
            return Brushes.Red;
        }

        return Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// TB3-2: 狀態顏色轉換器 - 當值為 true 時顯示綠色（正常/成功狀態）
/// </summary>
public class StatusColorConverter : IValueConverter
{
    public static readonly StatusColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive && isActive)
        {
            return Brushes.LimeGreen;
        }

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Enabled/Disabled foreground converter for list items
/// </summary>
public class EnabledForegroundConverter : IValueConverter
{
    public static readonly EnabledForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool enabled && enabled)
            return Brushes.White;
        return Brushes.DimGray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// TB3-3: 大於零轉換器 - 用於控制元素可見性
/// </summary>
public class GreaterThanZeroConverter : IValueConverter
{
    public static readonly GreaterThanZeroConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int intValue => intValue > 0,
            double doubleValue => doubleValue > 0,
            float floatValue => floatValue > 0,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
