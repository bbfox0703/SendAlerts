<#
.SYNOPSIS
    Send alert message to 16pin-vmon Alert Center via Named Pipe.

.DESCRIPTION
    TC2-2: PowerShell script to send alert messages to 16pin-vmon.
    Can be used with HWiNFO64 or other monitoring tools.

.PARAMETER GroupName
    The name of the Alert Group to trigger (e.g., "Critical", "Warning", "Info").

.PARAMETER Message
    Optional custom message. Supports HWiNFO64 variables when called from HWiNFO64.

.PARAMETER PipeName
    Named Pipe name (default: 16pin-vmon-alert).

.PARAMETER Timeout
    Connection timeout in milliseconds (default: 3000).

.EXAMPLE
    .\send-alert.ps1 -GroupName "Critical" -Message "GPU Temperature: 95°C"

.EXAMPLE
    .\send-alert.ps1 -GroupName "Warning"

.EXAMPLE
    # From HWiNFO64 Action:
    powershell.exe -ExecutionPolicy Bypass -File "C:\scripts\send-alert.ps1" -GroupName "Critical" -Message "GPU: <#GPU Core Temp#>°C"

.NOTES
    Author: 16pin-vmon
    Version: 1.0.0
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$GroupName,

    [Parameter(Mandatory=$false)]
    [string]$Message = "",

    [Parameter(Mandatory=$false)]
    [string]$PipeName = "16pin-vmon-alert",

    [Parameter(Mandatory=$false)]
    [int]$Timeout = 3000
)

# Build JSON message
$jsonObject = @{
    GroupName = $GroupName
}

if ($Message -ne "") {
    $jsonObject.CustomMessage = $Message
}

$jsonMessage = $jsonObject | ConvertTo-Json -Compress

Write-Host "[send-alert] Sending to pipe: \\.\pipe\$PipeName"
Write-Host "[send-alert] Message: $jsonMessage"

try {
    # Create Named Pipe client
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $PipeName, [System.IO.Pipes.PipeDirection]::Out)

    # Connect with timeout
    $pipe.Connect($Timeout)

    # Write message
    $writer = New-Object System.IO.StreamWriter($pipe)
    $writer.Write($jsonMessage)
    $writer.Flush()

    # Close connection
    $writer.Close()
    $pipe.Close()

    Write-Host "[send-alert] Message sent successfully!" -ForegroundColor Green
    exit 0
}
catch [System.TimeoutException] {
    Write-Host "[send-alert] ERROR: Connection timeout. Is 16pin-vmon running?" -ForegroundColor Red
    exit 1
}
catch {
    Write-Host "[send-alert] ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
