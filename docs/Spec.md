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
SendAlerts/               # 核心邏輯 (跨平台)
├── Interfaces/           # IAlertAction, IGpuProvider
├── Models/               # AlertActionConfig, AlertGroup, PipeMessage
├── Services/             # AlertService, LocalizationService, HttpApiServer
├── ViewModels/           # MainViewModel, AlertActionsViewModel, etc.
├── Views/                # AXAML UI 定義
├── Converters/           # UI 值轉換器
└── Resources/            # 多語系資源檔 (Strings.resx)

SendAlerts.Desktop/       # Windows 桌面端
├── Implementations/      # NvmlWindowsProvider, TrayIconManager
└── Program.cs            # 程式進入點

SendAlerts.Cli/           # 命令列工具
└── Program.cs            # CLI 進入點
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

# 測試群組
SendAlerts-cli test -g <GroupName>
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
| **HttpWebhook** | 通用 HTTP Webhook | Url, Method |
| **Email** | SMTP 郵件 (準備中) | SmtpHost, From, To |

#### IAlertAction 介面
```csharp
public interface IAlertAction : IDisposable
{
    string InstanceId { get; }          // 唯一識別碼
    AlertActionType ActionType { get; } // 動作類型
    string DisplayName { get; }         // 顯示名稱
    bool Validate();                    // 驗證設定
    Task ExecuteAsync(string message);  // 執行動作
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

### 3.2 硬體提供者優先順序

| 順序 | Provider | 條件 |
|------|----------|------|
| 1 | NvApiWindowsProvider | Windows + NVIDIA GPU (RTX 50 系列) |
| 2 | NvmlWindowsProvider | Windows + NVIDIA GPU |
| 3 | CpuNetworkWindowsProvider | Windows (無 NVIDIA GPU) |
| 4 | DemoGpuProvider | Fallback (模擬數據) |

### 3.3 顯示指標

- **GPU 模式**: Utilization, Temperature, Power
- **CPU 模式**: CPU Usage, Temperature, Network I/O

---

## 4. 設定系統

### 4.1 設定檔位置

- **Windows**: `%AppData%\SendAlerts\settings.json`
- **Linux**: `~/.config/SendAlerts/settings.json`

### 4.2 主要設定項目

```json
{
  "SettingsVersion": "2.0",
  "UseAlertCenterMode": true,
  "SamplingIntervalSeconds": 1,
  "HttpApiEnabled": true,
  "HttpApiPort": 58080,
  "HttpApiKey": "...",
  "Language": null,
  "AlertActions": [...],
  "AlertGroups": [...]
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
- 第二實例啟動時顯示提示後退出

### 6.2 系統匣

- 關閉視窗後縮小至系統匣
- 右鍵選單：顯示 / 結束
- 支援 `--minimized` 參數直接啟動至系統匣

### 6.3 開機自動啟動

- 透過 Windows 登錄檔設定
- 路徑：`HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`

---

## 7. 技術堆疊

| 類別 | 技術 |
|------|------|
| **Framework** | .NET 10 / C# |
| **UI** | Avalonia UI (MVVM) |
| **MVVM** | CommunityToolkit.Mvvm |
| **Hardware** | NVIDIA NVML / NvAPI |
| **Charts** | LiveCharts2 (SkiaSharp) |
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
