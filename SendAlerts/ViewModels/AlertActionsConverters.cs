using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;

namespace SendAlerts.ViewModels;

/// <summary>
/// TB1-2: ActionType 可見性轉換器（用於根據選擇的類型顯示不同的設定面板）
/// </summary>
public static class ActionTypeVisibilityConverter
{
    public static readonly ActionTypeMatchConverter CommandLine = new(AlertActionType.CommandLine);
    public static readonly ActionTypeMatchConverter Telegram = new(AlertActionType.Telegram);
    public static readonly ActionTypeMatchConverter Discord = new(AlertActionType.Discord);
    public static readonly ActionTypeMatchConverter Email = new(AlertActionType.Email);
    public static readonly ActionTypeMatchConverter HttpWebhook = new(AlertActionType.HttpWebhook);
    public static readonly ActionTypeMatchConverter SystemShutdown = new(AlertActionType.SystemShutdown);
}

/// <summary>
/// 檢查 ActionType 是否匹配
/// </summary>
public class ActionTypeMatchConverter : IValueConverter
{
    private readonly AlertActionType _targetType;

    public ActionTypeMatchConverter(AlertActionType targetType)
    {
        _targetType = targetType;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AlertActionType actionType)
        {
            return actionType == _targetType;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

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
                AlertActionType.Discord => "\uD83D\uDCAC",  // 💬
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

    private static readonly SolidColorBrush EnabledBrush = new(Color.Parse(ChartColors.EnabledGreen));
    private static readonly SolidColorBrush DisabledBrush = new(Color.Parse(ChartColors.DisabledGray));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled && isEnabled)
            return EnabledBrush;
        return DisabledBrush;
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
