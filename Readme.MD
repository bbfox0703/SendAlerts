# SendAlerts

硬體警報中繼站 (Hardware Alert Relay Station)

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-8B5CF6)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> ## ⚠️ 法律免責聲明 (Legal Disclaimer)
>
> **本軟體係依「現狀」(AS IS) 提供，開發者不負任何形式的擔保責任。**
> 使用者應了解，本工具涉及對顯卡底層硬體 (NVML) 的存取與監測。此為非必要功能，在任何情況下，開發者對於因使用或無法使用本軟體而產生的任何直接、間接、附帶、特別、懲罰性或衍生性損害（包括但不限於：硬體損壞、數據損失、系統損壞或營收損失）均不負任何賠償責任。
> **THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY.**

---

## 簡介

SendAlerts 是一款「硬體警報中繼站」，接收來自外部工具（如 HWiNFO64）的警報觸發指令，並透過多種管道（Discord、Telegram、命令列等）發送通知。

主畫面顯示 GPU/CPU 即時監控資訊（僅供參考），**警報功能由外部工具透過 Named Pipe 或 HTTP API 觸發**。

### 核心功能

- **Alert Center** - 接收 Named Pipe / HTTP API 指令，執行多管道警報
- **多管道通知** - 支援 Discord Webhook、Telegram Bot、命令列執行
- **群組管理** - 將多個 Action 組合為 Group，一次觸發多個通知
- **即時監控顯示** - GPU/CPU 使用率、溫度、功耗即時圖表（僅顯示，不主動警報）
- **HTTP API** - 支援遠端機器透過 HTTP 發送警報
- **CLI 工具** - 提供命令列工具發送警報
- **系統匣** - 最小化至系統匣背景執行
- **多語系** - 支援英文、繁體中文、日文介面

### 架構概念

```
外部工具 (HWiNFO64, 自訂腳本等)
        │
        ▼ Named Pipe / HTTP API
┌───────────────────────────────┐
│      SendAlerts Desktop       │
│  ┌─────────────────────────┐  │
│  │     Alert Center        │  │
│  │  ┌─────────────────────┐│  │
│  │  │ Group: "Critical"   ││  │
│  │  │  → Discord_Admin    ││  │
│  │  │  → Telegram_All     ││  │
│  │  └─────────────────────┘│  │
│  └─────────────────────────┘  │
└───────────────────────────────┘
```

## 安裝與執行

### 系統需求

- Windows 10/11 (Linux 需自行建置)
- .NET 10 Runtime
- (選用) NVIDIA 驅動程式 - 用於 GPU 監控顯示

### 從原始碼建置

```bash
git clone https://github.com/user/SendAlerts.git
cd SendAlerts
dotnet build
dotnet run --project SendAlerts.Desktop
```

### 執行檔位置

建置後，執行檔位於：
```
SendAlerts.Desktop/bin/Debug/net10.0-windows/
├── SendAlerts.Desktop.exe   # 主程式
└── SendAlerts-cli.exe       # CLI 工具
```

## 使用說明

### 基本設定

1. 啟動 `SendAlerts.Desktop.exe`
2. 點擊 **Alert Actions** 設定通知管道 (Discord Webhook, Telegram 等)
3. 點擊 **Alert Groups** 建立群組，選擇要包含的 Actions
4. 關閉視窗後程式會縮小至系統匣背景執行

### 觸發警報

#### 方式一：CLI 工具
```bash
SendAlerts-cli send -g Critical -m "GPU 溫度過高！"
```

#### 方式二：Named Pipe (PowerShell)
```powershell
'{"GroupName":"Critical","CustomMessage":"警報訊息"}' | Out-File -FilePath \\.\pipe\sendalerts-pipe -Encoding utf8
```

#### 方式三：HTTP API
```bash
curl -X POST http://localhost:58080/api/send \
  -H "X-API-Key: YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"groupName":"Critical","message":"警報訊息"}'
```

### HWiNFO64 整合

詳細設定請參閱 [`docs/HWiNFO-Setup.md`](docs/HWiNFO-Setup.md)

## 多語系支援

| 語系 | 代碼 | 自動偵測 |
|------|------|----------|
| English | en | 預設 |
| 繁體中文 | zh-TW | OS 語系 zh-TW, zh-Hant |
| 日本語 | ja | OS 語系 ja-* |

可在 Settings 手動切換語系。

## 技術堆疊

- **Framework**: .NET 10 / C#
- **UI**: Avalonia UI (MVVM + CommunityToolkit.Mvvm)
- **Hardware API**: NVIDIA NVML / NvAPI
- **Charts**: LiveCharts2 (SkiaSharp)
- **Logging**: Serilog
- **HTTP Server**: System.Net.HttpListener

## 開發文件

- 詳細規格請參閱 [`docs/Spec.md`](docs/Spec.md)
- 開發指南請參閱 [`CLAUDE.md`](CLAUDE.md)
- HWiNFO64 整合請參閱 [`docs/HWiNFO-Setup.md`](docs/HWiNFO-Setup.md)

## 授權

MIT License
