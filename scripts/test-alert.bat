@echo off
REM TC2-2: Quick test script for 16pin-vmon Alert Center
REM Usage: test-alert.bat [GroupName] [Message]
REM Example: test-alert.bat Critical "Test alert!"

setlocal

set GROUP=%~1
set MESSAGE=%~2

if "%GROUP%"=="" set GROUP=Default
if "%MESSAGE%"=="" set MESSAGE=Test alert from batch script

echo [test-alert] Sending alert to 16pin-vmon...
echo [test-alert] Group: %GROUP%
echo [test-alert] Message: %MESSAGE%

powershell -NoProfile -Command ^
    "$msg = '{\"GroupName\":\"%GROUP%\",\"CustomMessage\":\"%MESSAGE%\"}'; " ^
    "$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', '16pin-vmon-alert', 'Out'); " ^
    "try { $pipe.Connect(3000); " ^
    "$writer = New-Object System.IO.StreamWriter($pipe); " ^
    "$writer.Write($msg); $writer.Flush(); $pipe.Close(); " ^
    "Write-Host '[test-alert] Success!' -ForegroundColor Green " ^
    "} catch { Write-Host '[test-alert] Failed:' $_.Exception.Message -ForegroundColor Red; exit 1 }"

endlocal
