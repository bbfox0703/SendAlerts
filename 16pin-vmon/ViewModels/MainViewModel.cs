using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using _16pin_vmon.Core.Interfaces;
using _16pin_vmon.Logic;
using _16pin_vmon.Services;
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
    private readonly AlertEvaluator _tempEvaluator;
    private readonly List<IAlertAction> _alertActions = new();
    private CommandLineAlertAction? _commandLineAction;
    private TelegramAlertAction? _telegramAction;
    private LineNotifyAlertAction? _lineNotifyAction;

    // --- 警報門檻常數 (T0-3: 供審計日誌使用) ---
    private const float TemperatureThreshold = 88.0f;
    private const int AlertWindowSeconds = 3;
    private const int AlertTriggerCount = 2;

    // --- 警報狀態追蹤 (避免重複記錄) ---
    private bool _wasTempAlertActive;

    // --- 介面綁定屬性 ---
    [ObservableProperty] private string _gpuName = "正在偵測...";
    [ObservableProperty] private float _currentUtilization;
    [ObservableProperty] private float _currentTemperature;
    [ObservableProperty] private float _currentPower;
    [ObservableProperty] private bool _isTempAlert;
    [ObservableProperty] private float _powerLimit;

    // --- 動態標籤 (支援 GPU/CPU 模式切換) ---
    [ObservableProperty] private string _primaryMetricLabel = "GPU Utilization";
    [ObservableProperty] private string _temperatureLabel = "GPU Temperature";
    [ObservableProperty] private string _secondaryMetricLabel = "Power Usage";
    [ObservableProperty] private string _secondaryMetricUnit = "W";
    [ObservableProperty] private bool _isGpuMode = true;

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
            Labeler = v => v.ToString("F0") + " W", // 預設為 GPU 模式，InitializeCharts 會根據模式調整
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

        // 1. 初始化圖表外觀 (在設定標籤之後)
        InitializeCharts();

        // 2. 初始化警報判定器 (溫度)
        _tempEvaluator = new AlertEvaluator(
            seconds: AlertWindowSeconds,
            count: AlertTriggerCount,
            threshold: TemperatureThreshold,
            isLowerBound: false);

        // 3. 初始化警報動作 (T4-1)
        InitializeAlertActions();

        // 4. 設定定時器
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Log.Information("16pin-vmon 監控啟動: {GpuName} | Mode: {Mode} | PowerLimit: {PowerLimit:F1}{Unit}",
            GpuName, _gpuProvider.Mode, PowerLimit, SecondaryMetricUnit);
    }

    /// <summary>
    /// T4-1: 初始化警報動作
    /// </summary>
    private void InitializeAlertActions()
    {
        var settingsService = ServiceLocator.SettingsService;
        if (settingsService == null)
        {
            Log.Warning("SettingsService 未初始化，警報動作將無法使用");
            return;
        }

        // T4-1: CommandLine Alert Action
        _commandLineAction = new CommandLineAlertAction(settingsService);
        if (_commandLineAction != null)
        {
            _alertActions.Add(_commandLineAction);
        }

        // T4-2: Telegram Alert Action
        _telegramAction = new TelegramAlertAction(settingsService);
        if (_telegramAction != null)
        {
            _alertActions.Add(_telegramAction);
        }

        // T4-3: LINE Notify Alert Action
        _lineNotifyAction = new LineNotifyAlertAction(settingsService);
        if (_lineNotifyAction != null)
        {
            _alertActions.Add(_lineNotifyAction);
        }

        var settings = settingsService.Load();
        Log.Information("警報動作已初始化: CommandLine={CmdEnabled}, Telegram={TgEnabled}, LINE={LineEnabled}, DebugMode={Debug}",
            settings.CommandLineAlertEnabled,
            settings.TelegramAlertEnabled,
            settings.LineNotifyAlertEnabled,
            settings.AlertActionsDebugMode);
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
            // CPU/Network 模式: 0-125000 KB/s，自動縮放為 MB/s
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
            // GPU 模式: 功耗超過 600W 時動態擴展
            if (CurrentPower > currentMax)
            {
                PowerYAxes[0].MaxLimit = CurrentPower + 50;
            }
        }
        else
        {
            // CPU/Network 模式: 網路流量超過當前上限時動態擴展
            if (CurrentPower > currentMax)
            {
                // 擴展為當前值的 1.5 倍，最少增加 1000 KB/s
                PowerYAxes[0].MaxLimit = Math.Max(CurrentPower * 1.5, currentMax + 1000);
            }
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            // A. 讀取數據
            var reading = _gpuProvider.GetCurrentReading();
            CurrentUtilization = reading.GpuUtilization;
            CurrentTemperature = reading.Temperature;
            CurrentPower = reading.PowerUsage;

            // B. 警報邏輯判定 (滑動視窗) - 僅溫度
            IsTempAlert = _tempEvaluator.PushValueAndCheckAlert(CurrentTemperature);

            // C. 更新圖表數據 (保留最近 900 秒)
            UpdateHistory(UtilizationHistory, CurrentUtilization);
            UpdateHistory(TemperatureHistory, CurrentTemperature);
            UpdateHistory(PowerHistory, CurrentPower);

            // D. 警報執行動作與日誌
            HandleAlertLogs();

            // E. 動態 Y 軸調整
            AdjustYAxisDynamically();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "數據讀取或判定過程中發生錯誤");
        }
    }

    /// <summary>
    /// T0-3: 審計日誌 - 完整記錄警報觸發瞬間，作為「程式已盡提醒義務」之證明
    /// T4-1: 觸發警報動作
    /// </summary>
    private void HandleAlertLogs()
    {
        // 溫度警報：從無到有觸發時記錄
        if (IsTempAlert && !_wasTempAlertActive)
        {
            Log.Warning(
                "[ALERT TRIGGERED] GPU 溫度過高 | " +
                "GPU: {GpuName} | " +
                "溫度: {Temp:F1}°C | " +
                "門檻: > {Threshold:F1}°C | " +
                "判定條件: {Window}秒內{Count}次 | " +
                "功耗: {Power:F1}W",
                GpuName,
                CurrentTemperature,
                TemperatureThreshold,
                AlertWindowSeconds,
                AlertTriggerCount,
                CurrentPower);

            // T4-1: 執行警報動作
            _ = ExecuteAlertActionsAsync("Temperature", $"GPU 溫度過高: {CurrentTemperature:F1}°C");
        }

        // 溫度警報解除
        if (!IsTempAlert && _wasTempAlertActive)
        {
            Log.Information(
                "[ALERT CLEARED] GPU 溫度恢復正常 | " +
                "GPU: {GpuName} | " +
                "溫度: {Temp:F1}°C",
                GpuName,
                CurrentTemperature);
        }

        // 更新狀態追蹤
        _wasTempAlertActive = IsTempAlert;
    }

    /// <summary>
    /// T4-1: 非同步執行所有啟用的警報動作
    /// </summary>
    private async Task ExecuteAlertActionsAsync(string alertType, string message)
    {
        // 處理 CommandLine 動作的變數替換
        if (_commandLineAction != null && _commandLineAction.IsEnabled)
        {
            var substitutedCommand = _commandLineAction.SubstituteVariables(
                _commandLineAction.Command,
                CurrentPower,
                CurrentTemperature,
                GpuName,
                alertType);

            // 暫時替換命令
            var originalCommand = _commandLineAction.Command;
            _commandLineAction.Command = substitutedCommand;

            try
            {
                await _commandLineAction.ExecuteAsync(message);
            }
            finally
            {
                _commandLineAction.Command = originalCommand;
            }
        }

        // 執行其他警報動作
        foreach (var action in _alertActions.Where(a => a.IsEnabled && a != _commandLineAction))
        {
            try
            {
                await action.ExecuteAsync(message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "執行警報動作 {ActionName} 時發生錯誤", action.ActionName);
            }
        }
    }

    private void UpdateHistory(ObservableCollection<float> history, float newValue)
    {
        history.Add(newValue);
        if (history.Count > 900) history.RemoveAt(0);
    }
}
