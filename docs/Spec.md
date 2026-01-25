# 專案規格書：RTX 16-pin GPU 監控與警報系統 (16pin-vmon)

## 0. 專案願景

開發一個輕量化、跨平台（Windows/Linux）的 GPU 監控工具，專注於 **12VHPWR (16-pin) 電壓穩定性** 與 **核心溫度**。透過高度抽象化的設計，確保在硬體層與作業系統層具備極佳的擴充性，並提供自動化警報反應機制。

---

## 1. 技術棧 (Technical Stack)

* **語言:** C# (.NET 10 / 2026 Insider 標配)
* **框架:** Avalonia UI (使用 MVVM 模式，推薦 CommunityToolkit.Mvvm)
* **硬體接口:** NVIDIA NVML (NVIDIA Management Library)
* **圖表庫:** LiveCharts2 (Avalonia 版)
* **日誌系統:** Serilog (含 File Sink & Log Rotate)
* **依賴注入:** Microsoft.Extensions.DependencyInjection

---

## 2. 專案架構與模組化

為了達成「一次開發，跨平台執行」，專案必須採用 **(xplat)** 架構：

| 模組名稱 | 職責說明 |
| --- | --- |
| **16pin_vmon.Core** | 核心邏輯、介面定義 (`IGpuProvider`, `IAlertAction`)、ViewModels、多語系處理。 |
| **16pin_vmon.Desktop** | 桌面端啟動點、UI View 實作、Serilog 初始化、各平台硬體存取實作。 |
| **16pin_vmon.Infrastructure** | (可選) 放置網路呼叫 (Telegram/LINE) 或資料庫/CSV 儲存邏輯。 |

---

## 3. 功能詳細規格

### 3.1 硬體監控與抽象層

* **採樣頻率:** 預設 1s/次，使用者可調，設定需持久化。
* **數據來源:** 透過 `IGpuProvider` 介面。
* **Windows:** 載入 `nvml.dll`。
* **Linux:** 載入 `libnvidia-ml.so`。


* **安全機制:** * 必須實作 `IDisposable` 以確保程式關閉時正確執行 `nvmlShutdown()`。
* 初始化失敗時應切換至 Demo 模式，不允許程式崩潰。



### 3.2 警報引擎 (Alert Logic)

* **滑動視窗判定 (Sliding Window):** 警報觸發條件為「$X$ 秒內數值超過門檻 $Y$ 次」。
* **門檻設定:**
* 電壓低於設定值 (預設 11.8V)。
* 溫度高於設定值 (預設 88°C)。

* **警報動作 (Alert Actions):** 需實作 `IAlertAction` 介面。
* **Action 1:** 執行本地命令列指令。
* **Action 2-4:** 網路推送 (LINE Notify, Telegram, WhatsApp)，需具備 Token 設定 UI。
* **Action 5:** email推送，支援一般 MAPI 或是 GMail

* **強制斷電:** 觸發後呼叫 `IPlatformService.ShutdownOS()`，預設關閉。

### 3.3 GUI 介面設計

* **佈局:**
* **Top Panel:** 功能按鈕與選單。
* **Center Area:** 即時折線圖 (Dynamic Height & Width)。
* **Status Bar:** 系統訊息、目前狀態。


* **圖表細節:**
* 支援最近 15 分鐘數據回溯。
* Y 軸自動縮放：電壓區間  (遇 Peak 值自動上調)。
* 具備文字區塊顯示當前最後數值。


* **Theme Aware:** 自動跟隨 Windows/Linux 系統主題，透過 `IThemeService` 抽象化處理。

---

## 4. 資料管理與多語系

* **多語系:** 支援 **英文 (預設)**、**繁體中文**、**日文**。啟動時自動偵測 OS 語系。
* **設定儲存:** * 跨平台路徑管理 (Windows: `%AppData%`, Linux: `~/.config`)。
* 儲存格式: JSON。


* **CSV 匯出:** 可設定存檔筆數，支援記錄 16-pin 電壓與溫度，用於後續分析。

---

## 5. 開發規範

1. **非同步處理:** 所有 I/O 操作 (網路推送、CSV 寫入、Log寫入) 與硬體讀取必須在非同步執行緒執行，不得阻塞 UI。
2. **錯誤處理:** 所有的系統呼叫 (P/Invoke) 必須包覆在 `try-catch` 中，並透過 Serilog 記錄詳細錯誤代碼。
3. **單元測試預留:** 警報邏輯應可獨立於硬體進行測試 (Mocking `IGpuProvider`)。

---

對 CLI 說明指令：
>「我現在有一個空資料夾，請根據這份規格書，使用 `avalonia.xplat` 模板幫我產生初始的專案結構，並先實作 `16pin_vmon.Core` 中的 `IGpuProvider` 介面與警報判定的 `AlertEvaluator` 類別。 .gitignore 已經設定好」