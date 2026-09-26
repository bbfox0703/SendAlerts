# 專案規格書：SendAlerts - 硬體警報中繼站

## 專案定位

**SendAlerts** 是一款「硬體警報中繼站 (Hardware Alert Relay Station)」，專注於：

- **接收外部觸發** - 透過 Named Pipe 或 HTTP API 接收警報指令
- **多管道通知** - 執行 Discord Webhook、Telegram Bot、命令列等多種通知動作
- **群組管理** - 將多個 Action 組合為 Group，統一管理

主畫面的 GPU/CPU 監控資訊僅供參考，**不主動發送警報**。警報功能由 HWiNFO64 等專業外部工具觸發。

---

## 1. 核心架構

### 1.1 三層式警報架構 (Three-Tier Alert System)

```
┌─────────────────────────────────────────────────────────────────┐
│                    Interface Tier (接口層)                       │
│  Named Pipe: \\.\pipe\sendalerts-pipe                          │
│  HTTP API:   http://localhost:58080/api/send                   │
│  JSON Format: { "GroupName": "...", "CustomMessage": "..." }    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Group Tier (群組層)                           │
│  AlertGroup: { Name, MessageTemplate, List<ActionInstanceIds> } │
│  Example: "Critical" → [Discord_Admin, Telegram_All]           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Action Tier (動作層)                          │
│  IAlertAction implementations with unique InstanceId            │
│  Types: Discord, Telegram, CommandLine, HttpWebhook, Email     │
└─────────────────────────────────────────────────────────────────┘
```

### 1.2 專案結構

```
SendAlerts/               # 核心邏輯 (平台中立，由 Desktop / Cli / Watchdog / Tests 共用)
├── Interfaces/           # IAlertAction, IGpuProvider, ISensorDataProvider, IHwinfoProvider,
│                         # IStartupManager, IHttpUrlAclManager
├── Models/               # AlertActionConfig, AlertGroup, PipeMessage, ChartSlotConfig
├── Services/             # AlertService, AlertActionFactory, *AlertAction, HttpApiServer,
│                         # JsonSettingsService, SettingsMigrator, LocalizationService
├── Implementations/      # DemoGpuProvider (無 GPU 時的模擬數據)
├── ViewModels/           # MainViewModel, AlertActionsViewModel, etc.
├── Views/                # AXAML UI 定義
├── Converters/           # UI 值轉換器
└── Resources/            # 多語系資源檔 (Strings.resx)

SendAlerts.Desktop/       # Windows 桌面端 (win-x64)
├── Implementations/      # NamedPipeServer, SingleInstanceManager, NvApi/Nvml/CpuNetwork providers,
│                         # LhmSensorProvider, HwinfoSharedMemoryReader, StartupManager, TrayIconManager
└── Program.cs            # 程式進入點

SendAlerts.Cli/           # 命令列工具
└── Program.cs            # CLI 進入點

SendAlerts.Watchdog/      # Watchdog 監控程式 (偵測主程式異常退出並重啟)
SendAlerts.Tests/         # xUnit v3 單元測試
tools/NvI2CScanner/       # 獨立的 NVIDIA I2C 匯流排掃描工具 (唯讀)
```

---

## 2. 功能規格

### 2.1 接口層 (Interface Tier)

#### Named Pipe Server
- **Pipe 名稱**: `\\.\pipe\sendalerts-pipe`
- **協定**: JSON 文字訊息
- **格式**:
  ```json
  {
    "GroupName": "Critical",
    "CustomMessage": "GPU 溫度 95°C"
  }
  ```

#### HTTP API Server
- **預設 Port**: 58080 (可設定)
- **認證**: X-API-Key Header
- **端點**:
  | Method | Path | Description |
  |--------|------|-------------|
  | POST | /api/send | 發送警報 |
  | GET | /api/health | 健康檢查 |
  | GET | /api/groups | 取得群組清單 |

#### CLI 工具
```bash
# 發送警報
SendAlerts-cli send -g <GroupName> -m <Message>

# 列出群組
SendAlerts-cli list

# 發送測試訊息至 Default 群組
SendAlerts-cli test
```

### 2.2 群組層 (Group Tier)

#### AlertGroup 資料模型
```csharp
public class AlertGroup
{
    public string Name { get; set; }              // 唯一識別名稱 (CLI-safe)
    public string? Description { get; set; }       // 說明
    public bool IsEnabled { get; set; }           // 是否啟用
    public string MessageTemplate { get; set; }   // 訊息範本
    public List<string> ActionInstanceIds { get; set; }  // 關聯的 Action
}
```

#### 訊息範本變數
| 變數 | 說明 |
|------|------|
| `{message}` | CustomMessage 或預設訊息 |
| `{timestamp}` | 完整時間戳 |
| `{date}` | 日期 |
| `{time}` | 時間 |
| `{group_name}` | 群組名稱 |

#### 群組命名規則
- 只能包含：`a-zA-Z0-9_-`
- 必須以英文字母開頭
- 大小寫敏感

### 2.3 動作層 (Action Tier)

#### 支援的 Action 類型

| Type | 說明 | 必填欄位 |
|------|------|----------|
| **CommandLine** | 執行本地命令 | Command |
| **Telegram** | Telegram Bot API | BotToken, ChatId |
| **Discord** | Discord Webhook | WebhookUrl |
| **HttpWebhook** | 通用 HTTP Webhook (準備中) | Url, Method |
| **Email** | SMTP 郵件 (準備中) | SmtpHost, From, To |

#### IAlertAction 介面
```csharp
public interface IAlertAction
{
    string InstanceId { get; }                                   // 唯一識別碼
    AlertActionType ActionType { get; }                          // 動作類型
    string DisplayName { get; }                                  // 顯示名稱
    bool IsEnabled { get; set; }                                 // 是否啟用
    Task<AlertActionExecuteResult> ExecuteAsync(string message); // 執行動作
    AlertActionValidationResult Validate();                      // 驗證設定
}
```

#### 冷卻機制
- 每個 Action 獨立計算冷卻時間
- 預設 30 秒，可調整 5-300 秒
- 冷卻期間不重複執行

---

## 3. 硬體監控 (Display Only)

### 3.1 概述

主畫面的 GPU/CPU 監控資訊**僅供顯示參考**，不主動觸發警報。

### 3.2 硬體提供者

| Provider | 說明 |
|----------|------|
| NvApiWindowsProvider | NVIDIA GPU，優先嘗試 |
| NvmlWindowsProvider | NVIDIA GPU，NvAPI 不可用時使用 (GPU provider 只保留第一個成功的) |
| CpuNetworkWindowsProvider | CPU / Network，與 GPU provider 並存 |
| DemoGpuProvider | 以上皆不可用時的保底 (模擬數據) |
| HwinfoSharedMemoryReader | HWiNFO64 Shared Memory 感測器 (供圖表的外部來源使用) |
| LhmSensorProvider | LibreHardwareMonitor 感測器 (供圖表的外部來源使用) |

### 3.3 顯示指標

主畫面有 4 個圖表欄位 (`ChartSlots`)，每個欄位可設定為：

- **Off** - 不顯示
- **內建預設**: GPU 使用率、GPU 溫度、GPU 功耗、CPU 使用率、記憶體使用率、Network I/O
- **外部感測器**: 從 HWiNFO64 Shared Memory 或 LibreHardwareMonitor 選擇任一感測器

---

## 4. 設定系統

### 4.1 設定檔位置

- `%AppData%\SendAlerts\settings.json`

### 4.2 主要設定項目

```json
{
  "settingsVersion": 4,
  "useAlertCenterMode": true,
  "samplingIntervalSeconds": 1,
  "httpApiEnabled": false,
  "httpApiPort": 58080,
  "httpApiKey": "...",
  "watchdogEnabled": false,
  "chartSlots": [...],
  "alertActions": [...],
  "alertGroups": [...]
}
```

---

## 5. 多語系支援

### 5.1 支援語系

| 代碼 | 語言 | 自動偵測 |
|------|------|----------|
| en | English | 預設 |
| zh-TW | 繁體中文 | OS: zh-TW, zh-Hant |
| ja | 日本語 | OS: ja-* |

### 5.2 實作方式

- 使用 .NET ResX 資源檔
- `LocalizationService` 單例管理
- 優先順序：使用者設定 > OS 語系 > 英文預設

---

## 6. 系統整合

### 6.1 單一實例

- 使用 Named Mutex 確保只有一個實例運行
- 第二實例啟動時透過 Named Pipe 通知主實例還原視窗，然後退出

### 6.2 系統匣

- 關閉視窗後縮小至系統匣
- 右鍵選單：顯示 / Alert Actions / Alert Groups / 開機自動啟動 / 結束
- 支援 `--minimized` 參數直接啟動至系統匣

### 6.3 開機自動啟動

- 透過 Windows 登錄檔設定
- 路徑：`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`

---

## 7. 技術堆疊

| 類別 | 技術 |
|------|------|
| **Framework** | .NET 10 / C# |
| **UI** | Avalonia UI 12 (MVVM) |
| **MVVM** | CommunityToolkit.Mvvm |
| **Hardware** | NVIDIA NVML / NvAPI, LibreHardwareMonitorLib, HWiNFO64 Shared Memory |
| **Charts** | ScottPlot.Avalonia |
| **Logging** | Serilog |
| **HTTP** | System.Net.HttpListener |
| **IPC** | System.IO.Pipes |

---

## 8. 開發規範

### 8.1 非同步處理

- 所有 I/O 操作必須非同步執行
- 不得阻塞 UI 執行緒

### 8.2 錯誤處理

- 所有 P/Invoke 呼叫必須包覆 try-catch
- 使用 Serilog 記錄詳細錯誤

### 8.3 命名慣例

- **PascalCase**: 類別、方法、屬性
- **_camelCase**: 私有欄位
- **Loc_**: 本地化字串屬性前綴

---

## 9. 外部整合

### 9.1 HWiNFO64 整合

透過 HWiNFO64 的「執行程式」功能，在觸發條件時執行 PowerShell 腳本發送 Named Pipe 訊息。

詳見 [`docs/HWiNFO-Setup.md`](HWiNFO-Setup.md)

### 9.2 自訂腳本整合

提供 PowerShell 和 Python 範例腳本：
- `scripts/send-alert.ps1`
- `scripts/send_alert.py`
