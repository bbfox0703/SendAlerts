using System;
using _16pin_vmon.Core.Interfaces;
using _16pin_vmon.Models;
using Serilog;

namespace _16pin_vmon.Services;

/// <summary>
/// TA2-3: 警報動作工廠 - 根據 AlertActionConfig 建立對應的 IAlertAction 實例
/// </summary>
public static class AlertActionFactory
{
    /// <summary>
    /// 根據設定建立警報動作實例
    /// </summary>
    /// <param name="config">警報動作設定</param>
    /// <returns>IAlertAction 實例，若類型不支援或設定無效則回傳 null</returns>
    public static IAlertAction? Create(AlertActionConfig config)
    {
        if (config == null)
        {
            Log.Warning("[AlertActionFactory] 設定為 null，無法建立動作");
            return null;
        }

        // 先驗證設定
        var validation = config.Validate();
        if (!validation.IsValid)
        {
            Log.Warning("[AlertActionFactory] 設定驗證失敗: {InstanceId} - {Error}",
                config.InstanceId, validation.ErrorMessage);
            return null;
        }

        try
        {
            return config.ActionType switch
            {
                AlertActionType.CommandLine => CreateCommandLine(config),
                AlertActionType.Telegram => CreateTelegram(config),
                AlertActionType.LineNotify => CreateLineNotify(config),
                AlertActionType.Email => CreateEmail(config),
                AlertActionType.HttpWebhook => CreateHttpWebhook(config),
                AlertActionType.SystemShutdown => CreateSystemShutdown(config),
                _ => HandleUnsupportedType(config.ActionType)
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[AlertActionFactory] 建立動作實例時發生錯誤: {InstanceId}", config.InstanceId);
            return null;
        }
    }

    /// <summary>
    /// 批次建立多個警報動作實例
    /// </summary>
    /// <param name="configs">警報動作設定清單</param>
    /// <returns>成功建立的 IAlertAction 實例清單</returns>
    public static IAlertAction[] CreateMany(params AlertActionConfig[] configs)
    {
        var actions = new System.Collections.Generic.List<IAlertAction>();

        foreach (var config in configs)
        {
            var action = Create(config);
            if (action != null)
            {
                actions.Add(action);
            }
        }

        Log.Information("[AlertActionFactory] 批次建立完成: {Created}/{Total} 個動作",
            actions.Count, configs.Length);

        return actions.ToArray();
    }

    #region 各類型建立方法

    private static CommandLineAlertAction CreateCommandLine(AlertActionConfig config)
    {
        var action = new CommandLineAlertAction(
            config.InstanceId,
            config.Command ?? string.Empty,
            config.CooldownSeconds,
            config.DebugMode)
        {
            IsEnabled = config.IsEnabled
        };

        Log.Debug("[AlertActionFactory] 建立 CommandLine 動作: {InstanceId}", config.InstanceId);
        return action;
    }

    private static TelegramAlertAction CreateTelegram(AlertActionConfig config)
    {
        var action = new TelegramAlertAction(
            config.InstanceId,
            config.TelegramBotToken ?? string.Empty,
            config.TelegramChatId ?? string.Empty,
            config.CooldownSeconds,
            config.DebugMode)
        {
            IsEnabled = config.IsEnabled
        };

        Log.Debug("[AlertActionFactory] 建立 Telegram 動作: {InstanceId}", config.InstanceId);
        return action;
    }

    private static LineNotifyAlertAction CreateLineNotify(AlertActionConfig config)
    {
        var action = new LineNotifyAlertAction(
            config.InstanceId,
            config.LineNotifyAccessToken ?? string.Empty,
            config.CooldownSeconds,
            config.DebugMode)
        {
            IsEnabled = config.IsEnabled
        };

        Log.Debug("[AlertActionFactory] 建立 LineNotify 動作: {InstanceId}", config.InstanceId);
        return action;
    }

    /// <summary>
    /// TA2-6: Email 動作 (尚未實作)
    /// </summary>
    private static IAlertAction? CreateEmail(AlertActionConfig config)
    {
        // TODO: TA2-6 實作 EmailAlertAction
        Log.Warning("[AlertActionFactory] Email 動作尚未實作: {InstanceId}", config.InstanceId);
        return null;
    }

    /// <summary>
    /// TA2-7: HttpWebhook 動作 (尚未實作)
    /// </summary>
    private static IAlertAction? CreateHttpWebhook(AlertActionConfig config)
    {
        // TODO: TA2-7 實作 HttpWebhookAlertAction
        Log.Warning("[AlertActionFactory] HttpWebhook 動作尚未實作: {InstanceId}", config.InstanceId);
        return null;
    }

    /// <summary>
    /// TA2-8: SystemShutdown 動作 (尚未實作)
    /// </summary>
    private static IAlertAction? CreateSystemShutdown(AlertActionConfig config)
    {
        // TODO: TA2-8 實作 SystemShutdownAction
        Log.Warning("[AlertActionFactory] SystemShutdown 動作尚未實作: {InstanceId}", config.InstanceId);
        return null;
    }

    #endregion

    #region 輔助方法

    private static IAlertAction? HandleUnsupportedType(AlertActionType actionType)
    {
        Log.Warning("[AlertActionFactory] 不支援的動作類型: {ActionType}", actionType);
        return null;
    }

    /// <summary>
    /// 檢查指定的 ActionType 是否已實作
    /// </summary>
    public static bool IsImplemented(AlertActionType actionType)
    {
        return actionType switch
        {
            AlertActionType.CommandLine => true,
            AlertActionType.Telegram => true,
            AlertActionType.LineNotify => true,
            AlertActionType.Email => false,      // TA2-6
            AlertActionType.HttpWebhook => false, // TA2-7
            AlertActionType.SystemShutdown => false, // TA2-8
            _ => false
        };
    }

    /// <summary>
    /// 取得所有已實作的 ActionType
    /// </summary>
    public static AlertActionType[] GetImplementedTypes()
    {
        return new[]
        {
            AlertActionType.CommandLine,
            AlertActionType.Telegram,
            AlertActionType.LineNotify
        };
    }

    /// <summary>
    /// 取得 ActionType 的顯示名稱
    /// </summary>
    public static string GetDisplayName(AlertActionType actionType)
    {
        return actionType switch
        {
            AlertActionType.CommandLine => "命令列",
            AlertActionType.Telegram => "Telegram",
            AlertActionType.LineNotify => "LINE Notify",
            AlertActionType.Email => "電子郵件",
            AlertActionType.HttpWebhook => "HTTP Webhook",
            AlertActionType.SystemShutdown => "系統關機",
            _ => actionType.ToString()
        };
    }

    #endregion
}
