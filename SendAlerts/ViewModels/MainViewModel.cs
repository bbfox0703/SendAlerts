using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Core.Interfaces;
using SendAlerts.Services;
using Serilog;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

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

    // --- 平均值 (累計追蹤，避免遍歷 ObservableCollection) ---
    [ObservableProperty] private float _avgUtilization;
    [ObservableProperty] private float _avgTemperature;
    [ObservableProperty] private float _avgPower;
    [ObservableProperty] private string _avgPowerDisplay = "";
    private double _sumUtilization, _sumTemperature, _sumPower;
    private int _avgSampleCount;

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

    // --- TC1-3: 顯示模式提示 (use Loc_DisplayModeHint) ---

    // --- 圖表時間維度 ---
    [ObservableProperty] private int _selectedHistoryDuration = 900;

    /// <summary>
    /// 圖表最大顯示點數上限，避免 SkiaSharp native memory 過量消耗。
    /// 當 SelectedHistoryDuration 超過此值時，自動降採樣 (每 N 個 tick 記錄一點)。
    /// </summary>
    private const int MaxDisplayPoints = 3600;

    /// <summary>
    /// 當前的降採樣比率 (每 N 個 tick 記錄一點)
    /// </summary>
    private int _downsampleRate = 1;

    /// <summary>
    /// 降採樣用的 tick 計數器
    /// </summary>
    private int _tickCounter;

    public List<HistoryDurationOption> HistoryDurationOptions { get; } = new()
    {
        new(60, "1 min"),
        new(300, "5 min"),
        new(900, "15 min"),
        new(1800, "30 min"),
        new(3600, "60 min"),
    };

    partial void OnSelectedHistoryDurationChanged(int value)
    {
        // 計算降採樣比率: 確保最大點數不超過 MaxDisplayPoints
        _downsampleRate = Math.Max(1, (int)Math.Ceiling((double)value / MaxDisplayPoints));
        _tickCounter = 0;

        var maxPoints = value / _downsampleRate;
        TrimHistory(UtilizationHistory, maxPoints);
        TrimHistory(TemperatureHistory, maxPoints);
        TrimHistory(PowerHistory, maxPoints);
        Log.Information("圖表時間維度已切換為 {Duration} 秒 (降採樣比率: 1/{Rate}, 最大點數: {Max})",
            value, _downsampleRate, maxPoints);
    }

    private static void TrimHistory(ObservableCollection<TimestampedValue> history, int maxCount)
    {
        while (history.Count > maxCount)
            history.RemoveAt(0);
    }

    // --- TB3-3: 警報歷史 ---
    public ObservableCollection<AlertHistoryItem> AlertHistoryItems { get; } = new();

    // --- LiveCharts2 數據結構 ---
    public ObservableCollection<TimestampedValue> UtilizationHistory { get; } = new();
    public ObservableCollection<TimestampedValue> TemperatureHistory { get; } = new();
    public ObservableCollection<TimestampedValue> PowerHistory { get; } = new();

    public ISeries[] UtilizationSeries { get; set; } = [];
    public ISeries[] TempSeries { get; set; } = [];
    public ISeries[] PowerSeries { get; set; } = [];

    public Axis[] UtilizationYAxes { get; set; } = {
        new Axis {
            MinLimit = 0, MaxLimit = 100,
            Labeler = v => v.ToString("F0") + " %",
            SeparatorsPaint = new SolidColorPaint(SKColors.Gray.WithAlpha(50))
        }
    };

    public Axis[] TempYAxes { get; set; } = {
        new Axis {
            MinLimit = 0, MaxLimit = 100,
            Labeler = v => v.ToString("F0") + " °C",
            SeparatorsPaint = new SolidColorPaint(SKColors.Gray.WithAlpha(50))
        }
    };

    public Axis[] PowerYAxes { get; set; } = {
        new Axis {
            MinLimit = 0, MaxLimit = 600,
            Labeler = v => v.ToString("F0") + " W",
            SeparatorsPaint = new SolidColorPaint(SKColors.Gray.WithAlpha(50))
        }
    };

    public Axis[] XAxes { get; set; } = {
        new Axis {
            Labeler = v => new DateTime((long)v).ToString("HH:mm:ss"),
            ShowSeparatorLines = false,
            TextSize = 10
        }
    };

    public MainViewModel(IGpuProvider gpuProvider)
    {
        _gpuProvider = gpuProvider;
        GpuName = _gpuProvider.GetGpuName();
        PowerLimit = _gpuProvider.PowerLimit;

        // 初始化動態標籤 (支援 GPU/CPU 模式)
        ApplyProviderLabels();

        // Provider 切換
        CanSwitchProvider = ServiceLocator.AvailableProviders.Count > 1;
        CurrentProviderName = GetProviderDisplayName(_gpuProvider);
        UpdateSwitchTooltip();

        // 1. 初始化圖表外觀與降採樣比率
        _downsampleRate = Math.Max(1, (int)Math.Ceiling((double)_selectedHistoryDuration / MaxDisplayPoints));
        InitializeCharts();

        // 2. 設定定時器 (純讀取顯示，不觸發警報)
        var samplingInterval = ServiceLocator.SettingsService?.Load().SamplingIntervalSeconds ?? 1;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(samplingInterval) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        // 3. TB3-2/TB3-3: 訂閱狀態更新事件
        InitializeStatusSubscriptions();

        Log.Information("SendAlerts 監控啟動 (Display-Only Mode): {GpuName} | Mode: {Mode} | PowerLimit: {PowerLimit:F1}{Unit}",
            GpuName, _gpuProvider.Mode, PowerLimit, SecondaryMetricUnit);
    }

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

        // 清空圖表歷史與平均值
        UtilizationHistory.Clear();
        TemperatureHistory.Clear();
        PowerHistory.Clear();
        ResetAverages();

        // 重設 Y 軸
        ConfigureYAxesForMode();

        Log.Information("已切換 Provider: {Name} ({Type})", CurrentProviderName, _gpuProvider.GetType().Name);
    }

    private void InitializeCharts()
    {
        UtilizationSeries = new ISeries[] {
            new LineSeries<TimestampedValue> {
                Values = UtilizationHistory,
                Fill = new SolidColorPaint(SKColors.LimeGreen.WithAlpha(40)),
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0,
                Stroke = new SolidColorPaint(SKColors.LimeGreen, 1),
                Mapping = (tv, _) => new(tv.Timestamp.Ticks, tv.Value),
                YToolTipLabelFormatter = p =>
                    $"{p.Model!.Value:F1} %  ({p.Model.Timestamp:HH:mm:ss})"
            }
        };

        TempSeries = new ISeries[] {
            new LineSeries<TimestampedValue> {
                Values = TemperatureHistory,
                Fill = new SolidColorPaint(SKColors.OrangeRed.WithAlpha(40)),
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0,
                Stroke = new SolidColorPaint(SKColors.OrangeRed, 1),
                Mapping = (tv, _) => new(tv.Timestamp.Ticks, tv.Value),
                YToolTipLabelFormatter = p =>
                    $"{p.Model!.Value:F1} {TemperatureUnit}  ({p.Model.Timestamp:HH:mm:ss})"
            }
        };

        PowerSeries = new ISeries[] {
            new LineSeries<TimestampedValue> {
                Values = PowerHistory,
                Fill = new SolidColorPaint(SKColors.Cyan.WithAlpha(40)),
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0,
                Stroke = new SolidColorPaint(SKColors.Cyan, 1),
                Mapping = (tv, _) => new(tv.Timestamp.Ticks, tv.Value),
                YToolTipLabelFormatter = p => FormatPowerTooltip(p.Model!)
            }
        };

        // 根據硬體模式設定 Y 軸
        ConfigureYAxesForMode();
    }

    private string FormatPowerTooltip(TimestampedValue tv)
    {
        var valueText = IsGpuMode
            ? $"{tv.Value:F1} W"
            : tv.Value >= 1000
                ? $"{tv.Value / 1000:F1} MB/s"
                : $"{tv.Value:F0} KB/s";
        return $"{valueText}  ({tv.Timestamp:HH:mm:ss})";
    }

    /// <summary>
    /// 根據硬體模式設定 Y 軸標籤和範圍
    /// </summary>
    private void ConfigureYAxesForMode()
    {
        if (IsGpuMode)
        {
            // GPU 模式: 溫度 0-100 °C, 功耗 0-600 W
            TempYAxes[0].MinLimit = 0;
            TempYAxes[0].MaxLimit = 100;
            TempYAxes[0].Labeler = v => v.ToString("F0") + " °C";

            PowerYAxes[0].MinLimit = 0;
            PowerYAxes[0].MaxLimit = 600;
            PowerYAxes[0].Labeler = v => v.ToString("F0") + " W";
        }
        else
        {
            // CPU/Network 模式: 記憶體 0-100 %, 網路自動縮放
            TempYAxes[0].MinLimit = 0;
            TempYAxes[0].MaxLimit = 100;
            TempYAxes[0].Labeler = v => v.ToString("F0") + " %";

            PowerYAxes[0].MinLimit = 0;
            PowerYAxes[0].MaxLimit = 10000; // 初始 10 MB/s
            PowerYAxes[0].Labeler = v =>
            {
                if (v >= 1000)
                    return (v / 1000).ToString("F1") + " MB/s";
                return v.ToString("F0") + " KB/s";
            };
        }
    }

    /// <summary>
    /// 動態調整 Y 軸範圍
    /// </summary>
    private void AdjustYAxisDynamically()
    {
        var currentMax = PowerYAxes[0].MaxLimit ?? 600;

        if (IsGpuMode)
        {
            if (CurrentPower > currentMax)
            {
                PowerYAxes[0].MaxLimit = CurrentPower + 50;
            }
        }
        else
        {
            if (CurrentPower > currentMax)
            {
                PowerYAxes[0].MaxLimit = Math.Max(CurrentPower * 1.5, currentMax + 1000);
            }
        }
    }

    /// <summary>
    /// TC1-1: 定時器回呼 - 純讀取顯示，不觸發警報
    /// </summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            // A. 讀取數據 (每個 tick 都更新數值顯示)
            var reading = _gpuProvider.GetCurrentReading();
            CurrentUtilization = reading.GpuUtilization;
            CurrentTemperature = reading.Temperature;
            CurrentPower = reading.PowerUsage;

            var now = DateTime.Now;

            // B. 降採樣：長時間範圍時不是每個 tick 都寫入圖表
            _tickCounter++;
            if (_tickCounter >= _downsampleRate)
            {
                _tickCounter = 0;
                var maxPoints = SelectedHistoryDuration / _downsampleRate;
                bool removed = UtilizationHistory.Count >= maxPoints;
                float rU = 0, rT = 0, rP = 0;
                if (removed)
                {
                    rU = UtilizationHistory[0].Value;
                    rT = TemperatureHistory[0].Value;
                    rP = PowerHistory[0].Value;
                }
                UpdateHistory(UtilizationHistory, new TimestampedValue(CurrentUtilization, now), maxPoints);
                UpdateHistory(TemperatureHistory, new TimestampedValue(CurrentTemperature, now), maxPoints);
                UpdateHistory(PowerHistory, new TimestampedValue(CurrentPower, now), maxPoints);

                // E. 更新平均值 (累計值，不遍歷集合)
                UpdateAverages(CurrentUtilization, CurrentTemperature, CurrentPower, removed, rU, rT, rP);
            }

            // C. 更新 X 軸時間範圍 (固定寬度，資料由右向左推進)
            XAxes[0].MinLimit = now.AddSeconds(-SelectedHistoryDuration).Ticks;
            XAxes[0].MaxLimit = now.Ticks;

            // D. 動態 Y 軸調整
            AdjustYAxisDynamically();

            // F. TDP 定時重測 (僅 GPU 模式)
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

    private void UpdateAverages(float utilization, float temperature, float power, bool removed, float removedUtil, float removedTemp, float removedPower)
    {
        if (removed)
        {
            _sumUtilization -= removedUtil;
            _sumTemperature -= removedTemp;
            _sumPower -= removedPower;
        }
        _sumUtilization += utilization;
        _sumTemperature += temperature;
        _sumPower += power;
        _avgSampleCount = UtilizationHistory.Count;

        if (_avgSampleCount > 0)
        {
            AvgUtilization = (float)(_sumUtilization / _avgSampleCount);
            AvgTemperature = (float)(_sumTemperature / _avgSampleCount);
            AvgPower = (float)(_sumPower / _avgSampleCount);
        }

        AvgPowerDisplay = IsGpuMode
            ? $"(Avg: {AvgPower:F1} W)"
            : AvgPower >= 1000
                ? $"(Avg: {AvgPower / 1000:F1} MB/s)"
                : $"(Avg: {AvgPower:F0} KB/s)";
    }

    private void ResetAverages()
    {
        _sumUtilization = _sumTemperature = _sumPower = 0;
        _avgSampleCount = 0;
        AvgUtilization = AvgTemperature = AvgPower = 0;
        AvgPowerDisplay = "";
    }

    private static void UpdateHistory(ObservableCollection<TimestampedValue> history, TimestampedValue newValue, int maxPoints)
    {
        if (history.Count >= maxPoints) history.RemoveAt(0);
        history.Add(newValue);
    }
}

/// <summary>
/// 帶時間戳的圖表資料點
/// </summary>
public record TimestampedValue(float Value, DateTime Timestamp);

/// <summary>
/// 圖表時間維度選項
/// </summary>
public record HistoryDurationOption(int Seconds, string Label)
{
    public override string ToString() => Label;
}
