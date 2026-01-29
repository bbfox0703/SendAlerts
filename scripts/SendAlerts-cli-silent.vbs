' SendAlerts-cli-silent.vbs
' HWiNFO64 用無視窗 wrapper - 避免主控台視窗閃現
'
' 使用方式 (HWiNFO Alert Action):
'   Program:   wscript.exe
'   Arguments: "C:\path\to\SendAlerts-cli-silent.vbs" send -g Critical -m "GPU Temp: 95C"
'
' 或直接：
'   Program:   wscript.exe
'   Arguments: "SendAlerts-cli-silent.vbs" send -g Critical -m "GPU Temp: <#value#>"

Dim objShell, scriptDir, cliPath, args, i

Set objShell = CreateObject("WScript.Shell")

' 取得本腳本所在目錄
scriptDir = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)

' SendAlerts-cli.exe 路徑 (同目錄或上層 bin 目錄)
cliPath = scriptDir & "\..\SendAlerts.Desktop\bin\Debug\net10.0\SendAlerts-cli.exe"
If Not CreateObject("Scripting.FileSystemObject").FileExists(cliPath) Then
    ' 嘗試同目錄
    cliPath = scriptDir & "\SendAlerts-cli.exe"
End If
If Not CreateObject("Scripting.FileSystemObject").FileExists(cliPath) Then
    ' 嘗試 publish 目錄
    cliPath = scriptDir & "\..\publish\SendAlerts-cli.exe"
End If

' 組合命令列參數 (跳過第一個參數，即腳本路徑本身)
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
