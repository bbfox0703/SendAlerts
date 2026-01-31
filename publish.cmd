dotnet publish "D:\Github\SendAlerts\SendAlerts.Desktop\SendAlerts.Desktop.csproj" -c Release -r win-x64
dotnet publish "D:\Github\SendAlerts\SendAlerts.Cli\SendAlerts.Cli.csproj" -c Release -r win-x64
dotnet publish "D:\Github\SendAlerts\SendAlerts.Watchdog\SendAlerts.Watchdog.csproj" -c Release -r win-x64 -o "D:\Github\SendAlerts\SendAlerts.Desktop\bin\Release\net10.0\win-x64\publish"
