using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Core.Interfaces;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.ViewModels;

/// <summary>
/// TC1-1: MainViewModel - 純顯示模式 (Display-Only)
/// 專案轉型後，本 ViewModel 僅負責顯示硬體數據，不再主動觸發警報。
/// 警報功能已移至 Alert Center (透過 Named Pipe 由外部工具觸發)。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private IGpuProvider _gpuProvider;
    private readonly DispatcherTimer _timer;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    #region Localized Strings
    public string Loc_Settings => _loc["Settings"];
    public string Loc_AlertActions => _loc["Main_AlertActions"];
    public string Loc_AlertGroups => _loc["Main_AlertGroups"];
    public string Loc_DisplayModeHint => _loc["Main_DisplayOnly"];
    public string Loc_RecentAlerts => _loc["Main_RecentAlerts"];
    public string Loc_Log => _loc["Main_Log"];
    #endregion

    // --- 介面綁定屬性 ---
    [ObservableProperty] private string _gpuName = "正在偵測...";
    [ObservableProperty] private float _currentUtilization;
    [ObservableProperty] private float _currentTemperature;
    [ObservableProperty] private float _currentPower;
    [ObservableProperty] private float _powerLimit;
    [ObservableProperty] private string _powerLimitDisplay = "";

    // --- TDP 定時重測 ---
    private int _tdpRefreshCounter;
    private const int TdpRefreshIntervalSeconds = 600; // 10 分鐘

    // --- 動態標籤 (支援 GPU/CPU 模式切換) ---
    [ObservableProperty] private string _primaryMetricLabel = "GPU Utilization";
    [ObservableProperty] private string _temperatureLabel = "GPU Temperature";
    [ObservableProperty] private string _temperatureUnit = "°C";
    [ObservableProperty] private string _secondaryMetricLabel = "Power Usage";
    [ObservableProperty] private string _secondaryMetricUnit = "W";
    [ObservableProperty] private bool _isGpuMode = true;

    // --- Provider 切換 ---
    [ObservableProperty] private string _currentProviderName = "";
    [ObservableProperty] private string _switchProviderTooltip = "";
    [ObservableProperty] private bool _canSwitchProvider;

    // --- TB3-2: 狀態列屬性 ---
    [ObservableProperty] private string _statusText = "System Ready";
    [ObservableProperty] private bool _isPipeServerRunning;

    // --- 平均值摘要 ---
    [ObservableProperty] private string _avgSummary = "";

    // --- Power 圖表 Y 軸上限 (供 View 使用) ---
    [ObservableProperty] private double _powerChartYMax = 600;
    private double _cachedGpuPowerYMax;

    // --- 圖表固定 30 分鐘 ---
    private const int HistoryDurationSeconds = 1800;
    private int _maxPoints;

    // --- 環形 buffer (ScottPlot 資料) ---
    private double[] _utilizationBuffer = Array.Empty<double>();
    private double[] _temperatureBuffer = Array.Empty<double>();
    private double[] _powerBuffer = Array.Empty<double>();
    private int _bufferIndex;
    private int _bufferCount;

    // --- Running sum for averages (避免 LINQ) ---
    private double _utilizationSum;
    private double _temperatureSum;
    private double _powerSum;

    /// <summary>
    /// 通知 View 刷新圖表
    /// </summary>
    public event Action? ChartDataUpdated;
    public event Action? ChartDataCleared;

    // --- TB3-3: 警報歷史 ---
    public ObservableCollection<AlertHistoryItem> AlertHistoryItems { get; } = new();

    public MainViewModel(IGpuProvider gpuProvider)
    {
        _gpuProvider = gpuProvider;
        GpuName = _gpuProvider.GetGpuName();
        PowerLimit = _gpuProvider.PowerLimit;

        // 初始化動態標籤 (支援 GPU/CPU 模式)
        ApplyProviderLabels();

        // 根據 GPU TDP 設定 Power 圖表 Y 軸上限
        UpdatePowerChartYMax();

        // Provider 切換
        CanSwitchProvider = ServiceLocator.AvailableProviders.Count > 1;
        CurrentProviderName = GetProviderDisplayName(_gpuProvider);
        UpdateSwitchTooltip();

        // 初始化 buffer
        var samplingInterval = ServiceLocator.SettingsService?.Load().SamplingIntervalSeconds ?? 1;
        _maxPoints = HistoryDurationSeconds / Math.Max(samplingInterval, 1);
        InitializeBuffers();

        // 設定定時器 (純讀取顯示，不觸發警報)
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(samplingInterval) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        // TB3-2/TB3-3: 訂閱狀態更新事件
        InitializeStatusSubscriptions();

        Log.Information("SendAlerts 監控啟動 (Display-Only Mode): {GpuName} | Mode: {Mode} | PowerLimit: {PowerLimit:F1}{Unit}",
            GpuName, _gpuProvider.Mode, PowerLimit, SecondaryMetricUnit);
    }

    private void InitializeBuffers()
    {
        _utilizationBuffer = new double[_maxPoints];
        _temperatureBuffer = new double[_maxPoints];
        _powerBuffer = new double[_maxPoints];
        _bufferIndex = 0;
        _bufferCount = 0;
        _utilizationSum = 0;
        _temperatureSum = 0;
        _powerSum = 0;
    }

    /// <summary>
    /// 取得 buffer 中有效資料的 Span (供 View code-behind 使用)
    /// 回傳順序為時間順序 (最舊→最新)
    /// </summary>
    public double[] GetBufferSnapshot(int chartIndex)
    {
        var source = chartIndex switch
        {
            0 => _utilizationBuffer,
            1 => _temperatureBuffer,
            2 => _powerBuffer,
            _ => _utilizationBuffer
        };

        var result = new double[_bufferCount];
        if (_bufferCount < _maxPoints)
        {
            // buffer 尚未滿，資料從 0 開始
            Array.Copy(source, 0, result, 0, _bufferCount);
        }
        else
        {
            // buffer 已滿，需環繞拷貝
            var tailLen = _maxPoints - _bufferIndex;
            Array.Copy(source, _bufferIndex, result, 0, tailLen);
            Array.Copy(source, 0, result, tailLen, _bufferIndex);
        }
        return result;
    }

    public int BufferCount => _bufferCount;

    /// <summary>
    /// 更新取樣間隔 (設定變更後呼叫)
    /// </summary>
    public void UpdateSamplingInterval(int seconds)
    {
        seconds = Math.Clamp(seconds, AppConstants.SamplingIntervalMin, AppConstants.SamplingIntervalMax);
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        Log.Information("取樣間隔已更新: {Seconds} 秒", seconds);
    }

    /// <summary>
    /// TB3-2/TB3-3: 初始化狀態訂閱
    /// </summary>
    private void InitializeStatusSubscriptions()
    {
        // TB3-2: 訂閱 Pipe 狀態變更
        ServiceLocator.PipeStatusChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsPipeServerRunning = ServiceLocator.IsPipeServerRunning;
                UpdateStatusText();
            });
        };

        // 初始化 Pipe 狀態
        IsPipeServerRunning = ServiceLocator.IsPipeServerRunning;
        UpdateStatusText();

        // TB3-3: 訂閱警報歷史變更
        ServiceLocator.AlertHistoryChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(RefreshAlertHistory);
        };

        // 載入現有歷史
        RefreshAlertHistory();
    }

    /// <summary>
    /// TB3-2: 更新狀態列文字
    /// </summary>
    private void UpdateStatusText()
    {
        if (IsPipeServerRunning)
        {
            StatusText = "Alert Center Ready - Listening for alerts";
        }
        else
        {
            StatusText = "Display Only Mode";
        }
    }

    /// <summary>
    /// TB3-3: 刷新警報歷史清單
    /// </summary>
    private void RefreshAlertHistory()
    {
        AlertHistoryItems.Clear();
        foreach (var item in ServiceLocator.AlertHistory)
        {
            AlertHistoryItems.Add(item);
        }
    }

    private void UpdatePowerChartYMax()
    {
        if (IsGpuMode)
        {
            if (_cachedGpuPowerYMax <= 0)
                _cachedGpuPowerYMax = GpuTdpLookup.GetChartYMax(GpuName, PowerLimit);
            PowerChartYMax = _cachedGpuPowerYMax;
        }
        else
        {
            // CPU/Network 模式: 初始 100 KB/s，之後動態調整
            PowerChartYMax = 100;
        }
    }

    private void ApplyProviderLabels()
    {
        PrimaryMetricLabel = _gpuProvider.PrimaryMetricLabel;
        TemperatureLabel = _gpuProvider.TemperatureLabel;
        TemperatureUnit = _gpuProvider.TemperatureUnit;
        SecondaryMetricLabel = _gpuProvider.SecondaryMetricLabel;
        SecondaryMetricUnit = _gpuProvider.SecondaryMetricUnit;
        IsGpuMode = _gpuProvider.Mode == HardwareMode.Gpu;

        PowerLimitDisplay = IsGpuMode
            ? $"(TDP: {PowerLimit:F0}W)"
            : $"(Mem: {PowerLimit:F0}G)";
    }

    private static string GetProviderDisplayName(IGpuProvider provider)
    {
        var typeName = provider.GetType().Name;
        return provider.Mode switch
        {
            HardwareMode.Gpu when typeName.Contains("NvApi") => "GPU (NvAPI)",
            HardwareMode.Gpu when typeName.Contains("Nvml") => "GPU (NVML)",
            HardwareMode.Gpu when typeName.Contains("Demo") => "Demo",
            HardwareMode.CpuNetwork => "CPU / Memory / Network",
            _ => typeName
        };
    }

    private void UpdateSwitchTooltip()
    {
        var providers = ServiceLocator.AvailableProviders;
        if (providers.Count <= 1)
        {
            SwitchProviderTooltip = _loc["SwitchProvider_NoOther"] ?? "No other provider available";
            return;
        }

        var currentIndex = providers.IndexOf(_gpuProvider);
        var nextIndex = (currentIndex + 1) % providers.Count;
        var nextName = GetProviderDisplayName(providers[nextIndex]);
        SwitchProviderTooltip = string.Format(
            _loc["SwitchProvider_Tooltip"] ?? "Click to switch to: {0}",
            nextName);
    }

    [RelayCommand]
    private void SwitchProvider()
    {
        var providers = ServiceLocator.AvailableProviders;
        if (providers.Count <= 1) return;

        var currentIndex = providers.IndexOf(_gpuProvider);
        var nextIndex = (currentIndex + 1) % providers.Count;
        _gpuProvider = providers[nextIndex];
        ServiceLocator.GpuProvider = _gpuProvider;

        // 更新顯示資訊
        GpuName = _gpuProvider.GetGpuName();
        PowerLimit = _gpuProvider.PowerLimit;
        CurrentProviderName = GetProviderDisplayName(_gpuProvider);
        ApplyProviderLabels();
        UpdateSwitchTooltip();

        // 重算 Power Y 軸上限
        UpdatePowerChartYMax();

        // 清空 buffer
        _timer.Stop();
        InitializeBuffers();
        ChartDataCleared?.Invoke();
        _timer.Start();

        Log.Information("已切換 Provider: {Name} ({Type})", CurrentProviderName, _gpuProvider.GetType().Name);
    }

    /// <summary>
    /// TC1-1: 定時器回呼 - 純讀取顯示，不觸發警報
    /// </summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            // A. 讀取數據
            var reading = _gpuProvider.GetCurrentReading();
            CurrentUtilization = reading.GpuUtilization;
            CurrentTemperature = reading.Temperature;
            CurrentPower = reading.PowerUsage;

            // B. 寫入環形 buffer
            // 若 buffer 已滿，先減去即將被覆蓋的舊值
            if (_bufferCount >= _maxPoints)
            {
                _utilizationSum -= _utilizationBuffer[_bufferIndex];
                _temperatureSum -= _temperatureBuffer[_bufferIndex];
                _powerSum -= _powerBuffer[_bufferIndex];
            }

            _utilizationBuffer[_bufferIndex] = CurrentUtilization;
            _temperatureBuffer[_bufferIndex] = CurrentTemperature;
            _powerBuffer[_bufferIndex] = CurrentPower;

            _utilizationSum += CurrentUtilization;
            _temperatureSum += CurrentTemperature;
            _powerSum += CurrentPower;

            _bufferIndex = (_bufferIndex + 1) % _maxPoints;
            if (_bufferCount < _maxPoints) _bufferCount++;

            // C. 更新平均值摘要
            if (_bufferCount > 0)
            {
                var avgUtil = _utilizationSum / _bufferCount;
                var avgTemp = _temperatureSum / _bufferCount;
                var avgPower = _powerSum / _bufferCount;
                AvgSummary = $"Avg: {avgUtil:F0}% | {avgTemp:F1}{TemperatureUnit} | {avgPower:F1}{SecondaryMetricUnit}";
            }

            // D. 通知 View 刷新圖表
            ChartDataUpdated?.Invoke();

            // E. TDP 定時重測 (僅 GPU 模式)
            if (IsGpuMode)
            {
                var samplingInterval = _timer.Interval.TotalSeconds;
                _tdpRefreshCounter++;
                if (_tdpRefreshCounter >= TdpRefreshIntervalSeconds / samplingInterval)
                {
                    _tdpRefreshCounter = 0;
                    _gpuProvider.RefreshPowerLimit();
                    var newLimit = _gpuProvider.PowerLimit;
                    if (Math.Abs(newLimit - PowerLimit) > 0.1f)
                    {
                        PowerLimit = newLimit;
                        PowerLimitDisplay = $"(TDP: {PowerLimit:F0}W)";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "數據讀取過程中發生錯誤");
        }
    }

}
