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
    private readonly AlertEvaluator _voltageEvaluator;
    private readonly AlertEvaluator _tempEvaluator;
    private readonly List<IAlertAction> _alertActions = new();
    private CommandLineAlertAction? _commandLineAction;

    // --- 警報門檻常數 (T0-3: 供審計日誌使用) ---
    private const float VoltageThreshold = 11.8f;
    private const float TemperatureThreshold = 88.0f;
    private const int AlertWindowSeconds = 3;
    private const int AlertTriggerCount = 2;

    // --- 警報狀態追蹤 (避免重複記錄) ---
    private bool _wasVoltageAlertActive;
    private bool _wasTempAlertActive;

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

        // 2. 初始化警報判定器 (依規格書：X秒內達到門檻Y次)
        _voltageEvaluator = new AlertEvaluator(
            seconds: AlertWindowSeconds,
            count: AlertTriggerCount,
            threshold: VoltageThreshold,
            isLowerBound: true);
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

        Log.Information("16pin-vmon 監控啟動: {GpuName}", GpuName);
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

        var settings = settingsService.Load();
        Log.Information("警報動作已初始化: CommandLine={Enabled}, DebugMode={Debug}",
            settings.CommandLineAlertEnabled,
            settings.AlertActionsDebugMode);
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

    /// <summary>
    /// T0-3: 審計日誌 - 完整記錄警報觸發瞬間，作為「程式已盡提醒義務」之證明
    /// T4-1: 觸發警報動作
    /// </summary>
    private void HandleAlertLogs()
    {
        // 電壓警報：從無到有觸發時記錄
        if (IsVoltageAlert && !_wasVoltageAlertActive)
        {
            Log.Warning(
                "[ALERT TRIGGERED] 16-pin 電壓異常 | " +
                "GPU: {GpuName} | " +
                "電壓: {Voltage:F3}V | " +
                "門檻: < {Threshold:F1}V | " +
                "判定條件: {Window}秒內{Count}次 | " +
                "溫度: {Temp:F1}°C",
                GpuName,
                CurrentVoltage,
                VoltageThreshold,
                AlertWindowSeconds,
                AlertTriggerCount,
                CurrentTemperature);

            // T4-1: 執行警報動作
            _ = ExecuteAlertActionsAsync("Voltage", $"16-pin 電壓異常: {CurrentVoltage:F3}V");
        }

        // 電壓警報解除
        if (!IsVoltageAlert && _wasVoltageAlertActive)
        {
            Log.Information(
                "[ALERT CLEARED] 16-pin 電壓恢復正常 | " +
                "GPU: {GpuName} | " +
                "電壓: {Voltage:F3}V",
                GpuName,
                CurrentVoltage);
        }

        // 溫度警報：從無到有觸發時記錄
        if (IsTempAlert && !_wasTempAlertActive)
        {
            Log.Warning(
                "[ALERT TRIGGERED] GPU 溫度過高 | " +
                "GPU: {GpuName} | " +
                "溫度: {Temp:F1}°C | " +
                "門檻: > {Threshold:F1}°C | " +
                "判定條件: {Window}秒內{Count}次 | " +
                "電壓: {Voltage:F3}V",
                GpuName,
                CurrentTemperature,
                TemperatureThreshold,
                AlertWindowSeconds,
                AlertTriggerCount,
                CurrentVoltage);

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
        _wasVoltageAlertActive = IsVoltageAlert;
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
                CurrentVoltage,
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

        // 執行其他警報動作（未來 T4-2, T4-3 實作）
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