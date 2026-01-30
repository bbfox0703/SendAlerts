using System;
using SendAlerts.Interfaces;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// Windows 平台的 HTTP URL ACL 註冊 (netsh)
/// </summary>
public class WindowsHttpUrlAclManager : IHttpUrlAclManager
{
    public bool TryRegisterUrlAcl(int port)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"http add urlacl url=http://+:{port}/ user=Everyone",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(10000);
            var success = process.ExitCode == 0;
            if (success)
                Log.Information("[HttpUrlAclManager] URL ACL registered successfully for port {Port}", port);
            else
                Log.Warning("[HttpUrlAclManager] URL ACL registration returned exit code {Code}", process.ExitCode);
            return success;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[HttpUrlAclManager] URL ACL registration failed (user may have cancelled UAC)");
            return false;
        }
    }
}
