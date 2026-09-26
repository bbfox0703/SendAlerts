# Native AOT - blocker and gap list

This project ships **`PublishSingleFile` + JIT** (Desktop), and `PublishTrimmed`
`partial` (CLI/Watchdog). **No project sets `PublishAot=true`** and none should
until the blockers below are cleared. There are no *active* AOT errors because AOT
is never invoked — the items here are the gap list for *if* AOT is ever pursued.
Doing any of them before the hard blockers are cleared is wasted effort.

**Hard blockers (architectural — must clear first, in order):**
1. **ScottPlot.Avalonia** (5.1.59, on Avalonia 12.1.0) is reflection-heavy and its
   AOT compatibility is unverified.
2. **Reflection-based JSON is enabled on purpose**
   (`JsonSerializerIsReflectionEnabledByDefault=true` in `SendAlerts.Desktop`
   and `SendAlerts.Watchdog`). Most `JsonSerializer.Serialize/Deserialize<T>`
   calls use the generic reflection overload, not a `JsonSerializerContext`
   (`JsonSettingsService`, `HardwareDbManager`, `PipeMessage`, `HttpApiServer`,
   `GpuTdpLookup`, `Discord/Telegram` actions). Only `SendAlerts.Cli` uses a
   source-gen context (`CliJsonContext`). AOT needs source-gen everywhere.
3. **`LibreHardwareMonitorLib` + `DiskInfoToolkit`** are reflection/WMI/P-Invoke
   heavy and not AOT-annotated.

**Code-pattern gaps (only relevant once the blockers above are cleared):**
- `ViewLocator` uses `Type.GetType` + `Activator.CreateInstance` (already marked
  `[RequiresUnreferencedCode]`) — replace with a type→view switch expression.
- Cross-assembly reflection call into `SendAlerts.Desktop.Program.InitializeTrayIconWithWindow`
  from `MainWindow.axaml.cs` (`GetType`/`GetMethod`/`Invoke`) — trimmer drops the target.
- `Assembly.GetExecutingAssembly()` (×4: Watchdog `Program.cs`, `DiagnosticsReport`,
  `GpuTdpLookup`, `MainViewModel`) → must be `GetEntryAssembly()` under single-file/AOT.
- `[DllImport]` (NvAPI/NVML/CpuNetwork providers) → `[LibraryImport]` source-gen marshalling.
- `BuildAvaloniaApp()` already uses explicit `.UseWin32().UseSkia().UseHarfBuzz()`, but
  lists `Win32CompositionMode.WinUIComposition` first — AOT needs `RedirectionSurface`-only
  composition.

Reference: `CE-Handwire-Private/docs/aot-pitfalls.md` (private repo, not part of this
checkout — the Avalonia 12 + Native AOT landmine catalogue this gap list was checked against).
