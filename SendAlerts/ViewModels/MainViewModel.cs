using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
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
    public string Loc_HwinfoApply => _loc["HWiNFO_Apply"];
    public string Loc_HwinfoApplyTooltip => _loc["HWiNFO_ApplyTooltip"];
    public string Loc_HwinfoFilterWatermark => _loc["HWiNFO_FilterWatermark"];
    public string Loc_HwinfoRefreshTooltip => _loc["HWiNFO_RefreshTooltip"];
    public string Loc_ChartSourceOff => _loc["Chart_SourceOff"];
    public string Loc_ChartSourceTooltip => _loc["Chart_SourceTooltip"];
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
    [ObservableProperty] private string _primaryMetricLabel = "GPU Core Utilization";
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

    // --- 版本顯示 ---
    public string VersionDisplay { get; } = GetVersionString();

    // --- Power 圖表 Y 軸上限 (供 View 使用) ---
    [ObservableProperty] private double _powerChartYMax = 50;

    // --- Network MB/s 切換 (只向上不縮回) ---
    internal bool _networkScaleMB;

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
    public event Action<int>? SamplingIntervalChanged;

    // --- Chart Source (multi-provider) ---
    [ObservableProperty] private ChartSourceType _chartSource;
    [ObservableProperty] private string _chartSourceDisplayName = "Sensor Chart";
    [ObservableProperty] private string _chartTitleText = "";

    // --- HWiNFO Chart ---
    [ObservableProperty] private bool _isHwinfoChartVisible;
    [ObservableProperty] private double _currentHwinfoValue;
    public string HwinfoValueDisplay => FormatHwinfoValue(CurrentHwinfoValue);

    partial void OnCurrentHwinfoValueChanged(double value) => OnPropertyChanged(nameof(HwinfoValueDisplay));

    /// <summary>Adaptive formatting: integer if whole number, up to 3 decimal places otherwise.</summary>
    internal static string FormatHwinfoValue(double value)
    {
        if (double.IsNaN(value)) return "--";
        if (value == Math.Floor(value)) return value.ToString("F0");
        if (Math.Abs(value * 10 - Math.Round(value * 10)) < 0.001) return value.ToString("F1");
        if (Math.Abs(value * 100 - Math.Round(value * 100)) < 0.01) return value.ToString("F2");
        return value.ToString("F3");
    }
    [ObservableProperty] private string _hwinfoChartLabel = "";
    [ObservableProperty] private string _hwinfoChartUnit = "";
    [ObservableProperty] private double _hwinfoChartYMax = 0.1;
    [ObservableProperty] private HwinfoSensorItem? _selectedHwinfoSensor; // ComboBox 選擇 (尚未套用)
    [ObservableProperty] private HwinfoSensorItem? _appliedHwinfoSensor;  // 實際繪圖的感測器
    [ObservableProperty] private string _appliedHwinfoDisplay = "";       // 目前套用的顯示名稱
    [ObservableProperty] private string _hwinfoFilterText = "";
    [ObservableProperty] private bool _isHwinfoReadable;                  // SHM 是否可讀
    public ObservableCollection<HwinfoSensorItem> HwinfoSensorItems { get; } = new();
    public ObservableCollection<HwinfoSensorItem> HwinfoFilteredItems { get; } = new();
    private readonly List<HwinfoSensorItem> _allHwinfoItems = new();

    /// <summary>通知 View 刷新 HWiNFO 圖表</summary>
    public event Action? HwinfoChartDataUpdated;
    public event Action? HwinfoChartCleared;
    /// <summary>HWiNFO SHM 不可用時通知 View 顯示訊息</summary>
    public event Action<string>? HwinfoShmNotFound;

    // HWiNFO buffer (與主 buffer 分開)
    private double[] _hwinfoBuffer = Array.Empty<double>();
    private int _hwinfoBufferIndex;
    private int _hwinfoBufferCount;
    private double _hwinfoSum;
    private bool _hwinfoUnavailableLogged;
    private int _hwinfoRetryCounter; // 啟動時等待 HWiNFO 可用的計數器
    internal double _lastHwinfoTickValue; // View 用：最新一筆寫入值 (可能是 NaN)
    private bool _hwinfoInitializing; // 初始化期間不儲存設定
    private double _hwinfoPeak; // 記錄中的最大值 (忽略 NaN)

    // --- TB3-3: 警報歷史 ---
    public ObservableCollection<AlertHistoryItem> AlertHistoryItems { get; } = new();

    public MainViewModel(IGpuProvider gpuProvider)
    {
        _gpuProvider = gpuProvider;
        GpuName = _gpuProvider.GetGpuName();
        if (GpuName is "Unknown" or "N/A")
            GpuName = "GPU Detect Error";
        PowerLimit = _gpuProvider.PowerLimit;

        // 初始化動態標籤 (支援 GPU/CPU 模式)
        ApplyProviderLabels();

        // 根據 GPU TDP 設定 Power 圖表 Y 軸上限
        UpdatePowerChartYMax();

        // Provider 切換
        CanSwitchProvider = ServiceLocator.AvailableProviders.Count > 1;
        CurrentProviderName = GetProviderDisplayName(_gpuProvider);
        UpdateSwitchTooltip();

        // 初始化 buffer (固定 1800 點，interval 僅影響時間窗)
        var samplingInterval = ServiceLocator.SettingsService?.Load().SamplingIntervalSeconds ?? 1;
        _maxPoints = HistoryDurationSeconds;
        InitializeBuffers();

        // 設定定時器 (純讀取顯示，不觸發警報)
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(samplingInterval) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        // TB3-2/TB3-3: 訂閱狀態更新事件
        InitializeStatusSubscriptions();

        // HWiNFO: 載入設定
        InitializeHwinfo();

        Log.Information("SendAlerts monitor started (Display-Only Mode): {GpuName} | Mode: {Mode} | PowerLimit: {PowerLimit:F1}{Unit}",
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

        _hwinfoBuffer = new double[_maxPoints];
        Array.Fill(_hwinfoBuffer, double.NaN);
        _hwinfoBufferIndex = 0;
        _hwinfoBufferCount = 0;
        _hwinfoSum = 0;
        _hwinfoPeak = 0;
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
            3 => _hwinfoBuffer,
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
    /// interval 變更時清空 buffer 並通知 View 重算 X 軸
    /// </summary>
    public void UpdateSamplingInterval(int seconds)
    {
        seconds = Math.Clamp(seconds, AppConstants.SamplingIntervalMin, AppConstants.SamplingIntervalMax);
        var oldInterval = (int)_timer.Interval.TotalSeconds;
        _timer.Interval = TimeSpan.FromSeconds(seconds);

        if (seconds != oldInterval)
        {
            _timer.Stop();
            InitializeBuffers();
            ChartDataCleared?.Invoke();
            SamplingIntervalChanged?.Invoke(seconds);
            _timer.Start();
            Log.Information("Sampling interval updated: {Old}s -> {New}s, charts cleared", oldInterval, seconds);
        }
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
        // 統一初始 Y 軸 = 50（GPU 和 Network 都從 50 開始，動態成長）
        PowerChartYMax = 50;
        _networkScaleMB = false;
    }

    private void ApplyProviderLabels()
    {
        PrimaryMetricLabel = _gpuProvider.PrimaryMetricLabel;
        TemperatureLabel = _gpuProvider.TemperatureLabel;
        TemperatureUnit = _gpuProvider.TemperatureUnit;
        SecondaryMetricLabel = _gpuProvider.SecondaryMetricLabel;
        SecondaryMetricUnit = _gpuProvider.SecondaryMetricUnit;
        IsGpuMode = _gpuProvider.Mode == HardwareMode.Gpu;

        if (IsGpuMode)
        {
            if (PowerLimit <= 0)
            {
                // Laptop 或舊 GPU: nvmlDeviceGetPowerManagementLimit 回傳 NOT_SUPPORTED
                var tdp = GpuTdpLookup.FindTdp(GpuName);
                PowerLimitDisplay = tdp.HasValue
                    ? $"(TDP: ~{tdp.Value}W est.)"
                    : "(TDP: N/A)";
            }
            else
            {
                PowerLimitDisplay = $"(TDP: {PowerLimit:F0}W)";
            }
        }
        else
        {
            PowerLimitDisplay = $"(Mem: {PowerLimit:F0}G)";
        }
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
        if (GpuName is "Unknown" or "N/A")
            GpuName = "GPU Detect Error";
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

        Log.Information("Switched provider: {Name} ({Type})", CurrentProviderName, _gpuProvider.GetType().Name);
    }

    private static string GetVersionString()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return info?.InformationalVersion is { } v ? $"v{v}" : "v?";
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

            // F. Chart 讀取 (HWiNFO / LHM)
            if (IsHwinfoChartVisible && AppliedHwinfoSensor is { } appliedSensor)
            {
                var provider = GetActiveProvider();
                if (provider is null) return;

                var entry = provider.ReadEntry(appliedSensor.SensorName, appliedSensor.LabelOrig);
                if (entry is not null)
                {
                    _hwinfoUnavailableLogged = false;
                    IsHwinfoReadable = true;
                    CurrentHwinfoValue = entry.Value;
                    _lastHwinfoTickValue = entry.Value;

                    // Write to ring buffer
                    WriteHwinfoBuffer(entry.Value);

                    // Dynamic Y axis: peak * 1.1, min 0.1
                    if (entry.Value > _hwinfoPeak)
                        _hwinfoPeak = entry.Value;
                    HwinfoChartYMax = _hwinfoPeak > 0 ? _hwinfoPeak * 1.1 : 0.1;

                    HwinfoChartDataUpdated?.Invoke();
                }
                else
                {
                    // HWiNFO unavailable — 寫入 NaN 產生斷線而非假的 0 值
                    IsHwinfoReadable = false;
                    _lastHwinfoTickValue = double.NaN;
                    WriteHwinfoBuffer(double.NaN);
                    HwinfoChartDataUpdated?.Invoke();

                    if (!_hwinfoUnavailableLogged)
                    {
                        Log.Warning("[HWiNFO] Sensor read failed, HWiNFO64 may be closed or 12-hour limit reached");
                        _hwinfoUnavailableLogged = true;
                    }
                }
            }
            // 啟動時等待 HWiNFO：chart 已啟用但尚未有 applied sensor（等待 SHM 可用再自動套用）
            else if (IsHwinfoChartVisible && AppliedHwinfoSensor is null)
            {
                _hwinfoRetryCounter++;
                if (_hwinfoRetryCounter % 5 == 0) // 每 5 秒嘗試一次
                {
                    TryAutoApplyHwinfoSensor();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during data reading");
        }
    }

    #region HWiNFO Chart

    private ISensorDataProvider? GetActiveProvider() => ChartSource switch
    {
        ChartSourceType.HWiNFO => ServiceLocator.HwinfoProvider as ISensorDataProvider,
        ChartSourceType.LibreHardwareMonitor => ServiceLocator.LhmSensorProvider,
        _ => null
    };

    private void UpdateChartSourceDisplay()
    {
        ChartSourceDisplayName = ChartSource switch
        {
            ChartSourceType.HWiNFO => "HWiNFO",
            ChartSourceType.LibreHardwareMonitor => "LHM",
            _ => _loc["Chart_SensorChart"]
        };
        ChartTitleText = ChartSource switch
        {
            ChartSourceType.HWiNFO => "HWiNFO:",
            ChartSourceType.LibreHardwareMonitor => "LHM:",
            _ => ""
        };
    }

    [RelayCommand]
    private void SetChartSource(ChartSourceType source)
    {
        var previousSource = ChartSource;
        ChartSource = source;
        UpdateChartSourceDisplay();

        if (source == ChartSourceType.Off)
        {
            IsHwinfoChartVisible = false;
            SaveChartSettings();
            return;
        }

        // Check if provider is available
        var provider = GetActiveProvider();
        if (provider is null || !provider.IsAvailable)
        {
            // Defer to avoid re-entrancy
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ChartSource = ChartSourceType.Off;
                UpdateChartSourceDisplay();
                IsHwinfoChartVisible = false;

                var msg = source == ChartSourceType.HWiNFO
                    ? _loc["HWiNFO_ShmNotFound"]
                    : "LibreHardwareMonitor is not available.";
                HwinfoShmNotFound?.Invoke(msg);
            });
            return;
        }

        // If source changed, clear buffer and reset
        if (previousSource != source)
        {
            ClearHwinfoBuffer();
            _hwinfoPeak = 0;
            HwinfoChartYMax = 0.1;
            CurrentHwinfoValue = 0;
            _hwinfoUnavailableLogged = false;
            AppliedHwinfoSensor = null;
            AppliedHwinfoDisplay = "";
        }

        _hwinfoInitializing = true;
        try
        {
            IsHwinfoChartVisible = true;
            RefreshHwinfoSensorList();
        }
        finally
        {
            _hwinfoInitializing = false;
        }

        SaveChartSettings();
    }

    private void InitializeHwinfo()
    {
        var settings = ServiceLocator.SettingsService?.Load();
        if (settings is null) return;

        // Use new ChartSource if available, fallback to legacy HwinfoChartEnabled
        var source = settings.ChartSource;
        if (source == ChartSourceType.Off && settings.HwinfoChartEnabled)
            source = ChartSourceType.HWiNFO;

        if (source == ChartSourceType.Off) return;

        var selectedSensor = settings.ChartSelectedSensor ?? settings.HwinfoSelectedSensor;
        var selectedEntry = settings.ChartSelectedEntry ?? settings.HwinfoSelectedEntry;

        _hwinfoInitializing = true;
        try
        {
            ChartSource = source;
            UpdateChartSourceDisplay();
            IsHwinfoChartVisible = true;

            // 嘗試從設定還原 applied sensor 名稱（用於顯示）
            if (!string.IsNullOrEmpty(selectedSensor) &&
                !string.IsNullOrEmpty(selectedEntry))
            {
                AppliedHwinfoDisplay = $"{selectedEntry} [{selectedSensor}]";
            }

            // 嘗試立即套用
            TryAutoApplyHwinfoSensor();
        }
        finally
        {
            _hwinfoInitializing = false;
        }
    }

    /// <summary>
    /// 啟動時嘗試自動套用上次選擇的感測器
    /// </summary>
    private void TryAutoApplyHwinfoSensor()
    {
        var provider = GetActiveProvider();
        if (provider is null || !provider.IsAvailable) return;

        var settings = ServiceLocator.SettingsService?.Load();
        if (settings is null) return;

        var selectedSensor = settings.ChartSelectedSensor ?? settings.HwinfoSelectedSensor;
        var selectedEntry = settings.ChartSelectedEntry ?? settings.HwinfoSelectedEntry;
        if (string.IsNullOrEmpty(selectedSensor) || string.IsNullOrEmpty(selectedEntry))
            return;

        // 刷新清單
        RefreshHwinfoSensorList();

        // 找到上次的選擇
        foreach (var item in _allHwinfoItems)
        {
            if (item.SensorName == selectedSensor &&
                item.LabelOrig == selectedEntry)
            {
                ApplyHwinfoSensorInternal(item);
                Log.Information("[HWiNFO] Auto-restored sensor: {Name}", item.DisplayName);
                return;
            }
        }
    }

    [RelayCommand]
    private void RefreshHwinfoSensorList()
    {
        var provider = GetActiveProvider();
        if (provider is null || !provider.IsAvailable)
        {
            Log.Debug("[HWiNFO] Provider unavailable, cannot list sensors");
            return;
        }

        var previousComboSelection = SelectedHwinfoSensor;
        _allHwinfoItems.Clear();
        HwinfoSensorItems.Clear();

        var groups = provider.GetSensorGroups();
        foreach (var group in groups)
        {
            foreach (var entry in group.Entries)
            {
                var item = new HwinfoSensorItem(
                    group.SensorName, entry.LabelOrig, entry.Label, entry.Unit);
                _allHwinfoItems.Add(item);
                HwinfoSensorItems.Add(item);
            }
        }

        ApplyHwinfoFilter();

        // 嘗試還原 ComboBox 選擇
        if (previousComboSelection is not null)
        {
            foreach (var item in HwinfoFilteredItems)
            {
                if (item.SensorName == previousComboSelection.SensorName &&
                    item.LabelOrig == previousComboSelection.LabelOrig)
                {
                    SelectedHwinfoSensor = item;
                    break;
                }
            }
        }

        Log.Information("[HWiNFO] Sensor list refreshed, {Count} items", _allHwinfoItems.Count);
    }

    partial void OnHwinfoFilterTextChanged(string value)
    {
        ApplyHwinfoFilter();
    }

    private void ApplyHwinfoFilter()
    {
        // 記住目前 ComboBox 選擇
        var prev = SelectedHwinfoSensor;
        HwinfoFilteredItems.Clear();
        var filter = HwinfoFilterText?.Trim() ?? "";

        foreach (var item in _allHwinfoItems)
        {
            if (filter.Length == 0 ||
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                HwinfoFilteredItems.Add(item);
            }
        }

        // 嘗試保留選擇（若仍在過濾結果中）
        if (prev is not null && HwinfoFilteredItems.Contains(prev))
        {
            SelectedHwinfoSensor = prev;
        }
    }

    /// <summary>
    /// Apply 按鈕：將 ComboBox 選擇套用為實際繪圖的感測器
    /// </summary>
    [RelayCommand]
    private void ApplyHwinfoSensor()
    {
        if (SelectedHwinfoSensor is null) return;
        ApplyHwinfoSensorInternal(SelectedHwinfoSensor);
    }

    private void ApplyHwinfoSensorInternal(HwinfoSensorItem item)
    {
        // 判斷是否為不同的感測器
        var isDifferent = AppliedHwinfoSensor is null ||
                          AppliedHwinfoSensor.SensorName != item.SensorName ||
                          AppliedHwinfoSensor.LabelOrig != item.LabelOrig;

        AppliedHwinfoSensor = item;
        AppliedHwinfoDisplay = $"{item.Label} ({item.Unit}) [{item.SensorName}]";
        HwinfoChartLabel = item.Label;
        HwinfoChartUnit = item.Unit;

        if (isDifferent)
        {
            // 清空 buffer + 重設 Y 軸
            ClearHwinfoBuffer();
            _hwinfoPeak = 0;
            HwinfoChartYMax = 0.1;
            CurrentHwinfoValue = 0;
            _hwinfoUnavailableLogged = false;
            Log.Information("[HWiNFO] Applied sensor: {Name}", item.DisplayName);
        }

        SaveChartSettings();
    }

    private void ClearHwinfoBuffer()
    {
        // 填入 NaN 使 ScottPlot 不繪製舊資料
        Array.Fill(_hwinfoBuffer, double.NaN);
        _hwinfoBufferIndex = 0;
        _hwinfoBufferCount = 0;
        _hwinfoSum = 0;
        HwinfoChartCleared?.Invoke();
    }

    private void WriteHwinfoBuffer(double value)
    {
        if (_hwinfoBufferCount >= _maxPoints)
        {
            var old = _hwinfoBuffer[_hwinfoBufferIndex];
            if (!double.IsNaN(old))
                _hwinfoSum -= old;
        }

        _hwinfoBuffer[_hwinfoBufferIndex] = value;
        if (!double.IsNaN(value))
            _hwinfoSum += value;

        _hwinfoBufferIndex = (_hwinfoBufferIndex + 1) % _maxPoints;
        if (_hwinfoBufferCount < _maxPoints) _hwinfoBufferCount++;
    }

    partial void OnIsHwinfoChartVisibleChanged(bool value)
    {
        if (value && !_hwinfoInitializing)
        {
            var provider = GetActiveProvider();
            if (provider is null || !provider.IsAvailable)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    IsHwinfoChartVisible = false;
                    HwinfoShmNotFound?.Invoke(_loc["HWiNFO_ShmNotFound"]);
                });
                return;
            }
            RefreshHwinfoSensorList();
        }
        SaveChartSettings();
    }

    private void SaveChartSettings()
    {
        if (_hwinfoInitializing) return;

        var service = ServiceLocator.SettingsService;
        if (service is null) return;

        var settings = service.Load();
        settings.ChartSource = ChartSource;
        settings.ChartSelectedSensor = AppliedHwinfoSensor?.SensorName;
        settings.ChartSelectedEntry = AppliedHwinfoSensor?.LabelOrig;
        // Keep legacy fields in sync
        settings.HwinfoChartEnabled = ChartSource != ChartSourceType.Off;
        settings.HwinfoSelectedSensor = settings.ChartSelectedSensor;
        settings.HwinfoSelectedEntry = settings.ChartSelectedEntry;
        service.Save(settings);
    }

    #endregion
}
