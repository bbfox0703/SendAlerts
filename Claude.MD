# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands
- **Build**: `dotnet build`
- **Run**: `dotnet run --project 16pin-vmon.Desktop`
- **Clean**: `dotnet clean`

## Project Overview

**專案定位轉型：硬體警報中繼站 (Hardware Alert Relay Station)**

本專案從 GPU 監控工具轉型為「硬體警報中繼站」，利用 HWiNFO64 等專業工具作為資料源：
- 核心功能：接收外部觸發並執行多管道警報 (Alert Center)
- UI 顯示：GPU 功耗、核心電壓與溫度僅供參考，不再主動發送警訊
- 通訊方式：Single Instance 模式，所有外部指令透過 Named Pipe 集中處理

Built with Avalonia UI for Windows/Linux cross-platform support.

## Architecture

### Alert 多層級架構 (Three-Tier Alert System)

```
┌─────────────────────────────────────────────────────────────────┐
│                    Interface Tier (接口層)                       │
│  Named Pipe Server: \\.\pipe\16pin-vmon-alert                   │
│  JSON Format: { "GroupName": "...", "CustomMessage": "..." }    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Group Tier (群組層)                           │
│  AlertGroup: { Name, MessageTemplate, List<ActionReference> }   │
│  Example: "Critical" → [LINE_Admin, Telegram_All, Email_IT]     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Action Tier (動作層)                          │
│  IAlertAction implementations with unique InstanceId            │
│  Example: LINE_User1, LINE_Admin1, Telegram_All, Email_IT       │
└─────────────────────────────────────────────────────────────────┘
```

### Solution Structure
```
16pin-vmon/           (Core - cross-platform logic, namespace: _16pin_vmon)
├── Interfaces/       IAlertAction, IGpuProvider definitions
├── Models/           AlertActionConfig, AlertGroup, PipeMessage
├── Services/         AlertService, NamedPipeServer, ServiceLocator
├── ViewModels/       MainViewModel, AlertManagementViewModel
├── Views/            MainView, AlertManagementView
└── Converters/       UI value converters

16pin-vmon.Desktop/   (Platform head, namespace: _16pin_vmon.Desktop)
├── Implementations/  NvmlWindowsProvider, SingleInstanceManager
└── Program.cs        Single instance mutex + Named Pipe init
```

**Note**: Namespace uses underscores: `_16pin_vmon` (not `16pin-vmon`)

### Key Abstractions
- **`IAlertAction`**: Alert action interface with `InstanceId` for multi-instance support
- **`AlertGroup`**: Groups multiple actions with shared message template
- **`NamedPipeServer`**: Listens for external JSON commands
- **`AlertService`**: Orchestrates action execution by group
- **`IGpuProvider`**: Hardware abstraction for GPU data (display only)

### Data Flow (New)
```
External Tool (HWiNFO64)
        │
        ▼ Named Pipe JSON: {"GroupName":"Critical","CustomMessage":"GPU過熱!"}
┌───────────────────┐
│ NamedPipeServer   │
└───────────────────┘
        │
        ▼ Parse & Dispatch
┌───────────────────┐
│   AlertService    │ → Find Group by Name
└───────────────────┘
        │
        ▼ Execute All Actions in Group
┌───────────────────────────────────────────┐
│ IAlertAction[] (LINE, Telegram, Email...) │
└───────────────────────────────────────────┘
```

### HWiNFO64 Integration Example
```
; HWiNFO64 Alert Configuration
; Action: Run Program
; Program: powershell.exe
; Arguments: -Command "'{\"GroupName\":\"Critical\",\"CustomMessage\":\"GPU Temp: <#GPU Core Temp#>°C\"}' | Out-File -FilePath \\.\pipe\16pin-vmon-alert -Encoding utf8"
```

## Coding Standards

### Critical Rules
- **Single Instance**: Use Mutex to ensure only one instance runs
- **Named Pipe Security**: Validate JSON schema before processing
- **Async everywhere**: All I/O, pipe operations, and alert actions must be async
- **READ-ONLY operations only** - 本工具僅讀取硬體數據，嚴禁任何「寫入」操作

### Patterns
- **MVVM**: Use `CommunityToolkit.Mvvm` with `[ObservableProperty]` and `partial` classes
- **Async**: All I/O, logging, and alert actions must be async
- **Naming**: `PascalCase` for classes/methods, `_camelCase` for private fields

## Tech Stack
- .NET 10 / C#
- Avalonia UI (MVVM)
- System.IO.Pipes (Named Pipe IPC)
- LiveCharts2 (SkiaSharp)
- Serilog (logging)
- CommunityToolkit.Mvvm
- MailKit (Email support)

---

## Development TODO

> 本專案會在多台 PC 上開發，以下 TODO 用於追蹤進度。完成項目後請更新狀態。

### 📋 實作優先順序 (Recommended Order)

```
Phase A (核心架構) ─────────────────────────────────────────────────────────
   │
   ├─ A1: Single Instance + Named Pipe ← 最優先！建立通訊基礎
   │      TA1-1 → TA1-3 → TA1-4 → TA1-5 → TA1-2
   │
   ├─ A2: Action Tier ← 重構現有 Actions
   │      TA2-1 → TA2-2 → TA2-3 → TA2-4/5/9 (重構) → TA2-6/7/8 (新增)
   │
   ├─ A3: Group Tier ← 建立群組邏輯
   │      TA3-1 → TA3-2 → TA3-3 → TA3-4
   │
   └─ A4: Settings ← 整合設定
          TA4-1 → TA4-2 → TA4-3

Phase B (UI) ──────────────────────────────────────────────────────────────
   │
   ├─ B1: Action 管理 UI
   ├─ B2: Group 編輯 UI
   └─ B3: 主介面整合

Phase C/D (收尾) ──────────────────────────────────────────────────────────
   │
   ├─ C1: 移除主動警報、UI 簡化
   ├─ C2: HWiNFO64 整合文件
   └─ D1/D2: CLI 工具、系統整合
```

---
### 🚀 Phase A: Alert Center 核心架構 (專案轉型)
> 優先級: **最高** | 目標: 建立三層式警報中繼站架構

#### A1: Single Instance 與 Named Pipe (基礎通訊)
- [x] **TA1-1**: `SingleInstanceManager` - 使用 Mutex 確保單一實例運行
- [ ] **TA1-2**: 第二實例啟動時，透過 Named Pipe 傳送參數給主實例後退出
- [x] **TA1-3**: `NamedPipeServer` - 持續監聯 `\\.\pipe\16pin-vmon-alert`
- [x] **TA1-4**: `PipeMessage` 資料模型 - JSON Schema: `{ GroupName, CustomMessage }`
- [x] **TA1-5**: Pipe 訊息驗證與錯誤處理 (invalid JSON, unknown group)

#### A2: Action Tier 動作層 (多實例支援)
- [x] **TA2-1**: 重構 `IAlertAction` 介面 - 新增 `InstanceId`, `ActionType` 屬性
- [x] **TA2-2**: `AlertActionConfig` 資料模型 - 儲存單一動作的設定
- [x] **TA2-3**: `AlertActionFactory` - 根據 ActionType 建立對應的 IAlertAction 實例
- [x] **TA2-4**: 重構 `TelegramAlertAction` - 支援多實例 (不同 Bot/Chat) *(已在 TA2-1 完成)*
- [x] **TA2-5**: 重構 `LineNotifyAlertAction` - 支援多實例 (不同 Token) *(已在 TA2-1 完成)*
- [ ] **TA2-6**: 新增 `EmailAlertAction` - MailKit SMTP 整合
- [ ] **TA2-7**: 新增 `HttpClientAlertAction` - 支援 Webhook/REST API
- [ ] **TA2-8**: 新增 `SystemShutdownAction` - 安全關機 (需確認對話框)
- [x] **TA2-9**: 重構 `CommandLineAlertAction` - 支援多實例 *(已在 TA2-1 完成)*

#### A3: Group Tier 群組層
- [x] **TA3-1**: `AlertGroup` 資料模型 - `{ Name, MessageTemplate, ActionIds[] }`
- [x] **TA3-2**: `AlertService` - 管理群組 CRUD 與執行邏輯
- [x] **TA3-3**: 訊息範本變數替換: `{message}`, `{timestamp}`, `{group_name}` *(已在 TA3-1 完成)*
- [x] **TA3-4**: CustomMessage 覆蓋邏輯 - Pipe 訊息優先於群組範本 *(已在 TA3-1 完成)*

#### A4: 設定系統重構
- [x] **TA4-1**: 擴展 `AppSettings` - 新增 `List<AlertActionConfig>` 和 `List<AlertGroup>`
- [x] **TA4-2**: 設定檔版本遷移 - 舊版設定自動轉換為新格式
- [x] **TA4-3**: 預設群組建立 - 首次啟動時建立 "Default" 群組

---
### 🎨 Phase B: Alert Center UI (警報管理介面)

#### B1: Action 管理頁面
- [x] **TB1-1**: `AlertActionsView` - 顯示所有已設定的 Action 清單
- [x] **TB1-2**: Action 新增對話框 - 選擇 ActionType 後填入設定
- [x] **TB1-3**: Action 編輯功能 - 修改現有 Action 設定 *(整合於 TB1-2)*
- [x] **TB1-4**: Action 刪除功能 - 確認後移除 (檢查是否被 Group 引用) *(已在 TB1-1 完成)*
- [x] **TB1-5**: Action 測試按鈕 - 發送測試訊息驗證設定正確 *(已在 TB1-1 完成)*

#### B2: Group 編輯器
- [x] **TB2-1**: `AlertGroupsView` - 顯示所有 Group 清單
- [x] **TB2-2**: Group 新增/編輯對話框 - 設定名稱與訊息範本
- [x] **TB2-3**: Action 勾選清單 - 多選要加入 Group 的 Actions
- [x] **TB2-4**: Group 刪除功能 - 確認後移除
- [x] **TB2-5**: Group 測試按鈕 - 觸發該 Group 所有 Actions

#### B3: 主介面整合
- [x] **TB3-1**: 導航列新增「警報管理」按鈕 *(Alert Actions / Alert Groups 按鈕已存在)*
- [x] **TB3-2**: 狀態列顯示 Named Pipe 連線狀態
- [x] **TB3-3**: 最近警報歷史清單 (最近 10 筆觸發紀錄)

---
### 📊 Phase C: 顯示優化 (Display-Only Mode)

#### C1: UI 簡化
- [x] **TC1-1**: 移除主動警報邏輯 - MainViewModel 不再觸發 AlertEvaluator
- [x] **TC1-2**: 圖表改為純顯示模式 - 移除警報相關 UI 元素
- [x] **TC1-3**: 新增「僅供參考」標籤 - 提示資料來源為本機讀取

#### C2: HWiNFO64 整合文件
- [x] **TC2-1**: 撰寫 HWiNFO64 設定教學文件 (docs/HWiNFO-Setup.md)
- [x] **TC2-2**: 提供範例 PowerShell 腳本傳送 Pipe 訊息
- [x] **TC2-3**: 提供範例 Python 腳本傳送 Pipe 訊息

---
### 🔧 Phase D: 進階功能

#### D1: CLI 工具
- [x] **TD1-1**: 命令列工具發送 Pipe 訊息 (16pin-vmon-cli.exe send -g Critical -m "Test")
- [x] **TD1-2**: 命令列查詢 Group 清單

#### D2: 系統整合
- [x] **TD2-1**: Windows 開機自動啟動選項
- [x] **TD2-2**: 系統匣圖示 (Tray Icon) 最小化
- [x] **TD2-3**: 系統匣右鍵選單 (顯示/隱藏/退出)

---
### 📁 Legacy Phases (已完成 - 保留參考)

<details>
<summary>點擊展開已完成的舊版 TODO</summary>

### Phase 0: Legal & Safety Compliance (法律與安全合規)
- [x] **T0-1**: 啟動警告視窗 - 首次啟動時顯示免責聲明，要求勾選「我已閱讀免責聲明，並了解監控高負載硬體之風險」後才進入主畫面
- [x] **T0-2**: 免責聲明確認狀態持久化 - 記錄使用者已確認，下次啟動不再顯示
- [x] **T0-3**: Audit Log 強化 - Serilog 完整紀錄警報觸發瞬間（時間戳、電壓值、溫度值、觸發門檻），作為「程式已盡提醒義務」之證明

### Phase 1: Core Infrastructure (基礎架構)
- [x] `IGpuProvider` interface definition
- [x] `NvmlWindowsProvider` with P/Invoke
- [x] `AlertEvaluator` sliding window logic
- [x] `MainViewModel` timer-driven monitoring
- [x] LiveCharts2 dual-chart UI layout
- [x] **T1-1**: Dependency Injection setup (ServiceLocator + RuntimeInformation platform detection)
- [x] **T1-2**: Demo/Mock `IGpuProvider` for testing without GPU
- [x] **T1-3**: NvApiWindowsProvider - NVML fallback for RTX 50 series (Blackwell)
- [x] **T1-4**: CpuNetworkWindowsProvider - CPU/Network fallback when no NVIDIA GPU

### Phase 2: Hardware Database (硬體資料庫)
- [x] **T2-1**: Create `HardwareDbManager` class
- [x] **T2-2**: Create initial `gpu_mapping.json` schema and sample data
- [x] **T2-3**: Integrate three-tier Field ID detection into `NvmlWindowsProvider`
- [x] **T2-4**: Add PCI Subsystem ID reading via NVML

### Phase 3: Settings System (設定系統)
- [x] **T3-1**: Create `ISettingsService` interface
- [x] **T3-2**: Implement JSON-based settings persistence
- [x] **T3-3**: Cross-platform path management (`%AppData%` / `~/.config`)
- [x] **T3-4**: Settings UI (threshold values, sampling rate)

### Phase 4: Alert Actions (警報動作) - 部分重構至 Phase A
- [x] **T4-1**: `CommandLineAlertAction` - execute local commands
- [x] **T4-2**: `TelegramAlertAction` - bot API integration
- [x] **T4-3**: `LineNotifyAlertAction` - LINE Notify API
- [x] **T4-4**: Alert action configuration UI
- [x] **T4-5**: Debug mode - destructive actions log-only

### ~~Phase 5: Data Export (資料匯出)~~ - 暫緩
### ~~Phase 6: UI/UX Enhancements (介面強化)~~ - 部分整合至 Phase B
### ~~Phase 7: CLI Tools (命令列工具)~~ - 整合至 Phase D

</details>

---

### Completed Log (完成紀錄)
| Task ID | Description | Date | Notes |
|---------|-------------|------|-------|
| - | Initial project setup | - | avalonia.xplat template |
| - | IGpuProvider & NvmlWindowsProvider | - | Dynamic field detection implemented |
| - | AlertEvaluator | - | Sliding window algorithm |
| - | MainViewModel & UI | - | LiveCharts2 integration |
| T0-1 | 啟動警告視窗 | 2026-01-25 | DisclaimerWindow + DisclaimerViewModel |
| T1-2 | DemoGpuProvider | 2026-01-25 | Mock provider for testing without GPU |
| - | Solution/Namespace cleanup | 2026-01-25 | Fixed sln paths, namespace consistency |
| T0-3 | Audit Log 強化 | 2026-01-25 | Serilog File sink + rotate, alert logging |
| T3-1/2/3 | Settings System | 2026-01-25 | ISettingsService + JsonSettingsService |
| T0-2 | 免責聲明持久化 | 2026-01-25 | 整合 Settings System |
| T1-1 | Dependency Injection | 2026-01-25 | ServiceLocator + Platform detection |
| T2-1~4 | Hardware Database | 2026-01-25 | HardwareDbManager + 三段式偵測 + PCI SSID |
| T3-4 | Settings UI | 2026-01-26 | SettingsWindow + SettingsViewModel |
| T4-1 | CommandLineAlertAction | 2026-01-26 | 變數替換、冷卻機制、跨平台 shell 支援 |
| T4-2 | TelegramAlertAction | 2026-01-26 | Telegram Bot API 整合、HTML 格式訊息 |
| T4-3 | LineNotifyAlertAction | 2026-01-26 | LINE Notify API 整合 |
| T4-4 | Alert Action UI | 2026-01-26 | TabControl 分頁設定介面 |
| T4-5 | Debug Mode | 2026-01-26 | AlertActionsDebugMode 設定整合 |
| T1-3 | NvApiWindowsProvider | 2026-01-26 | NVML fallback, nvapi64.dll P/Invoke |
| T1-4 | CpuNetworkWindowsProvider | 2026-01-27 | CPU/Network fallback, LibreHardwareMonitor + NetworkInterface |
| TA1-1 | SingleInstanceManager | 2026-01-27 | Named Mutex 單一實例控制, Program.cs 整合 |
| TA1-3 | NamedPipeServer | 2026-01-27 | Named Pipe 監聽, 事件驅動訊息處理 |
| TA1-4 | PipeMessage | 2026-01-27 | JSON Schema, PipeMessageParser, 錯誤類型定義 |
| TA1-5 | Pipe 訊息驗證 | 2026-01-27 | 整合至 PipeMessageParser, 錯誤處理與日誌 |
| TA2-1 | IAlertAction 重構 | 2026-01-27 | InstanceId, ActionType, DisplayName, Validate(), 多實例建構子 |
| TA2-2 | AlertActionConfig | 2026-01-27 | 扁平化資料模型, 工廠方法, 驗證邏輯 |
| TA2-3 | AlertActionFactory | 2026-01-27 | Config→Action 轉換, 批次建立, 類型檢查輔助方法 |
| TA3-1 | AlertGroup | 2026-01-27 | 群組模型, 訊息範本變數替換, 驗證方法, 工廠方法 (含 TA3-3/TA3-4) |
| TA3-2 | AlertService | 2026-01-27 | Action/Group CRUD, 並行執行, Named Pipe 整合, ServiceLocator 註冊 |
| TA4-1 | AppSettings 擴展 | 2026-01-27 | AlertActions/AlertGroups 清單, SettingsVersion, UseAlertCenterMode, enum 字串序列化 |
| TA4-2 | SettingsMigrator | 2026-01-27 | T4→Alert Center 自動遷移, 版本檢測, 載入時自動執行 |
| TA4-3 | 預設群組建立 | 2026-01-27 | 首次啟動自動建立 Default/Critical/Warning/Info 群組 |
| TB1-1 | AlertActionsWindow | 2026-01-27 | Action 清單視窗, ViewModel, Converters, MainView 整合 |
| TB1-2/3 | EditActionDialog | 2026-01-27 | 新增/編輯 Action 對話框, 類型切換, 欄位驗證 |
| TB2-1~5 | AlertGroupsWindow | 2026-01-28 | Group 清單視窗, 新增/編輯對話框, Action 勾選, 測試功能 |
| TB3-1~3 | 主介面整合 | 2026-01-28 | 狀態列 Pipe 連線指示燈, 警報歷史清單, ServiceLocator 事件 |
| TC1-1~3 | Display-Only Mode | 2026-01-28 | 移除 AlertEvaluator/T4 警報動作, 純顯示模式提示標籤 |
| TC2-1~3 | HWiNFO64 Integration | 2026-01-28 | docs/HWiNFO-Setup.md, scripts/send-alert.ps1, scripts/send_alert.py |
| TD1-1~2 | CLI 工具 | 2026-01-28 | 16pin-vmon.Cli 專案, send/list/test 命令, System.CommandLine |
| TD2-1~3 | 系統整合 | 2026-01-28 | StartupManager 登錄檔, TrayIconManager 系統匣, 右鍵選單 |
