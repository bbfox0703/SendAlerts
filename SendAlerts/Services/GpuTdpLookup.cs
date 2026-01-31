using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Serilog;

namespace SendAlerts.Services;

/// <summary>
/// 從 gpu_tdp.json 查詢 GPU TDP，用於設定 Power 圖表 Y 軸上限。
/// 匹配規則：GPU 名稱必須包含 entry 中所有 Keywords 才算 match。
/// 排序規則：Keywords 數量多的優先（更精確的 match 優先）。
/// </summary>
public static class GpuTdpLookup
{
    private static readonly List<TdpEntry> _entries = [];

    static GpuTdpLookup()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("SendAlerts.Assets.gpu_tdp.json");
            if (stream is null)
            {
                // Fallback: try as Avalonia resource via file path
                var exeDir = AppContext.BaseDirectory;
                var filePath = Path.Combine(exeDir, "Assets", "gpu_tdp.json");
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    LoadFromJson(json);
                }
                else
                {
                    Log.Warning("[GpuTdpLookup] gpu_tdp.json not found");
                }
                return;
            }

            using var reader = new StreamReader(stream);
            LoadFromJson(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[GpuTdpLookup] Failed to load gpu_tdp.json");
        }
    }

    private static void LoadFromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<List<TdpEntry>>(json);
        if (entries is not null)
        {
            // Sort by keyword count descending (more specific matches first)
            entries.Sort((a, b) => b.Keywords.Count.CompareTo(a.Keywords.Count));
            _entries.AddRange(entries);
            Log.Information("[GpuTdpLookup] Loaded {Count} TDP entries", _entries.Count);
        }
    }

    /// <summary>
    /// 根據 GPU 名稱查詢 TDP (W)。
    /// 所有 Keywords 必須都出現在 gpuName 中才算 match。
    /// </summary>
    /// <returns>TDP in watts, or null if no match</returns>
    public static int? FindTdp(string gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return null;

        // Entries already sorted by keyword count desc (most specific first)
        foreach (var entry in _entries)
        {
            var allMatch = true;
            foreach (var keyword in entry.Keywords)
            {
                if (gpuName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    allMatch = false;
                    break;
                }
            }
            if (allMatch)
            {
                Log.Information("[GpuTdpLookup] Matched '{GpuName}' → {TDP}W (keywords: {Keywords})",
                    gpuName, entry.TDP, string.Join(", ", entry.Keywords));
                return entry.TDP;
            }
        }

        return null;
    }

    /// <summary>
    /// 計算 Power 圖表 Y 軸上限：TDP * 1.2，取整到 50 的倍數。
    /// </summary>
    public static double GetChartYMax(string gpuName, float currentPowerLimit)
    {
        var tdp = FindTdp(gpuName);
        if (tdp.HasValue)
        {
            var raw = tdp.Value * 1.2;
            return Math.Ceiling(raw / 50) * 50; // 進位到 50 的倍數
        }

        // Fallback: use provider's power limit * 1.5
        if (currentPowerLimit > 0)
        {
            var raw = currentPowerLimit * 1.5;
            return Math.Ceiling(raw / 50) * 50;
        }

        return 600; // ultimate fallback
    }

    private class TdpEntry
    {
        public List<string> Keywords { get; set; } = [];
        public int TDP { get; set; }
    }
}
