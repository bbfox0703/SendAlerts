# SendAlerts

警示傳送代理程式

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-8B5CF6)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> ## ⚠️ 法律免責聲明 (Legal Disclaimer)
> 
> 
> **本軟體係依「現狀」(AS IS) 提供，開發者不負任何形式的擔保責任。**
> 使用者應了解，本工具涉及對顯卡底層硬體 (NVML) 的存取與監測。此為非必要功能，在任何情況下，開發者對於因使用或無法使用本軟體而產生的任何直接、間接、附帶、特別、懲罰性或衍生性損害（包括但不限於：硬體熔毀、數據損失、系統損壞或營收損失）均不負任何賠償責任。
> **THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY.**

---

## 簡介

SendAlerts 是一款設計用來做為警示傳送的代理人工具，使用者只要設定好傳送動作及群組，接著只要使用其提供的命令列程式，就能簡單的送簡訊到 Discord 或是 Telegram，主畫面會顯示一些簡單的 GPU 監控資訊，但這些監控並不帶有警示功能。

### 核心功能

- **即時資訊走勢圖** - 透過 NVML API 每秒讀取相關資訊。
- **視覺化圖表** - LiveCharts2 即時折線圖，支援 15 分鐘數據回溯
- **跨平台支援** - 具跨 Windows / Linux 能力。目前只在 Windows 測試。Linux 需自行建置。

## 安裝與執行

### 系統需求

- Windows 10/11 或 Linux (支援 NVML)
- NVIDIA 驅動程式 (含 nvml.dll / libnvidia-ml.so)
- .NET 10 Runtime

### 從原始碼建置

```bash
git clone https://github.com/bbfox0703/SendAlerts.git
cd SendAlerts
dotnet build
dotnet run --project SendAlerts.Desktop
```

## 使用說明

1. 啟動程式後會自動偵測 NVIDIA GPU
2. 主畫面顯示即時相關圖表。如果沒有 NVIDIA GPU，圖表會顯示其它資訊，
3. 隨時接收警示命令列指令來的觸發警報
4. 警報門檻可於設定中自訂
5. 支援 Discord 與 Telegram 傳送
6. 設定完成後，關閉主程式視窗，程式會繼續在背景執行。
7. 支援網路傳送 (HTTP API)

## 技術堆疊

- **Framework**: .NET 10 / C#
- **UI**: Avalonia UI (MVVM)
- **Hardware API**: NVIDIA NVML v2
- **Charts**: LiveCharts2 (SkiaSharp)
- **Logging**: Serilog

## 開發文件

- 詳細規格請參閱 [`docs/Spec.md`](docs/Spec.md)
- 開發指南請參閱 [`CLAUDE.md`](CLAUDE.md)

## 授權

MIT License