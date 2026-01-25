using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using _16pin_vmon.Core.Interfaces;
using _16pin_vmon.Logic;
using Serilog;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace _16pin_vmon.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IGpuProvider _gpuProvider;
    private readonly DispatcherTimer _timer;
    private readonly AlertEvaluator _voltageEvaluator;
    private readonly AlertEvaluator _tempEvaluator;

    // --- 介面綁定屬性 ---
    [ObservableProperty] private string _gpuName = "正在偵測...";
    [ObservableProperty] private float _currentVoltage;
    [ObservableProperty] private float _currentTemperature;
    [ObservableProperty] private bool _isVoltageAlert;
    [ObservableProperty] private bool _isTempAlert;

    // --- LiveCharts2 數據結構 ---
    public ObservableCollection<float> VoltageHistory { get; } = new();
    public ObservableCollection<float> TemperatureHistory { get; } = new();

    public ISeries[] VoltageSeries { get; set; }
    public ISeries[] TempSeries { get; set; }

    public Axis[] VoltageYAxes { get; set; } = {
        new Axis {
            MinLimit = 11.3, MaxLimit = 12.6,
            Labeler = v => v.ToString("F2") + " V",
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

        // 1. 初始化圖表外觀
        InitializeCharts();

        // 2. 初始化警報判定器 (依規格書：3秒內2次判定)
        _voltageEvaluator = new AlertEvaluator(seconds: 3, count: 2, threshold: 11.8f, isLowerBound: true);
        _tempEvaluator = new AlertEvaluator(seconds: 3, count: 2, threshold: 88.0f, isLowerBound: false);

        // 3. 設定定時器
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Log.Information("16pin-vmon 監控啟動: {GpuName}", GpuName);
    }

    private void InitializeCharts()
    {
        VoltageSeries = new ISeries[] {
            new LineSeries<float> {
                Values = VoltageHistory,
                Fill = null,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(SKColors.Cyan, 2)
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
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            // A. 讀取數據
            var reading = _gpuProvider.GetCurrentReading();
            CurrentVoltage = reading.Voltage16Pin;
            CurrentTemperature = reading.Temperature;

            // B. 警報邏輯判定 (滑動視窗)
            IsVoltageAlert = _voltageEvaluator.PushValueAndCheckAlert(CurrentVoltage);
            IsTempAlert = _tempEvaluator.PushValueAndCheckAlert(CurrentTemperature);

            // C. 更新圖表數據 (保留最近 900 秒)
            UpdateHistory(VoltageHistory, CurrentVoltage);
            UpdateHistory(TemperatureHistory, CurrentTemperature);

            // D. 警報執行動作與日誌
            HandleAlertLogs();

            // E. 動態 Y 軸調整
            if (CurrentVoltage > 12.6) VoltageYAxes[0].MaxLimit = CurrentVoltage + 0.1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "數據讀取或判定過程中發生錯誤");
        }
    }

    private void HandleAlertLogs()
    {
        if (IsVoltageAlert)
        {
            Log.Warning("檢測到 RTX 50 16-pin 電壓異常降壓: {Voltage}V", CurrentVoltage);
            // 未來在此處觸發 LINE/Telegram
        }

        if (IsTempAlert)
        {
            Log.Warning("檢測到 GPU 溫度過高: {Temp}°C", CurrentTemperature);
        }
    }

    private void UpdateHistory(ObservableCollection<float> history, float newValue)
    {
        history.Add(newValue);
        if (history.Count > 900) history.RemoveAt(0);
    }
}