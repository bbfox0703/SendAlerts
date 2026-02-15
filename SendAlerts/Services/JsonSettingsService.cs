using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SendAlerts.Core.Interfaces;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// T3-2: JSON 檔案設定服務實作
/// </summary>
public class JsonSettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonSettingsService()
    {
        // 跨平台路徑: %AppData%/SendAlerts (Windows) 或 ~/.config/SendAlerts (Linux)
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "SendAlerts");

        // 確保目錄存在
        Directory.CreateDirectory(appDirectory);

        _settingsFilePath = Path.Combine(appDirectory, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // TA4-1: 列舉以字串形式序列化 (更易讀)
            Converters = { new JsonStringEnumConverter<AlertActionType>() }
        };
    }

    public string GetSettingsFilePath() => _settingsFilePath;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                Log.Information("設定檔不存在，建立預設設定: {Path}", _settingsFilePath);
                var defaultSettings = new AppSettings();

                // TA4-3: 首次啟動時初始化預設群組
                SettingsMigrator.InitializeDefaultGroups(defaultSettings);
                Save(defaultSettings);

                return defaultSettings;
            }

            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);

            if (settings == null)
            {
                Log.Warning("設定檔解析失敗，使用預設設定");
                return new AppSettings();
            }

            // Decrypt sensitive fields after loading
            CredentialProtector.UnprotectSensitiveFields(settings);

            // TA4-2: 自動遷移舊版設定
            if (SettingsMigrator.NeedsMigration(settings))
            {
                var migrated = SettingsMigrator.Migrate(settings);
                if (migrated)
                {
                    // 儲存遷移後的設定
                    Save(settings);
                    Log.Information("舊版設定已自動遷移並儲存");
                }
            }
            // TA4-3: 首次啟動時初始化預設群組
            else if (SettingsMigrator.NeedsDefaultGroups(settings))
            {
                var initialized = SettingsMigrator.InitializeDefaultGroups(settings);
                if (initialized)
                {
                    Save(settings);
                    Log.Information("預設群組已建立並儲存");
                }
            }

            Log.Information("已載入設定檔: {Path}", _settingsFilePath);
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "載入設定檔時發生錯誤，使用預設設定");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            // Encrypt sensitive fields before saving
            CredentialProtector.ProtectSensitiveFields(settings);

            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsFilePath, json);
            Log.Information("設定已儲存: {Path}", _settingsFilePath);

            // Decrypt back so in-memory settings remain usable
            CredentialProtector.UnprotectSensitiveFields(settings);
        }
        catch (Exception ex)
        {
            // Ensure settings are decrypted even on failure
            CredentialProtector.UnprotectSensitiveFields(settings);
            Log.Error(ex, "儲存設定檔時發生錯誤");
            throw;
        }
    }
}
