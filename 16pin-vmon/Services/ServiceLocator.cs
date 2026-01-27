using _16pin_vmon.Core.Interfaces;

namespace _16pin_vmon.Services;

/// <summary>
/// T1-1: 簡易服務定位器，用於跨專案依賴注入
/// 在應用程式啟動時由 Platform Head (Desktop) 設定具體實作
/// </summary>
public static class ServiceLocator
{
    /// <summary>
    /// GPU 資料提供者（由 Desktop 專案根據平台設定）
    /// </summary>
    public static IGpuProvider? GpuProvider { get; set; }

    /// <summary>
    /// 設定服務
    /// </summary>
    public static ISettingsService? SettingsService { get; set; }

    /// <summary>
    /// TA3-2: 警報服務 (Alert Center 核心)
    /// </summary>
    public static AlertService? AlertService { get; set; }
}
