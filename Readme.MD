# 🚀 16pin-vmon (RTX 50 GPU Monitor)

本專案是一個使用 **Avalonia UI** 開發的跨平台（Windows/Linux）GPU 監測工具，核心目標是針對 **RTX 50 系列** 監控 16-pin (12VHPWR) 電壓穩定性，防止燒燬。

> [!IMPORTANT]
> **開發核心原則：** 必須嚴格遵守 `docs/Spec.md` 中的規格。所有硬體存取與系統呼叫必須經過 **Abstraction Layer (Interface)**，嚴禁在 ViewModel 或 View 中直接寫入平台相關的 P/Invoke 代碼。

---

## 🏗️ 專案架構 (Solution Architecture)

本專案採用 `avalonia.xplat` 範本建立，結構如下：

* **`16pin-vmon/` (Core Project)**
* `Interfaces/`: 定義 `IGpuProvider` 與 `IAlertAction`。
* `Logic/`: 包含 `AlertEvaluator` (滑動視窗判定演算法)。
* `ViewModels/`: `MainViewModel` (核心監控邏輯與數據綁定)。
* `Views/`: `MainView.axaml` (跨平台 UI 佈局)。
* `Converters/`: `AlertColorConverter` (UI 狀態轉換)。


* **`16pin-vmon.Desktop/` (Platform Head)**
* `Implementations/`: 包含 `NvmlWindowsProvider.cs` (Windows 平台的 NVML 實作)。
* `Program.cs` & `AppBuilder`: 負責注入具體的實作類別。



---

## 🛠️ 技術棧 (Tech Stack)

| 元件 | 內容 |
| --- | --- |
| **Language** | C# (.NET 10 Insider) |
| **GUI Framework** | Avalonia UI (MVVM) |
| **Hardward API** | NVIDIA NVML (v2) |
| **Charts** | LiveCharts2 (SkiaSharp) |
| **Logging** | Serilog |
| **Alert Logic** | Sliding Window ( seconds,  hits) |

---

## 📈 目前開發進度 (Current State)

* [x] **抽象層設計**：`IGpuProvider` 介面已定義。
* [x] **警報演算法**：`AlertEvaluator` (滑動視窗) 已實作。
* [x] **Windows 實作**：`NvmlWindowsProvider` 已完成 P/Invoke 封裝 (含 Field Value 156 讀取)。
* [x] **ViewModel 核心**：`MainViewModel` 已實作 Timer 驅動與 LiveCharts2 數據對應。
* [x] **UI 佈局**：`MainView.axaml` 已實作雙等高折線圖與 Status Bar。
* [ ] **設定系統**：尚未實作 JSON 存取與跨平台路徑管理。
* [ ] **Alert Actions**：尚未實作 LINE / Telegram / Command-line 觸發。
* [ ] **多語系**：尚未實作 I18N 資源檔與切換邏輯。

---

## 🤖 給 CLI 的指令指南

如果你被授權進行後續開發，請遵循以下優先權：

1. **優先閱讀** `docs/Spec.md` 以了解完整的業務邏輯。
2. **依賴注入**：在 `App.axaml.cs` 中根據 `RuntimeInformation` 注入正確的 `IGpuProvider`。
3. **UI 強化**：目前 `MainView.axaml` 需要 `LiveCharts` 的命名空間正確配置以及 `AlertColorConverter` 的資源註冊。
4. **警報擴充**：在 `Core` 專案中實作 `IAlertAction` 介面。

---

## ⚠️ 開發注意事項

* **RTX 50 數值**：應實作動態 Field 偵測機制。雖然預設可能是 `NVML_FI_DEV_VOLTAGE_GRAPHICS_12V` (ID 156)，但優先參考下方的 硬體相容性與探索策略 (Hardware Probing Strategy)
* **跨平台路徑**：設定檔必須儲存在 `Environment.SpecialFolder.ApplicationData`。
* **記憶體管理**：確保 `IGpuProvider` 的 `Dispose` 被呼叫以執行 `nvmlShutdown`。
* **Debug Mode**：除錯模式下，具破壞性的 Alert actions，例如強制切掉電源、執行 command line 等，變成只有產生 debug log 但是沒有動作

## 硬體相容性與探索策略 (Hardware Probing Strategy)
先讀取 **硬體映射 JSON 結構資料庫**，使用 **三段式 Field ID 判定邏輯**，和現行顯示卡進行比對來取得 Field ID，如果沒有資料：
1. **動態探針 (Dynamic Probing):** - 由於 RTX 50 系列不同廠商 (AIB) 可能定義不同 Field ID，程式啟動時需對 NVML 進行電壓軌掃描。
2. **數據校驗:** - 僅當數值符合 $11.2V < V < 12.6V$ 時，方判定為有效的 16-pin 監控對象。
3. **Fallback 模式:** - 若無法鎖定 Field ID，應嘗試讀取 GPU 全域功耗 (Total Power) 換算為預估值，並於 Status Bar 顯示「警告：電壓數據為預估值」。