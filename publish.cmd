@echo off
set OUTDIR=.\publish
if exist "%OUTDIR%" rd /s /q "%OUTDIR%"
md "%OUTDIR%"
dotnet publish "SendAlerts.Desktop\SendAlerts.Desktop.csproj" -c Release -r win-x64 -o "%OUTDIR%"
dotnet publish "SendAlerts.Cli\SendAlerts.Cli.csproj" -c Release -r win-x64 -o "%OUTDIR%"
dotnet publish "SendAlerts.Watchdog\SendAlerts.Watchdog.csproj" -c Release -r win-x64 -o "%OUTDIR%"
