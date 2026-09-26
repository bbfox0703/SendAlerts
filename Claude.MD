# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands
- **Build**: `dotnet build`
- **Run**: `dotnet run --project SendAlerts.Desktop`
- **Clean**: `dotnet clean`

## Project Overview

**硬體警報中繼站 (Hardware Alert Relay Station)**

本專案是「硬體警報中繼站」，以 HWiNFO64 等專業工具作為資料源：
- 核心功能：接收外部觸發並執行多管道警報 (Alert Center)
- UI 顯示：GPU 功耗、核心電壓與溫度僅供參考，本程式不主動發送警訊（GPU/NVML 相關程式碼源自專案早期的 GPU 監控工具定位）
- 通訊方式：Single Instance 模式；外部指令經由 Named Pipe 或 HTTP API (`POST /api/send`, `X-API-Key`) 進入，統一交給 `AlertService` 處理

Built with Avalonia UI. Windows-only (win-x64, `Avalonia.Win32` backend); the Core project stays platform-neutral behind interfaces.

## Architecture

### Alert 多層級架構 (Three-Tier Alert System)

```
┌─────────────────────────────────────────────────────────────────┐
│                    Interface Tier (接口層)                       │
│  Named Pipe Server: \\.\pipe\sendalerts-pipe                   │
│  HTTP API:          POST http://localhost:58080/api/send       │
│  JSON Format: { "GroupName": "...", "CustomMessage": "..." }    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Group Tier (群組層)                           │
│  AlertGroup: { Name, MessageTemplate, List<ActionReference> }   │
│  Example: "Critical" → [Discord_Admin, Telegram_All, Email_IT]   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Action Tier (動作層)                          │
│  IAlertAction implementations with unique InstanceId            │
│  Example: Discord_User1, Discord_Admin1, Telegram_All, Email_IT │
└─────────────────────────────────────────────────────────────────┘
```

### Solution Structure
```
SendAlerts/            (Core - platform-neutral logic, namespace: SendAlerts;
│                       referenced by Desktop, Cli, Watchdog, Tests)
├── Interfaces/        IAlertAction, IGpuProvider, ISensorDataProvider, IHwinfoProvider,
│                      IStartupManager, IHttpUrlAclManager
├── Models/            AlertActionConfig, AlertGroup, PipeMessage, chart/sensor models
├── Services/          AlertService, AlertActionFactory, *AlertAction, HttpApiServer,
│                      JsonSettingsService, SettingsMigrator, ServiceLocator
├── Implementations/   DemoGpuProvider (mock provider for running without a GPU)
├── ViewModels/        MainViewModel, AlertActionsViewModel, AlertGroupsViewModel, dialog VMs
├── Views/             MainView (4 ScottPlot charts), AlertActionsWindow, AlertGroupsWindow, dialogs
└── Converters/        UI value converters

SendAlerts.Desktop/    (Windows platform head, namespace: SendAlerts.Desktop)
├── Implementations/   NamedPipeServer, SingleInstanceManager, Nvml/NvApi/CpuNetwork providers,
│                      LhmSensorProvider, HwinfoSharedMemoryReader, StartupManager,
│                      TrayIconManager, WindowsHttpUrlAclManager
└── Program.cs         Single instance mutex + Named Pipe init + BuildAvaloniaApp()

SendAlerts.Cli/        SendAlerts-cli.exe: send / list / test
SendAlerts.Watchdog/   Separate process that watches the main app (own mutex, pipe, tray icon)
SendAlerts.Tests/      xUnit v3 tests for Core
tools/NvI2CScanner/    Standalone read-only NVIDIA I2C bus scanner
```

**Note**: Namespace is `SendAlerts` (previously was `_16pin_vmon` with underscores)

### Key Abstractions
- **`IAlertAction`**: Alert action interface with `InstanceId` for multi-instance support
- **`AlertGroup`**: Groups multiple actions with shared message template
- **`NamedPipeServer`**: Listens for external JSON commands
- **`AlertService`**: Orchestrates action execution by group
- **`IGpuProvider`**: Hardware abstraction for GPU data (display only)

### Data Flow
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
│ IAlertAction[] (Discord, Telegram, Email...) │
└───────────────────────────────────────────┘
```

### HWiNFO64 Integration Example
```
; HWiNFO64 Alert Configuration
; Action: Run Program
; Program: powershell.exe
; Arguments: -Command "'{\"GroupName\":\"Critical\",\"CustomMessage\":\"GPU Temp: <#GPU Core Temp#>°C\"}' | Out-File -FilePath \\.\pipe\sendalerts-pipe -Encoding utf8"
```

## Coding Standards

### Critical Rules
- **Single Instance**: Use Mutex to ensure only one instance runs
- **Named Pipe Security**: Validate JSON schema before processing
- **Async everywhere**: All I/O (pipe, HTTP, file) and alert actions must be async
- **Hardware is read-only** - 本工具僅讀取硬體數據: never call an API that changes hardware state (clocks, power limits, voltages, fan curves, I2C/SMBus writes), because this tool runs beside high-load hardware and a bad write can damage it. Ordinary app writes — settings, logs, the startup registry key, the HTTP URL ACL — are fine.
- **Platform Abstraction**: Any system/OS-dependent call (P/Invoke, Registry, OS commands) goes through an interface in `SendAlerts/Interfaces/`, with the implementation in `SendAlerts.Desktop/Implementations/`. The Core project (`SendAlerts/`) contains no platform-specific code, because Cli, Watchdog and Tests reference it too.

### Patterns
- **MVVM**: Use `CommunityToolkit.Mvvm` with `[ObservableProperty]` and `partial` classes
- **Naming**: `PascalCase` for classes/methods, `_camelCase` for private fields
- **Log messages**: New `Log.*()` messages, and any you edit, are English only (no Chinese/Japanese/other non-ASCII text); use structured logging parameters for variable data. Many existing messages are still Chinese — leave lines you aren't otherwise changing as they are.

## Known Constraints

### Avalonia 12 + ScottPlot.Avalonia 5.1.59
- `Directory.Packages.props` pins all Avalonia.* packages to `[12.1.0, 13.0.0)` (the range keeps
  the NuGet GUI from offering 13.x) and `ScottPlot.Avalonia 5.1.59`. Do not downgrade either:
  5.1.59 requires Avalonia >= 12, and ScottPlot 5.1.58 and earlier throw `TypeLoadException` in
  `AvaPlot` rendering under Avalonia 12 (fully transparent/black window).
- `BuildAvaloniaApp()` in **both** `SendAlerts.Desktop` and `SendAlerts.Watchdog` must keep
  `.UseHarfBuzz()`. Without it `AppBuilder.Setup()` throws `No text shaping system configured` at
  startup — the build succeeds, and the process dies before any window or tray icon appears.
- After any Avalonia or ScottPlot bump, run the app, not just the build: smoke-test all 4 `AvaPlot`
  charts + the custom `GradientFillPlottable` + the crosshair/tooltip layer.
- The full 11 → 12 migration record (Diagnostics → DiagnosticsSupport, XAML renames) is in commit
  `ec27b9e` and the comments in `Directory.Packages.props`.

### Native AOT is not supported (ship JIT + single-file)
Desktop ships `PublishSingleFile` + JIT; Cli and Watchdog ship `PublishTrimmed` with
`TrimMode=partial`. No project sets `PublishAot=true`, and none should: reflection-based JSON is
enabled on purpose, LibreHardwareMonitorLib and DiskInfoToolkit are not AOT-annotated, and
ScottPlot's AOT compatibility is unverified.
The blocker and gap list for a future AOT attempt is in `docs/native-aot.md`.

## Tech Stack
- .NET 10 / C#, win-x64
- Avalonia UI 12 (MVVM) + CommunityToolkit.Mvvm
- ScottPlot.Avalonia (charts)
- LibreHardwareMonitorLib, NVML / NvAPI (P/Invoke), HWiNFO shared memory (sensor data)
- System.IO.Pipes (Named Pipe IPC), HttpListener (HTTP API)
- System.CommandLine (CLI)
- Serilog (logging)
- xUnit v3 (tests)

## Development TODO

Task tracking — phases, open items, and the completion log — lives in `docs/TODO.md`; code
comments reference its task IDs (e.g. `TA2-6`). This project is developed on several PCs, so
update that file when you finish a tracked item.
