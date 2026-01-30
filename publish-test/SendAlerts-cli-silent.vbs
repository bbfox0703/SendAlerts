' SendAlerts-cli-silent.vbs
' HWiNFO64 用無視窗 wrapper - 避免主控台視窗閃現
'
' 使用方式 (HWiNFO Alert Action):
'   Program:   wscript.exe
'   Arguments: "C:\path\to\SendAlerts-cli-silent.vbs" send -g Critical -m "GPU Temp: 95C"

Dim objShell, fso, scriptDir, cliPath, args, i

Set objShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

' 取得本腳本所在目錄
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)

' SendAlerts-cli.exe 路徑 (同目錄)
cliPath = scriptDir & "\SendAlerts-cli.exe"

If Not fso.FileExists(cliPath) Then
    WScript.Echo "Error: SendAlerts-cli.exe not found at: " & cliPath
    WScript.Quit 1
End If

' 組合命令列參數
args = ""
For i = 0 To WScript.Arguments.Count - 1
    If InStr(WScript.Arguments(i), " ") > 0 Then
        args = args & " """ & WScript.Arguments(i) & """"
    Else
        args = args & " " & WScript.Arguments(i)
    End If
Next

' 以隱藏視窗模式執行 (0 = vbHide)
objShell.Run """" & cliPath & """" & args, 0, False
