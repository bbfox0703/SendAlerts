using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// T2-1: 硬體資料庫管理器 - 管理 GPU 映射資料
/// </summary>
public class HardwareDbManager
{
    private readonly string _dbFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private GpuMappingDatabase _database;

    public HardwareDbManager()
    {
        // 資料庫檔案路徑 (與設定檔同目錄)
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "SendAlerts");
        Directory.CreateDirectory(appDirectory);

        _dbFilePath = Path.Combine(appDirectory, "gpu_mapping.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _database = LoadDatabase();
    }

    /// <summary>
    /// 根據 Subsystem ID 查詢已驗證的 Field ID
    /// </summary>
    /// <param name="subsystemId">格式: "VendorId:DeviceId" (例如 "1462:5170")</param>
    /// <returns>已驗證的 Field ID，若無則回傳 null</returns>
    public uint? GetVerifiedFieldId(string subsystemId)
    {
        var mapping = _database.Mappings
            .FirstOrDefault(m => m.SubsystemId.Equals(subsystemId, StringComparison.OrdinalIgnoreCase));

        if (mapping != null)
        {
            Log.Information(
                "[HardwareDB] 找到已驗證映射: {Model} (SSID: {SubsystemId}) -> Field ID {FieldId}",
                mapping.Model,
                mapping.SubsystemId,
                mapping.FieldId);
            return mapping.FieldId;
        }

        Log.Information("[HardwareDB] 未找到 Subsystem ID {SubsystemId} 的映射", subsystemId);
        return null;
    }

    /// <summary>
    /// 新增或更新 GPU 映射
    /// </summary>
    public void AddOrUpdateMapping(GpuMapping mapping)
    {
        var existing = _database.Mappings
            .FirstOrDefault(m => m.SubsystemId.Equals(mapping.SubsystemId, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            _database.Mappings.Remove(existing);
        }

        mapping.LastUpdated = DateTime.Now;
        _database.Mappings.Add(mapping);
        SaveDatabase();

        Log.Information(
            "[HardwareDB] 已更新映射: {Model} (SSID: {SubsystemId}) -> Field ID {FieldId}",
            mapping.Model,
            mapping.SubsystemId,
            mapping.FieldId);
    }

    /// <summary>
    /// 取得所有映射
    /// </summary>
    public IReadOnlyList<GpuMapping> GetAllMappings() => _database.Mappings.AsReadOnly();

    /// <summary>
    /// 匯出資料庫為 JSON 字串 (用於使用者回報)
    /// </summary>
    public string ExportToJson()
    {
        return JsonSerializer.Serialize(_database, _jsonOptions);
    }

    private GpuMappingDatabase LoadDatabase()
    {
        try
        {
            if (File.Exists(_dbFilePath))
            {
                var json = File.ReadAllText(_dbFilePath);
                var db = JsonSerializer.Deserialize<GpuMappingDatabase>(json, _jsonOptions);
                if (db != null)
                {
                    Log.Information("[HardwareDB] 已載入 {Count} 筆 GPU 映射", db.Mappings.Count);
                    return db;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[HardwareDB] 載入資料庫失敗，使用內建預設值");
        }

        // 回傳內建預設資料庫
        return CreateDefaultDatabase();
    }

    private void SaveDatabase()
    {
        try
        {
            var json = JsonSerializer.Serialize(_database, _jsonOptions);
            File.WriteAllText(_dbFilePath, json);
            Log.Information("[HardwareDB] 資料庫已儲存: {Path}", _dbFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[HardwareDB] 儲存資料庫失敗");
        }
    }

    /// <summary>
    /// T2-2: 建立內建預設資料庫 (已知的 RTX 50 系列映射)
    /// </summary>
    private GpuMappingDatabase CreateDefaultDatabase()
    {
        var db = new GpuMappingDatabase
        {
            Version = "1.0",
            Description = "SendAlerts GPU Hardware Mapping Database",
            Mappings = new List<GpuMapping>
            {
                // RTX 5090 系列 (範例資料，需要實際驗證)
                new GpuMapping
                {
                    SubsystemId = "1462:5170",
                    FieldId = 156,
                    Model = "MSI RTX 5090 SUPRIM X",
                    Vendor = "MSI",
                    Notes = "預設映射，待驗證",
                    LastUpdated = DateTime.Now
                },
                new GpuMapping
                {
                    SubsystemId = "1043:8930",
                    FieldId = 156,
                    Model = "ASUS RTX 5090 ROG STRIX",
                    Vendor = "ASUS",
                    Notes = "預設映射，待驗證",
                    LastUpdated = DateTime.Now
                },
                new GpuMapping
                {
                    SubsystemId = "3842:5093",
                    FieldId = 156,
                    Model = "EVGA RTX 5090 FTW3",
                    Vendor = "EVGA",
                    Notes = "預設映射，待驗證",
                    LastUpdated = DateTime.Now
                },
                // RTX 5080 系列
                new GpuMapping
                {
                    SubsystemId = "1462:5080",
                    FieldId = 156,
                    Model = "MSI RTX 5080 GAMING X",
                    Vendor = "MSI",
                    Notes = "預設映射，待驗證",
                    LastUpdated = DateTime.Now
                }
            }
        };

        // 儲存預設資料庫
        try
        {
            var json = JsonSerializer.Serialize(db, _jsonOptions);
            File.WriteAllText(_dbFilePath, json);
            Log.Information("[HardwareDB] 已建立預設資料庫: {Path}", _dbFilePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[HardwareDB] 無法儲存預設資料庫");
        }

        return db;
    }
}

/// <summary>
/// GPU 映射資料庫結構
/// </summary>
public class GpuMappingDatabase
{
    public string Version { get; set; } = "1.0";
    public string Description { get; set; } = "";
    public List<GpuMapping> Mappings { get; set; } = new();
}

/// <summary>
/// T2-2: GPU 映射資料結構
/// </summary>
public class GpuMapping
{
    /// <summary>
    /// Subsystem ID (格式: "VendorId:DeviceId", 例如 "1462:5170")
    /// </summary>
    public string SubsystemId { get; set; } = "";

    /// <summary>
    /// 已驗證的 NVML Field ID
    /// </summary>
    public uint FieldId { get; set; }

    /// <summary>
    /// GPU 型號名稱
    /// </summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// AIB 廠商名稱
    /// </summary>
    public string Vendor { get; set; } = "";

    /// <summary>
    /// 備註
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// 最後更新時間
    /// </summary>
    public DateTime LastUpdated { get; set; }
}
