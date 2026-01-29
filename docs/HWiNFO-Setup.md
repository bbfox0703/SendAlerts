# HWiNFO64 Integration Guide

本文件說明如何將 HWiNFO64 與 SendAlerts Alert Center 整合，實現硬體警報自動觸發。

## 概述

SendAlerts 作為「硬體警報中繼站」，透過 Named Pipe 接收外部工具（如 HWiNFO64）的警報觸發訊息，並執行預設的警報動作（Telegram、Discord 等）。

```
HWiNFO64 (監控硬體)
    │
    ▼ Named Pipe: \\.\pipe\sendalerts-pipe
    │
SendAlerts Alert Center
    │
    ▼ 執行警報動作
[Telegram] [LINE] [Email] [Webhook] ...
```

## 前置需求

1. **SendAlerts** 已安裝並運行
2. **HWiNFO64** v7.0 或更高版本（支援 Actions 功能）
3. 已在 SendAlerts 中設定至少一個 Alert Group

## HWiNFO64 設定步驟

### 步驟 1：啟用 Alerts 功能

1. 開啟 HWiNFO64
2. 點選 **Settings** → **Alerts**
3. 勾選 **Enable Alerting System**

### 步驟 2：新增感測器警報

1. 在主視窗找到要監控的感測器（例如：GPU Temperature）
2. 右鍵點選該感測器 → **Add to Alerts**
3. 設定觸發條件：
   - **Threshold**: 設定門檻值（例如：85°C）
   - **Condition**: 選擇觸發條件（Above / Below）
   - **Duration**: 設定持續時間（建議 3-5 秒避免誤觸）

### 步驟 3：設定 Action (執行程式)

在 Alert 設定中：

1. **Action Type**: 選擇 **Run Program**
2. **Program**: `powershell.exe`
3. **Arguments** (複製以下內容):

```powershell
-NoProfile -Command "$msg = '{\"GroupName\":\"Critical\",\"CustomMessage\":\"GPU Temperature: <#GPU Core Temp#>°C\"}'; $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'sendalerts-pipe', 'Out'); $pipe.Connect(1000); $writer = New-Object System.IO.StreamWriter($pipe); $writer.Write($msg); $writer.Flush(); $pipe.Close()"
```
3. 也可參考在 SendAlerts 中群組設定的 CLI 範例

<div style="display: flex; flex-wrap: wrap; gap: 10px;">
  <img src="./docs/ScreenShots/HWiNFO_Alerts.png" style="width: 75%; height: auto;">
</div>


#### 參數說明

| 變數 | 說明 |
|------|------|
| `GroupName` | SendAlerts 中的 Alert Group 名稱 |
| `CustomMessage` | 自訂訊息，可包含 HWiNFO64 變數 |
| `<#GPU Core Temp#>` | HWiNFO64 感測器值變數 |

### 步驟 4：測試警報

1. 確認 SendAlerts 已啟動（狀態列顯示綠燈）
2. 在 HWiNFO64 中手動觸發警報（調整門檻值測試）
3. 檢查 SendAlerts 是否收到警報並執行動作

## 常用 Alert Group 範例

### Critical (嚴重警報)
用於需要立即處理的情況：
- GPU/CPU 溫度過高
- 風扇停轉
- 電壓異常

```json
{"GroupName":"Critical","CustomMessage":"[CRITICAL] GPU 溫度過高: <#GPU Core Temp#>°C"}
```

### Warning (警告)
用於需要注意但不緊急的情況：
- 溫度接近門檻
- 使用率持續偏高

```json
{"GroupName":"Warning","CustomMessage":"[WARNING] CPU 使用率: <#Total CPU Usage#>%"}
```

### Info (資訊)
用於記錄性質的通知：
- 系統狀態變更
- 定期報告

```json
{"GroupName":"Info","CustomMessage":"系統狀態正常 - GPU: <#GPU Core Temp#>°C"}
```

## HWiNFO64 常用變數

| 變數 | 說明 |
|------|------|
| `<#GPU Core Temp#>` | GPU 核心溫度 |
| `<#GPU Hot Spot Temp#>` | GPU 熱點溫度 |
| `<#GPU Power#>` | GPU 功耗 |
| `<#GPU Fan#>` | GPU 風扇轉速 |
| `<#CPU Package Temp#>` | CPU 封裝溫度 |
| `<#Total CPU Usage#>` | CPU 總使用率 |
| `<#Physical Memory Used#>` | 已使用記憶體 |

> **提示**: 變數名稱取決於您的硬體，請在 HWiNFO64 中查看實際的感測器名稱。

## 疑難排解

### 問題：警報未觸發

1. 確認 SendAlerts 狀態列顯示綠燈
2. 檢查 HWiNFO64 Alert 設定是否正確
3. 查看 SendAlerts 日誌檔 (`%AppData%\SendAlerts\logs\`)

### 問題：收到 "找不到群組" 錯誤

確認 `GroupName` 與 SendAlerts 中設定的群組名稱完全一致（區分大小寫）。

### 問題：PowerShell 執行錯誤

1. 確認 PowerShell 執行政策允許執行腳本
2. 嘗試在命令提示字元中手動執行測試

## 進階：使用 SendAlerts-cli（推薦）

使用 CLI 工具比 PowerShell 更簡潔，但直接呼叫 `SendAlerts-cli.exe` 會閃現主控台視窗。
請使用提供的 VBS wrapper 以無視窗模式執行：

1. **Action Type**: 選擇 **Run Program**
2. **Program**: `wscript.exe`
3. **Arguments**:
   ```
   "C:\path\to\SendAlerts-cli-silent.vbs" send -g Critical -m "GPU Temp: <#GPU Core Temp#>°C"
   ```

> `SendAlerts-cli-silent.vbs` 位於 `scripts/` 目錄，會以隱藏視窗模式呼叫 `SendAlerts-cli.exe`，不會閃現主控台視窗。

## 進階：使用腳本檔案

若 Arguments 欄位長度限制，可將 PowerShell 腳本存為檔案：

1. 建立 `send-alert.ps1`（參考 `scripts/send-alert.ps1`）
2. HWiNFO64 Arguments 設定為：
   ```
   -ExecutionPolicy Bypass -File "C:\path\to\send-alert.ps1" -GroupName "Critical" -Message "GPU: <#GPU Core Temp#>°C"
   ```

## 相關資源

- [SendAlerts GitHub](https://github.com/your-repo/SendAlerts)
- [HWiNFO64 官網](https://www.hwinfo.com/)
- [範例腳本](../scripts/)
