# 專案規格書：RTX 16-pin GPU 監控與警報系統 (16pin-vmon)

# 功能簡述

監測RTX GPU顯示卡的 16 pin voltage數值、以及GPU溫度，在voltage低於設定的警示值、或是GPU溫度高於設定的榅度警示值時，執行發送alert的動作。

## 核心功能構思
1. **數據採集**：經由 NVIDIA NVML 每秒讀取一次 16-pin voltage 和 GPU temperature。
2. **視覺化**：數據即時更新於 GUI 折線圖，電壓與溫度分為獨立圖表。
3. **定時器**：預設 1 秒取樣一次，使用者可自定義取樣頻率。
4. **抽象層設計 (Abstraction Layer)**：確保 OS 與硬體 API 呼叫跨平台（Windows/Linux）相容。
5. **警報系統**：
* 支援電壓、溫度獨立門檻設定。
* **判定機制**：滑動視窗（X 秒內達到門檻 Y 次）。
* **多樣化 Action**：本地指令、LINE、Telegram、WhatsApp、Email (MAPI/GMail)。
6. 強制斷電功能：Alert觸發後，可設定是否要經由抽象層 (Abstraction Layer Interface) 觸發斷電程序的 interface，預設是呼叫OS強制關機
7. 需要經SeriLog來寫入log，管理log rotate
8. 多語系支援，預設英文界面，但是可自動偵測OS語系而切換成繁體中文或是日文。支援手動設定語系功能。
9. 相關設定 (alert threshold, alert actions, UI language、save to csv、幾秒讀一次數值) 要能儲存下來，下次執行時自動套用。設定管理一樣要跨平台能使用
10. 支援 Windows theme aware。Theme aware 一樣要經抽象層來完成。
11. 使用者可設定 16 pin volatage 以及 GPU temperature、或是以後新增的監控項目，能存到 .csv 中，使用者可設定 csv 保留筆數。要有UI可設定
12. 硬體相容性與識別策略 (Hardware Compatibility)
為了應對 RTX 50 系列（及後續型號）不同 AIB 廠商（如 MSI, ASUS）可能採用不同 Field ID 的問題，採用以下策略：
12.1 硬體身分識別 (Device Identity)
* **PCI Info 擷取**：必須讀取顯卡的 `Device ID`、`Vendor ID` 以及最重要的 **`Subsystem ID`** (SSID)。
* **識別格式**：採 `VendorID:DeviceID:SubsystemID` 組合（例如 `10DE:2684:1462:5170`）。
12.2 三段式 Field ID 判定邏輯
a. **第一優先：JSON 資料庫 (Hardware DB)**
* 檢查專案內附的 `gpu_mapping.json`。
* 若目前顯卡的 `SubsystemID` 存在於資料庫，直接使用其標註的 `VerifiedVoltageFieldId`。
b. **第二優先：動態掃描 (Dynamic Probing)**
* 若資料庫無紀錄，啟動 Probe-First 模式，掃描 NVML Field ID (範圍 150-165)。
* 若偵測到數值符合 ，則自動鎖定該 ID 為監控對象。
c. **第三優先：Fallback 機制**
* 若上述皆失敗，則讀取 GPU Total Power 並於 UI 提示「目前為估算數值」。
12.3 獨立 Mapping 工具
* **CLI 工具功能**：支援以獨立 Command Line 模式執行，輸出目前顯卡的 PCI 資訊與偵測到的 Field ID 至 JSON 格式。要考量多顯示卡的環境中，能抓取到 nVidia GPU
* **用途**：便於使用者回報資料，擴充 `gpu_mapping.json`。
13 Windows 終極方案：NVAPI 整合
* **觸發條件**：若 NVML 偵測不到有效 Field ID（Blackwell 架構常態）。
* **實作方式**：載入 `nvapi64.dll`，使用 `NvAPI_GPU_GetPowerSensors` (V1/V2) 讀取硬體感測器。
* **優點**：可精確識別 16-pin 每個 Rail 的電流與電壓，支援 RTX 50 系列所有 AIB 私板。
 
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
4. **硬體映射 JSON 結構** 假設範例：
```json
{ "GpuMappings": [ { "SubsystemId": "1462:5170", "FieldId": 156, "Model": "MSI 5090 Suprim X" } ] }

```


> **給 CLI 的說明**：
> 本專案已具備 `avalonia.xplat` 結構。目前 `NvmlWindowsProvider.cs` 需根據此規格書的「三段式判定邏輯」進行重構。請確保 `HardwareDbManager` 的實作能正確處理 JSON 不存在時的自動回退。 .SLN / .csproj 未 compile 過