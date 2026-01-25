# CLAUDE.md for 16pin-vmon

## Build & Test Commands
- **Build Solution**: `dotnet build`
- **Run Desktop Project**: `dotnet run --project 16pin-vmon.Desktop`
- **Clean Solution**: `dotnet clean`

## Project Context
- **Spec Reference**: Always refer to `docs/Spec.md` for hardware logic and alert window algorithms.
- **Hardware Target**: RTX 50 Series (Blackwell), focus on 16-pin voltage rail (Field ID 150-165).
- **Architecture**: Avalonia xplat (MVVM). UI is in `.Core`, Platform-specific P/Invoke is in `.Desktop`.

## Coding Standards & Patterns
- **Abstraction Layer**: DO NOT use Win32 or NVML APIs directly in ViewModels. Use `IGpuProvider` interface.
- **MVVM**: Use `CommunityToolkit.Mvvm` with `[ObservableProperty]` and `partial` classes.
- **Naming**: Use `PascalCase` for classes/methods, `_camelCase` for private fields.
- **Async**: Use `Async/Await` for all I/O, Logging, and Hardware Probing.
- **Error Handling**: Wrap all NVML calls in `try-catch` with `Serilog` logging.

## Database Management
- Hardware mappings are stored in `gpu_mapping.json`.
- Use `HardwareDbManager` for lookup before attempting dynamic probing.