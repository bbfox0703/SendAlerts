using System.Collections.Generic;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// TA4-2: 設定檔版本遷移器
/// 負責將舊版設定自動轉換為新版 Alert Center 格式
/// </summary>
public static class SettingsMigrator
{
    /// <summary>
    /// 目前最新的設定版本
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// 執行設定遷移
    /// </summary>
    /// <param name="settings">要遷移的設定</param>
    /// <returns>是否有進行遷移</returns>
    public static bool Migrate(AppSettings settings)
    {
        if (settings == null) return false;

        var originalVersion = settings.SettingsVersion;
        var migrated = false;

        // 版本 1 → 2: 舊版 T4 設定遷移至 Alert Center
        if (settings.SettingsVersion < 2)
        {
            migrated = MigrateV1ToV2(settings);
            if (migrated)
            {
                settings.SettingsVersion = 2;
            }
        }

        if (migrated)
        {
            Log.Information("[SettingsMigrator] 設定已從 v{OldVersion} 遷移至 v{NewVersion}",
                originalVersion, settings.SettingsVersion);
        }

        return migrated;
    }

    /// <summary>
    /// 版本 1 → 2: 將舊版 T4 警報設定遷移至新版 Alert Center
    /// </summary>
    private static bool MigrateV1ToV2(AppSettings settings)
    {
        var hasLegacySettings = HasLegacyAlertSettings(settings);

        if (!hasLegacySettings)
        {
            Log.Debug("[SettingsMigrator] 無舊版警報設定需要遷移");
            return false;
        }

        Log.Information("[SettingsMigrator] 開始遷移舊版 T4 警報設定...");

        var migratedActions = new List<AlertActionConfig>();
        var defaultGroupActionIds = new List<string>();

        // 遷移 CommandLine 設定
        if (settings.CommandLineAlertEnabled && !string.IsNullOrWhiteSpace(settings.CommandLineAlertCommand))
        {
            var config = new AlertActionConfig
            {
                InstanceId = "CommandLine_Legacy",
                ActionType = AlertActionType.CommandLine,
                IsEnabled = true,
                Command = settings.CommandLineAlertCommand,
                CooldownSeconds = settings.CommandLineAlertCooldownSeconds,
                DebugMode = settings.AlertActionsDebugMode
            };
            migratedActions.Add(config);
            defaultGroupActionIds.Add(config.InstanceId);
            Log.Debug("[SettingsMigrator] 已遷移 CommandLine 設定: {InstanceId}", config.InstanceId);
        }

        // 遷移 Telegram 設定
        if (settings.TelegramAlertEnabled &&
            !string.IsNullOrWhiteSpace(settings.TelegramBotToken) &&
            !string.IsNullOrWhiteSpace(settings.TelegramChatId))
        {
            var config = new AlertActionConfig
            {
                InstanceId = "Telegram_Legacy",
                ActionType = AlertActionType.Telegram,
                IsEnabled = true,
                TelegramBotToken = settings.TelegramBotToken,
                TelegramChatId = settings.TelegramChatId,
                CooldownSeconds = AppConstants.DefaultCooldownSeconds,
                DebugMode = settings.AlertActionsDebugMode
            };
            migratedActions.Add(config);
            defaultGroupActionIds.Add(config.InstanceId);
            Log.Debug("[SettingsMigrator] 已遷移 Telegram 設定: {InstanceId}", config.InstanceId);
        }

        // LINE Notify 已停用，不再遷移
        if (settings.LineNotifyAlertEnabled &&
            !string.IsNullOrWhiteSpace(settings.LineNotifyAccessToken))
        {
            Log.Warning("[SettingsMigrator] 偵測到舊版 LINE Notify 設定，但 LINE Notify 服務已於 2025 年停止。請改用 Discord Webhook。");
            // 停用舊設定
            settings.LineNotifyAlertEnabled = false;
        }

        // 如果有遷移的動作，建立預設群組
        if (migratedActions.Count > 0)
        {
            // 合併現有的 AlertActions（避免重複）
            foreach (var action in migratedActions)
            {
                if (!settings.AlertActions.Exists(a => a.InstanceId == action.InstanceId))
                {
                    settings.AlertActions.Add(action);
                }
            }

            // 建立或更新 Default 群組
            var defaultGroup = settings.AlertGroups.Find(g => g.Name == "Default");
            if (defaultGroup == null)
            {
                defaultGroup = new AlertGroup
                {
                    Name = "Default",
                    Description = "從舊版設定遷移的預設群組",
                    IsEnabled = true,
                    MessageTemplate = "[Alert] {message}\n時間: {timestamp}",
                    ActionInstanceIds = new List<string>(defaultGroupActionIds)
                };
                settings.AlertGroups.Add(defaultGroup);
            }
            else
            {
                // 合併動作到現有群組
                foreach (var actionId in defaultGroupActionIds)
                {
                    if (!defaultGroup.ActionInstanceIds.Contains(actionId))
                    {
                        defaultGroup.ActionInstanceIds.Add(actionId);
                    }
                }
            }

            // 建立 Critical 群組（也使用相同的動作）
            var criticalGroup = settings.AlertGroups.Find(g => g.Name == "Critical");
            if (criticalGroup == null)
            {
                criticalGroup = new AlertGroup
                {
                    Name = "Critical",
                    Description = "緊急警報群組",
                    IsEnabled = true,
                    MessageTemplate = "🚨 [CRITICAL] {message}\n時間: {timestamp}",
                    ActionInstanceIds = new List<string>(defaultGroupActionIds)
                };
                settings.AlertGroups.Add(criticalGroup);
            }

            // 啟用 Alert Center 模式
            settings.UseAlertCenterMode = true;

            Log.Information("[SettingsMigrator] 遷移完成: {ActionCount} 個動作, {GroupCount} 個群組",
                migratedActions.Count, settings.AlertGroups.Count);

            return true;
        }

        return false;
    }

    /// <summary>
    /// 檢查是否有舊版警報設定
    /// </summary>
    private static bool HasLegacyAlertSettings(AppSettings settings)
    {
        // 檢查是否有任何舊版 T4 警報設定啟用 (LINE Notify 已停用，不納入判斷)
        return (settings.CommandLineAlertEnabled && !string.IsNullOrWhiteSpace(settings.CommandLineAlertCommand)) ||
               (settings.TelegramAlertEnabled && !string.IsNullOrWhiteSpace(settings.TelegramBotToken));
    }

    /// <summary>
    /// 檢查設定是否需要遷移
    /// </summary>
    public static bool NeedsMigration(AppSettings settings)
    {
        return settings != null && settings.SettingsVersion < CurrentVersion && HasLegacyAlertSettings(settings);
    }

    /// <summary>
    /// TA4-3: 檢查是否需要初始化預設群組
    /// </summary>
    public static bool NeedsDefaultGroups(AppSettings settings)
    {
        return settings != null &&
               settings.AlertGroups.Count == 0 &&
               !HasLegacyAlertSettings(settings);
    }

    /// <summary>
    /// TA4-3: 初始化預設群組（首次啟動時）
    /// </summary>
    /// <param name="settings">要初始化的設定</param>
    /// <returns>是否有進行初始化</returns>
    public static bool InitializeDefaultGroups(AppSettings settings)
    {
        if (settings == null) return false;

        // 如果已有群組或有舊版設定，不初始化
        if (settings.AlertGroups.Count > 0 || HasLegacyAlertSettings(settings))
        {
            return false;
        }

        Log.Information("[SettingsMigrator] 首次啟動，建立預設群組...");

        // 建立預設群組（不含 Actions，等使用者自行設定）
        settings.AlertGroups.AddRange(new[]
        {
            new AlertGroup
            {
                Name = "Default",
                Description = "預設警報群組",
                IsEnabled = true,
                MessageTemplate = "[Alert] {message}\n時間: {timestamp}",
                ActionInstanceIds = new List<string>()
            },
            new AlertGroup
            {
                Name = "Critical",
                Description = "緊急警報 - 高優先級",
                IsEnabled = true,
                MessageTemplate = "🚨 [CRITICAL] {message}\n群組: {group_name}\n時間: {timestamp}",
                ActionInstanceIds = new List<string>()
            },
            new AlertGroup
            {
                Name = "Warning",
                Description = "警告通知 - 中優先級",
                IsEnabled = true,
                MessageTemplate = "⚠️ [WARNING] {message}\n時間: {timestamp}",
                ActionInstanceIds = new List<string>()
            },
            new AlertGroup
            {
                Name = "Info",
                Description = "一般資訊通知",
                IsEnabled = true,
                MessageTemplate = "ℹ️ {message}",
                ActionInstanceIds = new List<string>()
            }
        });

        // 啟用 Alert Center 模式
        settings.UseAlertCenterMode = true;
        settings.SettingsVersion = CurrentVersion;

        Log.Information("[SettingsMigrator] 已建立 {Count} 個預設群組", settings.AlertGroups.Count);

        return true;
    }
}
