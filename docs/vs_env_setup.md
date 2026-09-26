VS 2026 insider:
dotnet new install Avalonia.Templates

### 專案來源
專案最初以 `dotnet new avalonia.xplat` 範本建立。專案已存在，不要再執行該指令（`--force` 會覆蓋現有檔案）。

### NuGet 套件
所有套件版本集中在 `Directory.Packages.props` (Central Package Management) 管理，不需要手動 `dotnet add package`。
圖表使用 `ScottPlot.Avalonia`（由 `SendAlerts` Core 專案引用）。
