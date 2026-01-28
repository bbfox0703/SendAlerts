using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SendAlerts.Core.Interfaces;
using SendAlerts.Services;
using Serilog;
using LiveChartsCore;
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
    private readonly IGpuProvider _gpuProvider;
    private readonly DispatcherTimer _timer;

    // --- 介面綁定屬性 ---
    [ObservableProperty] private string _gpuName = "正在偵測...";
    [ObservableProperty] private float _currentUtilization;
    [ObservableProperty] private float _currentTemperature;
    [ObservableProperty] private float _currentPower;
    [ObservableProperty] private float _powerLimit;

    // --- 動態標籤 (支援 GPU/CPU 模式切換) ---
    [ObservableProperty] private string _primaryMetricLabel = "GPU Utilization";
    [ObservableProperty] private string _temperatureLabel = "GPU Temperature";
    [ObservableProperty] private string _secondaryMetricLabel = "Power Usage";
    [ObservableProperty] private string _secondaryMetricUnit = "W";
    [ObservableProperty] private bool _isGpuMode = true;

    // --- TB3-2: 狀態列屬性 ---
    [ObservableProperty] private string _statusText = "System Ready";
    [ObservableProperty] private bool _isPipeServerRunning;

    // --- TC1-3: 顯示模式提示 ---
    [ObservableProperty] private string _displayModeHint = "Display Only - Use HWiNFO64 for alerts";

    // --- TB3-3: 警報歷史 ---
    public ObservableCollection<AlertHistoryItem> AlertHistoryItems { get; } = new();

    // --- LiveCharts2 數據結構 ---
    public ObservableCollection<float> UtilizationHistory { get; } = new();
    public ObservableCollection<float> TemperatureHistory { get; } = new();
    public ObservableCollection<float> PowerHistory { get; } = new();

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
            Labeler = v => string.Empty,
            ShowSeparatorLines = false
        }
    };

    public MainViewModel(IGpuProvider gpuProvider)
    {
        _gpuProvider = gpuProvider;
        GpuName = _gpuProvider.GetGpuName();
        PowerLimit = _gpuProvider.PowerLimit;

        // 初始化動態標籤 (支援 GPU/CPU 模式)
        PrimaryMetricLabel = _gpuProvider.PrimaryMetricLabel;
        TemperatureLabel = _gpuProvider.TemperatureLabel;
        SecondaryMetricLabel = _gpuProvider.SecondaryMetricLabel;
        SecondaryMetricUnit = _gpuProvider.SecondaryMetricUnit;
        IsGpuMode = _gpuProvider.Mode == HardwareMode.Gpu;

        // 1. 初始化圖表外觀
        InitializeCharts();

        // 2. 設定定時器 (純讀取顯示，不觸發警報)
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        // 3. TB3-2/TB3-3: 訂閱狀態更新事件
        InitializeStatusSubscriptions();

        Log.Information("SendAlerts 監控啟動 (Display-Only Mode): {GpuName} | Mode: {Mode} | PowerLimit: {PowerLimit:F1}{Unit}",
            GpuName, _gpuProvider.Mode, PowerLimit, SecondaryMetricUnit);
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

    private void InitializeCharts()
    {
        UtilizationSeries = new ISeries[] {
            new LineSeries<float> {
                Values = UtilizationHistory,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(SKColors.LimeGreen, 2)
            }
        };

        TempSeries = new ISeries[] {
            new LineSeries<float> {
                Values = TemperatureHistory,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(SKColors.OrangeRed, 2)
            }
        };

        PowerSeries = new ISeries[] {
            new LineSeries<float> {
                Values = PowerHistory,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(SKColors.Cyan, 2)
            }
        };

        // 根據硬體模式設定 Y 軸
        ConfigureYAxesForMode();
    }

    /// <summary>
    /// 根據硬體模式設定 Y 軸標籤和範圍
    /// </summary>
    private void ConfigureYAxesForMode()
    {
        if (IsGpuMode)
        {
            // GPU 模式: 0-600 W
            PowerYAxes[0].MinLimit = 0;
            PowerYAxes[0].MaxLimit = 600;
            PowerYAxes[0].Labeler = v => v.ToString("F0") + " W";
        }
        else
        {
            // CPU/Network 模式: 自動縮放
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
            // A. 讀取數據
            var reading = _gpuProvider.GetCurrentReading();
            CurrentUtilization = reading.GpuUtilization;
            CurrentTemperature = reading.Temperature;
            CurrentPower = reading.PowerUsage;

            // B. 更新圖表數據 (保留最近 900 秒)
            UpdateHistory(UtilizationHistory, CurrentUtilization);
            UpdateHistory(TemperatureHistory, CurrentTemperature);
            UpdateHistory(PowerHistory, CurrentPower);

            // C. 動態 Y 軸調整
            AdjustYAxisDynamically();

            // TC1-1: 不再執行警報判定與觸發
            // 警報功能已移至 Alert Center (透過 Named Pipe 由外部工具觸發)
        }
        catch (Exception ex)
        {
            Log.Error(ex, "數據讀取過程中發生錯誤");
        }
    }

    private void UpdateHistory(ObservableCollection<float> history, float newValue)
    {
        history.Add(newValue);
        if (history.Count > 900) history.RemoveAt(0);
    }
}
