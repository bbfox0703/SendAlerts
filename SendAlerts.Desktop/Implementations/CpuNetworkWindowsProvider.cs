using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using SendAlerts.Core.Interfaces;
using LibreHardwareMonitor.Hardware;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// Windows 平台 CPU/Network 資料提供者
/// 當 NVIDIA GPU 不可用時的 fallback 方案
/// - CPU 使用率 via LibreHardwareMonitor
/// - 記憶體使用率 via GlobalMemoryStatusEx (取代 CPU 溫度)
/// - 網路 I/O via System.Net.NetworkInformation
/// </summary>
public class CpuNetworkWindowsProvider : IGpuProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private readonly Computer _computer;
    private IHardware? _cpu;
    private ISensor? _cpuLoadSensor;

    // 網路流量追蹤
    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTime _prevTimestamp;
    private float _currentThroughputKBps;

    private bool _isInitialized;
    private string _cpuName = "Unknown CPU";
    private float _totalMemoryGB;

    public bool IsAvailable => _isInitialized;
    public float PowerLimit => _totalMemoryGB; // 總記憶體容量 (GB)

    // 硬體模式與動態標籤 (CPU/Network 模式)
    public HardwareMode Mode => HardwareMode.CpuNetwork;
    public string PrimaryMetricLabel => "CPU Utilization";
    public string TemperatureLabel => "Memory Usage";
    public string TemperatureUnit => "%";
    public string SecondaryMetricLabel => "Network I/O";
    public string SecondaryMetricUnit => "KB/s";

    public CpuNetworkWindowsProvider()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsNetworkEnabled = false // 使用 System.Net.NetworkInformation 代替
        };

        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Log.Information("[CPU/Network] 開始初始化...");

            _computer.Open();

            // 尋找 CPU
            _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (_cpu == null)
            {
                Log.Error("[CPU/Network] 找不到 CPU");
                return;
            }

            _cpuName = _cpu.Name;
            _cpu.Update();

            // 尋找 CPU Load Total 感測器
            _cpuLoadSensor = _cpu.Sensors
                .FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "CPU Total");

            // 取得總記憶體容量
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                _totalMemoryGB = (float)Math.Ceiling(memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
                Log.Information("[CPU/Network] 總記憶體: {TotalGB:F1} GB", _totalMemoryGB);
            }

            // 初始化網路流量基準
            InitializeNetworkBaseline();

            _isInitialized = true;
            Log.Information("[CPU/Network] 初始化完成 | CPU: {CpuName} | Memory: {MemGB:F1} GB",
                _cpuName, _totalMemoryGB);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CPU/Network] 初始化時發生錯誤");
            _isInitialized = false;
        }
    }

    private void InitializeNetworkBaseline()
    {
        try
        {
            var (received, sent) = GetTotalNetworkBytes();
            _prevBytesReceived = received;
            _prevBytesSent = sent;
            _prevTimestamp = DateTime.Now;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CPU/Network] 網路基準初始化失敗");
        }
    }

    private (long received, long sent) GetTotalNetworkBytes()
    {
        long totalReceived = 0;
        long totalSent = 0;

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            foreach (var ni in interfaces)
            {
                var stats = ni.GetIPv4Statistics();
                totalReceived += stats.BytesReceived;
                totalSent += stats.BytesSent;
            }
        }
        catch
        {
            // 忽略錯誤，返回 0
        }

        return (totalReceived, totalSent);
    }

    public string GetGpuName() => _isInitialized ? $"{_cpuName} (CPU Mode)" : "N/A";

    public GpuReading GetCurrentReading()
    {
        if (!_isInitialized)
            return new GpuReading(0, 0, 0, DateTime.Now);

        // 更新硬體數據
        _cpu?.Update();

        // 讀取 CPU 使用率
        float cpuLoad = 0;
        if (_cpuLoadSensor != null && _cpuLoadSensor.Value.HasValue)
        {
            cpuLoad = _cpuLoadSensor.Value.Value;
        }

        // 讀取記憶體使用率
        float memoryUsagePercent = 0;
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            memoryUsagePercent = memStatus.dwMemoryLoad;
        }

        // 計算網路吞吐量
        float networkThroughput = CalculateNetworkThroughput();

        return new GpuReading(cpuLoad, memoryUsagePercent, networkThroughput, DateTime.Now);
    }

    private float CalculateNetworkThroughput()
    {
        try
        {
            var now = DateTime.Now;
            var elapsed = (now - _prevTimestamp).TotalSeconds;

            if (elapsed < 0.1)
            {
                return _currentThroughputKBps; // 避免除以零或太頻繁計算
            }

            var (currentReceived, currentSent) = GetTotalNetworkBytes();

            long deltaBytes = (currentReceived - _prevBytesReceived) + (currentSent - _prevBytesSent);

            // 處理計數器溢位
            if (deltaBytes < 0)
            {
                deltaBytes = 0;
            }

            _currentThroughputKBps = (float)(deltaBytes / 1024.0 / elapsed);

            // 更新基準
            _prevBytesReceived = currentReceived;
            _prevBytesSent = currentSent;
            _prevTimestamp = now;

            return _currentThroughputKBps;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CPU/Network] 計算網路吞吐量時發生錯誤");
            return 0;
        }
    }

    public void Dispose()
    {
        try
        {
            _computer.Close();
            Log.Information("[CPU/Network] 已安全關閉");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[CPU/Network] 關閉時發生錯誤");
        }
    }
}
