using System;
using System.Threading;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// TA1-1: Single Instance Manager
/// 使用 Named Mutex 確保應用程式只有一個實例運行
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = "Local\\SendAlerts-single-instance";
    private const string PipeName = "sendalerts-pipe";

    private Mutex? _mutex;
    private bool _hasHandle;
    private bool _disposed;

    /// <summary>
    /// Named Pipe 名稱，供外部工具連線使用
    /// Windows: \\.\pipe\sendalerts-pipe
    /// </summary>
    public static string NamedPipeName => PipeName;

    /// <summary>
    /// 是否為第一個實例 (擁有 Mutex)
    /// </summary>
    public bool IsFirstInstance => _hasHandle;

    /// <summary>
    /// 嘗試取得單一實例鎖定
    /// </summary>
    /// <returns>true 如果是第一個實例，false 如果已有其他實例運行</returns>
    public bool TryAcquire()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SingleInstanceManager));

        try
        {
            // 建立或開啟已存在的 Mutex
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out bool createdNew);

            if (createdNew)
            {
                // 成功建立新的 Mutex，這是第一個實例
                _hasHandle = true;
                Log.Information("[SingleInstance] 取得單一實例鎖定，啟動為主實例");
                return true;
            }
            else
            {
                // Mutex 已存在，嘗試等待取得 (立即返回，不等待)
                try
                {
                    _hasHandle = _mutex.WaitOne(millisecondsTimeout: 0, exitContext: false);
                }
                catch (AbandonedMutexException)
                {
                    // 前一個持有者異常結束，我們現在持有這個 Mutex
                    _hasHandle = true;
                    Log.Warning("[SingleInstance] 偵測到前一實例異常結束，接管單一實例鎖定");
                    return true;
                }

                if (_hasHandle)
                {
                    Log.Information("[SingleInstance] 取得單一實例鎖定，啟動為主實例");
                    return true;
                }
                else
                {
                    Log.Information("[SingleInstance] 偵測到已有實例運行");
                    // 釋放 Mutex 參考，因為我們沒有取得它
                    _mutex.Dispose();
                    _mutex = null;
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SingleInstance] 取得單一實例鎖定時發生錯誤，將以無鎖模式繼續啟動");
            // Mutex 取得失敗不應阻止程式啟動
            _hasHandle = false;
            return true;
        }
    }

    /// <summary>
    /// 釋放單一實例鎖定
    /// </summary>
    public void Release()
    {
        if (_disposed) return;

        if (_mutex != null && _hasHandle)
        {
            try
            {
                _mutex.ReleaseMutex();
                Log.Information("[SingleInstance] 已釋放單一實例鎖定");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SingleInstance] 釋放 Mutex 時發生錯誤");
            }
            _hasHandle = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Release();

        if (_mutex != null)
        {
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
