VS 2026 insider:
dotnet new install Avalonia.Templates

### 在 D:\Github\SendAlerts 目錄下執行這指令
dotnet new avalonia.xplat --output . --force

### 在 D:\Github\SendAlerts 目錄下執行這兩條指令
#### 安裝到 Core 專案 (負責 UI 邏輯)
dotnet add SendAlerts package LiveChartsCore.SkiaSharpView.Avalonia --version 2.0.0-rc6.1

#### 安裝到 Desktop 專案 (負責 渲染)
dotnet add SendAlerts.Desktop package LiveChartsCore.SkiaSharpView.Avalonia --version 2.0.0-rc6.1