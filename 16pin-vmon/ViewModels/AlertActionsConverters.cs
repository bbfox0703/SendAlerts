using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using _16pin_vmon.Core.Interfaces;

namespace _16pin_vmon.ViewModels;

/// <summary>
/// TB1-1: ActionType 轉換為圖示
/// </summary>
public class ActionTypeIconConverter : IValueConverter
{
    public static readonly ActionTypeIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AlertActionType actionType)
        {
            return actionType switch
            {
                AlertActionType.CommandLine => "\u2318",    // ⌘
                AlertActionType.Telegram => "\u2708",       // ✈
                AlertActionType.LineNotify => "\u260E",     // ☎
                AlertActionType.Email => "\u2709",          // ✉
                AlertActionType.HttpWebhook => "\u21C4",    // ⇄
                AlertActionType.SystemShutdown => "\u23FB", // ⏻
                _ => "\u2022"                               // •
            };
        }
        return "\u2022";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// TB1-1: IsEnabled 轉換為背景顏色
/// </summary>
public class EnabledColorConverter : IValueConverter
{
    public static readonly EnabledColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
        {
            return isEnabled
                ? new SolidColorBrush(Color.FromRgb(46, 125, 50))   // Green
                : new SolidColorBrush(Color.FromRgb(120, 120, 120)); // Gray
        }
        return new SolidColorBrush(Color.FromRgb(120, 120, 120));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// TB1-1: IsEnabled 轉換為文字
/// </summary>
public class EnabledTextConverter : IValueConverter
{
    public static readonly EnabledTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
        {
            return isEnabled ? "ON" : "OFF";
        }
        return "OFF";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
