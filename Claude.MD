# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands
- **Build**: `dotnet build`
- **Run**: `dotnet run --project 16pin-vmon.Desktop`
- **Clean**: `dotnet clean`

## Project Overview
RTX 50 Series (Blackwell) GPU monitoring tool focused on 16-pin (12VHPWR) voltage rail stability. Built with Avalonia UI for Windows/Linux cross-platform support.

**Spec Reference**: Always consult `docs/Spec.md` for hardware logic, alert algorithms, and feature requirements.

## Architecture

### Solution Structure (avalonia.xplat template)
```
16pin-vmon/           (Core - cross-platform logic)
├── Interfaces/       IGpuProvider, IAlertAction definitions
├── Logic/            AlertEvaluator (sliding window algorithm)
├── ViewModels/       MainViewModel (MVVM data binding)
├── Views/            Avalonia AXAML UI layouts
└── Converters/       UI value converters

16pin-vmon.Desktop/   (Platform head - Windows/Linux)
└── Implementations/  NvmlWindowsProvider (NVML P/Invoke)
```

### Key Abstractions
- **`IGpuProvider`**: Hardware abstraction for GPU data. Returns `GpuReading(Voltage16Pin, Temperature, Timestamp)`.
- **`IAlertAction`**: Alert notification interface (LINE, Telegram, email, CLI commands).
- **`AlertEvaluator`**: Sliding window algorithm - triggers when threshold exceeded Y times within X seconds.

### Data Flow
1. `DispatcherTimer` (1s interval) → `IGpuProvider.GetCurrentReading()`
2. Values → `AlertEvaluator.PushValueAndCheckAlert()` → Alert state
3. Values → `ObservableCollection<float>` → LiveCharts2 visualization

## Coding Standards

### Critical Rules
- **Never access Win32/NVML APIs directly in ViewModels** - always use `IGpuProvider` interface
- **Wrap all NVML calls in try-catch** with Serilog logging
- **Implement `IDisposable`** on providers to ensure `nvmlShutdown()` is called
- **READ-ONLY operations only** - 本工具僅讀取硬體數據，嚴禁任何「寫入」操作（如降壓 Undervolt、超頻 Overclock 等修改硬體暫存器行為）

### Patterns
- **MVVM**: Use `CommunityToolkit.Mvvm` with `[ObservableProperty]` and `partial` classes
- **Async**: All I/O, logging, and hardware probing must be async
- **Naming**: `PascalCase` for classes/methods, `_camelCase` for private fields

## Hardware Probing Strategy (Three-Tier Field ID Detection)

When initializing NVML for RTX 50 series:

1. **First**: Check `gpu_mapping.json` for known `SubsystemID` → `VerifiedVoltageFieldId` mapping
2. **Second**: Dynamic probe Field IDs 150-165, accept if value in range 11.2V-12.6V
3. **Fallback**: Use GPU Total Power estimation, display warning in UI

Device identity format: `VendorID:DeviceID:SubsystemID` (e.g., `10DE:2684:1462:5170`)

## Tech Stack
- .NET 10 / C#
- Avalonia UI (MVVM)
- NVIDIA NVML v2 (P/Invoke)
- LiveCharts2 (SkiaSharp)
- Serilog (logging)
- CommunityToolkit.Mvvm

---

## Development TODO

> 本專案會在多台 PC 上開發，以下 TODO 用於追蹤進度。完成項目後請更新狀態。

### Phase 0: Legal & Safety Compliance (法律與安全合規)
- [x] **T0-1**: 啟動警告視窗 - 首次啟動時顯示免責聲明，要求勾選「我已閱讀免責聲明，並了解監控高負載硬體之風險」後才進入主畫面
- [ ] **T0-2**: 免責聲明確認狀態持久化 - 記錄使用者已確認，下次啟動不再顯示 (依賴 T3-2)
- [x] **T0-3**: Audit Log 強化 - Serilog 完整紀錄警報觸發瞬間（時間戳、電壓值、溫度值、觸發門檻），作為「程式已盡提醒義務」之證明

### Phase 1: Core Infrastructure (基礎架構)
- [x] `IGpuProvider` interface definition
- [x] `NvmlWindowsProvider` with P/Invoke
- [x] `AlertEvaluator` sliding window logic
- [x] `MainViewModel` timer-driven monitoring
- [x] LiveCharts2 dual-chart UI layout
- [ ] **T1-1**: Dependency Injection setup in `App.axaml.cs` (use `RuntimeInformation` to inject correct provider)
- [x] **T1-2**: Demo/Mock `IGpuProvider` for testing without GPU

### Phase 2: Hardware Database (硬體資料庫)
- [ ] **T2-1**: Create `HardwareDbManager` class
- [ ] **T2-2**: Create initial `gpu_mapping.json` schema and sample data
- [ ] **T2-3**: Integrate three-tier Field ID detection into `NvmlWindowsProvider`
- [ ] **T2-4**: Add PCI Subsystem ID reading via NVML

### Phase 3: Settings System (設定系統)
- [ ] **T3-1**: Create `ISettingsService` interface
- [ ] **T3-2**: Implement JSON-based settings persistence
- [ ] **T3-3**: Cross-platform path management (`%AppData%` / `~/.config`)
- [ ] **T3-4**: Settings UI (threshold values, sampling rate)

### Phase 4: Alert Actions (警報動作)
- [ ] **T4-1**: `CommandLineAlertAction` - execute local commands
- [ ] **T4-2**: `TelegramAlertAction` - bot API integration
- [ ] **T4-3**: `LineNotifyAlertAction` - LINE Notify API
- [ ] **T4-4**: Alert action configuration UI
- [ ] **T4-5**: Debug mode - destructive actions log-only

### Phase 5: Data Export (資料匯出)
- [ ] **T5-1**: CSV export service with configurable row limit
- [ ] **T5-2**: CSV export settings UI

### Phase 6: UI/UX Enhancements (介面強化)
- [ ] **T6-1**: `IThemeService` for Windows theme awareness
- [ ] **T6-2**: Multi-language support (EN/ZH-TW/JA)
- [ ] **T6-3**: Auto-detect OS language on startup
- [ ] **T6-4**: Status bar warning for estimated voltage mode

### Phase 7: CLI Tools (命令列工具)
- [ ] **T7-1**: Standalone CLI for GPU probing and mapping export
- [ ] **T7-2**: Multi-GPU environment support

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
