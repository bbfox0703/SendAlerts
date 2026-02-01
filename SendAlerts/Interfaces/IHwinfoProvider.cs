using System;
using System.Collections.Generic;
using SendAlerts.Models;

namespace SendAlerts.Core.Interfaces;

/// <summary>
/// HWiNFO64 Shared Memory 資料提供者介面
/// </summary>
public interface IHwinfoProvider : IDisposable
{
    /// <summary>
    /// Shared Memory 是否可讀取
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 取得所有感測器群組 (可選擇性篩選名稱)
    /// </summary>
    /// <param name="nameFilters">若提供，僅回傳名稱包含任一關鍵字的群組 (不分大小寫)</param>
    List<HwinfoSensorGroup> GetSensorGroups(params string[] nameFilters);

    /// <summary>
    /// 讀取特定感測器項目的當前值
    /// </summary>
    /// <param name="sensorName">感測器群組名稱</param>
    /// <param name="labelOrig">項目原始名稱</param>
    /// <returns>讀取成功回傳項目資料，失敗回傳 null</returns>
    HwinfoSensorEntry? ReadEntry(string sensorName, string labelOrig);
}
