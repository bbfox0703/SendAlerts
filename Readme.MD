# 16pin-vmon

RTX 50 系列 GPU 16-pin (12VHPWR) 電壓監控工具

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

16pin-vmon 是一款專為 NVIDIA RTX 50 系列 (Blackwell) 設計的 GPU 監控工具，主要監測 16-pin 電源連接器的電壓穩定性，在電壓異常時即時發出警報，協助預防因電壓不穩導致的硬體損壞。

### 核心功能

- **即時電壓監測** - 透過 NVML API 每秒讀取 16-pin 電壓軌數據
- **溫度監控** - 同步監測 GPU 核心溫度
- **智慧警報系統** - 滑動視窗演算法，避免誤報 (X 秒內超標 Y 次才觸發)
- **視覺化圖表** - LiveCharts2 即時折線圖，支援 15 分鐘數據回溯
- **跨平台支援** - Windows / Linux (透過 Avalonia UI)

### 硬體相容性

本工具採用三段式 Field ID 偵測機制，自動適配不同 AIB 廠商 (MSI、ASUS、EVGA 等) 的 RTX 50 系列顯示卡：

1. 查詢內建硬體資料庫 (`gpu_mapping.json`)
2. 動態掃描 NVML Field ID 150-165
3. Fallback 至功耗估算模式

## 安裝與執行

### 系統需求

- Windows 10/11 或 Linux (支援 NVML)
- NVIDIA 驅動程式 (含 nvml.dll / libnvidia-ml.so)
- .NET 10 Runtime

### 從原始碼建置

```bash
git clone https://github.com/bbfox0703/16pin-vmon.git
cd 16pin-vmon
dotnet build
dotnet run --project 16pin-vmon.Desktop
```

## 使用說明

1. 啟動程式後會自動偵測 NVIDIA GPU
2. 主畫面顯示即時電壓與溫度圖表
3. 當電壓低於 11.8V 或溫度高於 88°C 時觸發警報
4. 警報門檻可於設定中自訂

### 警報預設值

| 監測項目 | 觸發條件 | 預設門檻 |
|---------|---------|---------|
| 16-pin 電壓 | 低於設定值 | 11.8V |
| GPU 溫度 | 高於設定值 | 88°C |
| 判定機制 | 滑動視窗 | 3 秒內 2 次 |

## 技術棧

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
