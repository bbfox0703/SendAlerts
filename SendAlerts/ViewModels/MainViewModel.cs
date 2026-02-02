using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Core.Interfaces;
using SendAlerts.Interfaces;
using SendAlerts.Models;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.ViewModels;

/// <summary>
/// MainViewModel — Flexible 4-slot chart system.
/// Each slot can display a built-in preset or an external sensor (HWiNFO/LHM).
/// </summary>
public partial class MainViewModel : ViewModelBase
{
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

    // --- GPU name / power limit (from best available provider) ---
    [ObservableProperty] private string _gpuName = "Detecting...";
    [ObservableProperty] private string _powerLimitDisplay = "";

    // --- TDP refresh ---
    private int _tdpRefreshCounter;
    private const int TdpRefreshIntervalSeconds = 600;

    // --- TB3-2: Status bar ---
    [ObservableProperty] private string _statusText = "System Ready";
    [ObservableProperty] private bool _isPipeServerRunning;

    // --- Average summary ---
    [ObservableProperty] private string _avgSummary = "";

    // --- Version ---
    public string VersionDisplay { get; } = GetVersionString();

    // --- Chart: 30 min fixed ---
    private const int HistoryDurationSeconds = 1800;
    private int _maxPoints;

    // ==========================================================================
    // 4 Flexible Chart Slots
    // ==========================================================================

    internal class ChartSlotState
    {
        public ChartSlotConfig Config { get; set; } = new() { SourceType = ChartSlotSourceType.Off };
        public double[] Buffer = Array.Empty<double>();
        public int BufferIndex;
        public int BufferCount;
        public double Sum;
        public double Peak;
        public double YMax = 100;
        public double CurrentValue;
        public string Label = "";
        public string Unit = "";
        public bool IsActive;
        public bool NetworkScaleMB; // for NetworkIO only
        public HwinfoSensorItem? AppliedSensor;
        public bool UnavailableLogged;
        public int RetryCounter;
        public double LastTickValue; // View reads this for ShiftAndAppend

        public void ClearBuffer()
        {
            Array.Fill(Buffer, double.NaN);
            BufferIndex = 0;
            BufferCount = 0;
            Sum = 0;
            Peak = 0;
        }

        public void WriteBuffer(double value, int maxPoints)
        {
            if (BufferCount >= maxPoints)
            {
                var old = Buffer[BufferIndex];
                if (!double.IsNaN(old)) Sum -= old;
            }
            Buffer[BufferIndex] = value;
            if (!double.IsNaN(value)) Sum += value;
            BufferIndex = (BufferIndex + 1) % maxPoints;
            if (BufferCount < maxPoints) BufferCount++;
        }
    }

    internal readonly ChartSlotState[] _slots = new ChartSlotState[4];

    // Per-slot observable properties (×4)
    // Slot 0
    [ObservableProperty] private string _slot0Label = "";
    [ObservableProperty] private string _slot0ValueDisplay = "";
    [ObservableProperty] private string _slot0Unit = "";
    [ObservableProperty] private double _slot0YMax = 100;
    [ObservableProperty] private bool _slot0IsVisible;
    [ObservableProperty] private string _slot0Color = ChartColors.SlotLineColors[0];
    [ObservableProperty] private bool _slot0IsReadable = true;
    // Slot 1
    [ObservableProperty] private string _slot1Label = "";
    [ObservableProperty] private string _slot1ValueDisplay = "";
    [ObservableProperty] private string _slot1Unit = "";
    [ObservableProperty] private double _slot1YMax = 100;
    [ObservableProperty] private bool _slot1IsVisible;
    [ObservableProperty] private string _slot1Color = ChartColors.SlotLineColors[1];
    [ObservableProperty] private bool _slot1IsReadable = true;
    // Slot 2
    [ObservableProperty] private string _slot2Label = "";
    [ObservableProperty] private string _slot2ValueDisplay = "";
    [ObservableProperty] private string _slot2Unit = "";
    [ObservableProperty] private double _slot2YMax = 100;
    [ObservableProperty] private bool _slot2IsVisible;
    [ObservableProperty] private string _slot2Color = ChartColors.SlotLineColors[2];
    [ObservableProperty] private bool _slot2IsReadable = true;
    // Slot 3
    [ObservableProperty] private string _slot3Label = "";
    [ObservableProperty] private string _slot3ValueDisplay = "";
    [ObservableProperty] private string _slot3Unit = "";
    [ObservableProperty] private double _slot3YMax = 100;
    [ObservableProperty] private bool _slot3IsVisible;
    [ObservableProperty] private string _slot3Color = ChartColors.SlotLineColors[3];
    [ObservableProperty] private bool _slot3IsReadable = true;

    /// <summary>Notify View to refresh all charts</summary>
    public event Action? ChartDataUpdated;
    public event Action? ChartDataCleared;
    public event Action<int>? SamplingIntervalChanged;

    /// <summary>Request View to open ChartConfigDialog for a slot</summary>
    public event Action<int>? OpenChartConfigRequested;

    // --- TB3-3: Alert history ---
    public ObservableCollection<AlertHistoryItem> AlertHistoryItems { get; } = new();

    public MainViewModel()
    {
        // Design-time only
        _timer = new DispatcherTimer();
        for (int i = 0; i < 4; i++) _slots[i] = new ChartSlotState();
    }

    public MainViewModel(bool initialize)
    {
        _timer = new DispatcherTimer();
        for (int i = 0; i < 4; i++) _slots[i] = new ChartSlotState();
        if (!initialize) return;

        // Determine GPU name from best provider
        var gpuProvider = ServiceLocator.ProvidersByMode.GetValueOrDefault(HardwareMode.Gpu)
                          ?? ServiceLocator.GpuProvider;
        if (gpuProvider != null)
        {
            GpuName = gpuProvider.GetGpuName();
            if (GpuName is "Unknown" or "N/A") GpuName = "GPU Detect Error";
            UpdatePowerLimitDisplay(gpuProvider);
        }
        else
        {
            GpuName = "No GPU";
            PowerLimitDisplay = "";
        }

        // Initialize buffers
        var samplingInterval = ServiceLocator.SettingsService?.Load().SamplingIntervalSeconds ?? 1;
        _maxPoints = HistoryDurationSeconds;
        InitializeSlotBuffers();

        // Load chart slot configs
        LoadChartSlotConfigs();

        // Timer
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(samplingInterval) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        // Status subscriptions
        InitializeStatusSubscriptions();

        Log.Information("SendAlerts started (Flexible Charts): {GpuName}", GpuName);
    }

    private void InitializeSlotBuffers()
    {
        for (int i = 0; i < 4; i++)
        {
            _slots[i].Buffer = new double[_maxPoints];
            Array.Fill(_slots[i].Buffer, double.NaN);
            _slots[i].BufferIndex = 0;
            _slots[i].BufferCount = 0;
            _slots[i].Sum = 0;
            _slots[i].Peak = 0;
        }
    }

    private static List<ChartSlotConfig> BuildDefaultChartSlots()
    {
        var hasGpu = ServiceLocator.ProvidersByMode.ContainsKey(HardwareMode.Gpu);
        var slots = new List<ChartSlotConfig>();
        if (hasGpu)
        {
            slots.Add(new ChartSlotConfig { SourceType = ChartSlotSourceType.BuiltIn, BuiltInPreset = BuiltInChartPreset.GpuCoreUtilization });
            slots.Add(new ChartSlotConfig { SourceType = ChartSlotSourceType.BuiltIn, BuiltInPreset = BuiltInChartPreset.GpuTemperature });
            slots.Add(new ChartSlotConfig { SourceType = ChartSlotSourceType.BuiltIn, BuiltInPreset = BuiltInChartPreset.GpuPowerUsage });
        }
        else
        {
            slots.Add(new ChartSlotConfig { SourceType = ChartSlotSourceType.BuiltIn, BuiltInPreset = BuiltInChartPreset.CpuUtilization });
            slots.Add(new ChartSlotConfig { SourceType = ChartSlotSourceType.BuiltIn, BuiltInPreset = BuiltInChartPreset.MemoryUsage });
            slots.Add(new ChartSlotConfig { SourceType = ChartSlotSourceType.BuiltIn, BuiltInPreset = BuiltInChartPreset.NetworkIO });
        }
        slots.Add(new ChartSlotConfig { SourceType = ChartSlotSourceType.Off });
        return slots;
    }

    private void LoadChartSlotConfigs()
    {
        var settings = ServiceLocator.SettingsService?.Load();
        List<ChartSlotConfig> slotConfigs;

        if (settings == null || settings.ChartSlots.Count < 4)
        {
            // No config or incomplete — use defaults
            slotConfigs = BuildDefaultChartSlots();
            if (settings != null)
            {
                settings.ChartSlots = new List<ChartSlotConfig>(slotConfigs.Select(c => c.Clone()));
                ServiceLocator.SettingsService?.Save(settings);
            }
        }
        else
        {
            slotConfigs = settings.ChartSlots;
        }

        for (int i = 0; i < 4; i++)
        {
            ApplySlotConfig(i, slotConfigs[i], save: false);
        }
    }

    /// <summary>
    /// Apply a configuration to a slot. Handles label, unit, YMax, active state.
    /// </summary>
    public void ApplySlotConfig(int index, ChartSlotConfig config, bool save = true)
    {
        if (index < 0 || index >= 4) return;

        var slot = _slots[index];
        slot.Config = config;
        slot.ClearBuffer();
        slot.NetworkScaleMB = false;
        slot.UnavailableLogged = false;
        slot.RetryCounter = 0;
        slot.AppliedSensor = null;

        switch (config.SourceType)
        {
            case ChartSlotSourceType.Off:
                slot.IsActive = false;
                slot.Label = "";
                slot.Unit = "";
                slot.YMax = 100;
                break;

            case ChartSlotSourceType.BuiltIn:
                ApplyBuiltInPreset(slot, config.BuiltInPreset);
                break;

            case ChartSlotSourceType.External:
                slot.IsActive = true;
                slot.Label = config.SensorEntry ?? "External";
                slot.Unit = "";
                slot.YMax = 0.1;
                slot.Peak = 0;
                // Try to auto-apply sensor
                TryAutoApplyExternalSensor(index);
                break;
        }

        UpdateSlotDisplayProperties(index);

        if (save) SaveChartSlotConfigs();
    }

    private void ApplyBuiltInPreset(ChartSlotState slot, BuiltInChartPreset preset)
    {
        slot.IsActive = preset != BuiltInChartPreset.None;

        switch (preset)
        {
            case BuiltInChartPreset.GpuCoreUtilization:
                slot.Label = "GPU Core Utilization";
                slot.Unit = "%";
                slot.YMax = 100;
                break;
            case BuiltInChartPreset.GpuTemperature:
                slot.Label = "GPU Temperature";
                slot.Unit = "\u00b0C";
                slot.YMax = 100;
                break;
            case BuiltInChartPreset.GpuPowerUsage:
                slot.Label = "Power Usage";
                slot.Unit = "W";
                slot.YMax = 50;
                break;
            case BuiltInChartPreset.CpuUtilization:
                slot.Label = "CPU Utilization";
                slot.Unit = "%";
                slot.YMax = 100;
                break;
            case BuiltInChartPreset.MemoryUsage:
                slot.Label = "Memory Usage";
                slot.Unit = "%";
                slot.YMax = 100;
                break;
            case BuiltInChartPreset.NetworkIO:
                slot.Label = "Network I/O";
                slot.Unit = "KB/s";
                slot.YMax = 50;
                break;
            default:
                slot.Label = "";
                slot.Unit = "";
                slot.YMax = 100;
                slot.IsActive = false;
                break;
        }
    }

    private void TryAutoApplyExternalSensor(int index)
    {
        var slot = _slots[index];
        var config = slot.Config;
        if (string.IsNullOrEmpty(config.SensorName) || string.IsNullOrEmpty(config.SensorEntry))
            return;

        var provider = GetExternalProvider(config.ExternalSource);
        if (provider is null || !provider.IsAvailable) return;

        // Try to find and apply sensor
        var groups = provider.GetSensorGroups();
        foreach (var group in groups)
        {
            foreach (var entry in group.Entries)
            {
                if (group.SensorName == config.SensorName && entry.LabelOrig == config.SensorEntry)
                {
                    slot.AppliedSensor = new HwinfoSensorItem(group.SensorName, entry.LabelOrig, entry.Label, entry.Unit);
                    slot.Label = entry.Label;
                    slot.Unit = entry.Unit;
                    var sourceName = config.ExternalSource == ChartSourceType.HWiNFO ? "HWiNFO" : "LHM";
                    Log.Information("[Chart] Slot {Index} auto-restored external sensor: {Source}:{Name}", index, sourceName, entry.Label);
                    UpdateSlotDisplayProperties(index);
                    return;
                }
            }
        }
    }

    internal void UpdateSlotDisplayProperties(int index)
    {
        var slot = _slots[index];
        var label = slot.Label;
        var value = slot.IsActive ? FormatValue(slot.CurrentValue) : "";
        var unit = slot.Unit;
        var yMax = slot.YMax;
        var visible = slot.IsActive;
        var readable = slot.Config.SourceType != ChartSlotSourceType.External || slot.AppliedSensor != null;

        switch (index)
        {
            case 0:
                Slot0Label = label; Slot0ValueDisplay = value; Slot0Unit = unit;
                Slot0YMax = yMax; Slot0IsVisible = visible; Slot0IsReadable = readable;
                break;
            case 1:
                Slot1Label = label; Slot1ValueDisplay = value; Slot1Unit = unit;
                Slot1YMax = yMax; Slot1IsVisible = visible; Slot1IsReadable = readable;
                break;
            case 2:
                Slot2Label = label; Slot2ValueDisplay = value; Slot2Unit = unit;
                Slot2YMax = yMax; Slot2IsVisible = visible; Slot2IsReadable = readable;
                break;
            case 3:
                Slot3Label = label; Slot3ValueDisplay = value; Slot3Unit = unit;
                Slot3YMax = yMax; Slot3IsVisible = visible; Slot3IsReadable = readable;
                break;
        }
    }

    internal static string FormatValue(double value)
    {
        if (double.IsNaN(value)) return "--";
        if (value == Math.Floor(value)) return value.ToString("F0");
        if (Math.Abs(value * 10 - Math.Round(value * 10)) < 0.001) return value.ToString("F1");
        if (Math.Abs(value * 100 - Math.Round(value * 100)) < 0.01) return value.ToString("F2");
        return value.ToString("F3");
    }

    private void UpdatePowerLimitDisplay(IGpuProvider provider)
    {
        var powerLimit = provider.PowerLimit;
        if (provider.Mode == HardwareMode.Gpu)
        {
            if (powerLimit <= 0)
            {
                var tdp = GpuTdpLookup.FindTdp(GpuName);
                PowerLimitDisplay = tdp.HasValue ? $"(TDP: ~{tdp.Value}W est.)" : "(TDP: N/A)";
            }
            else
            {
                PowerLimitDisplay = $"(TDP: {powerLimit:F0}W)";
            }
        }
        else
        {
            PowerLimitDisplay = $"(Mem: {powerLimit:F0}G)";
        }
    }

    /// <summary>Get buffer snapshot for View (time-ordered)</summary>
    public double[] GetBufferSnapshot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= 4) return Array.Empty<double>();
        var slot = _slots[slotIndex];
        var count = slot.BufferCount;
        var result = new double[count];
        if (count == 0) return result;

        if (count < _maxPoints)
        {
            Array.Copy(slot.Buffer, 0, result, 0, count);
        }
        else
        {
            var tailLen = _maxPoints - slot.BufferIndex;
            Array.Copy(slot.Buffer, slot.BufferIndex, result, 0, tailLen);
            Array.Copy(slot.Buffer, 0, result, tailLen, slot.BufferIndex);
        }
        return result;
    }

    public int GetSlotBufferCount(int slotIndex) =>
        slotIndex >= 0 && slotIndex < 4 ? _slots[slotIndex].BufferCount : 0;

    public void UpdateSamplingInterval(int seconds)
    {
        seconds = Math.Clamp(seconds, AppConstants.SamplingIntervalMin, AppConstants.SamplingIntervalMax);
        var oldInterval = (int)_timer.Interval.TotalSeconds;
        _timer.Interval = TimeSpan.FromSeconds(seconds);

        if (seconds != oldInterval)
        {
            _timer.Stop();
            InitializeSlotBuffers();
            ChartDataCleared?.Invoke();
            SamplingIntervalChanged?.Invoke(seconds);
            _timer.Start();
            Log.Information("Sampling interval updated: {Old}s -> {New}s", oldInterval, seconds);
        }
    }

    // ==========================================================================
    // Timer Tick — unified loop for all 4 slots
    // ==========================================================================

    private void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            // Cache readings per HardwareMode to avoid duplicate calls
            var readings = new Dictionary<HardwareMode, GpuReading>();
            foreach (var (mode, provider) in ServiceLocator.ProvidersByMode)
            {
                try { readings[mode] = provider.GetCurrentReading(); }
                catch { /* provider error, skip */ }
            }

            // Process each slot
            for (int i = 0; i < 4; i++)
            {
                var slot = _slots[i];
                if (!slot.IsActive) continue;

                switch (slot.Config.SourceType)
                {
                    case ChartSlotSourceType.BuiltIn:
                        ProcessBuiltInSlot(i, slot, readings);
                        break;
                    case ChartSlotSourceType.External:
                        ProcessExternalSlot(i, slot);
                        break;
                }

                UpdateSlotDisplayProperties(i);
            }

            // Average summary (from first 3 slots if built-in)
            UpdateAvgSummary();

            // Notify View
            ChartDataUpdated?.Invoke();

            // TDP periodic refresh
            if (ServiceLocator.ProvidersByMode.TryGetValue(HardwareMode.Gpu, out var gpuProv))
            {
                _tdpRefreshCounter++;
                if (_tdpRefreshCounter >= TdpRefreshIntervalSeconds / _timer.Interval.TotalSeconds)
                {
                    _tdpRefreshCounter = 0;
                    gpuProv.RefreshPowerLimit();
                    UpdatePowerLimitDisplay(gpuProv);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during chart data reading");
        }
    }

    private void ProcessBuiltInSlot(int index, ChartSlotState slot, Dictionary<HardwareMode, GpuReading> readings)
    {
        var preset = slot.Config.BuiltInPreset;
        double value = double.NaN;

        switch (preset)
        {
            case BuiltInChartPreset.GpuCoreUtilization:
                if (readings.TryGetValue(HardwareMode.Gpu, out var gr1)) value = gr1.GpuUtilization;
                break;
            case BuiltInChartPreset.GpuTemperature:
                if (readings.TryGetValue(HardwareMode.Gpu, out var gr2)) value = gr2.Temperature;
                break;
            case BuiltInChartPreset.GpuPowerUsage:
                if (readings.TryGetValue(HardwareMode.Gpu, out var gr3)) value = gr3.PowerUsage;
                break;
            case BuiltInChartPreset.CpuUtilization:
                if (readings.TryGetValue(HardwareMode.CpuNetwork, out var cr1)) value = cr1.GpuUtilization;
                break;
            case BuiltInChartPreset.MemoryUsage:
                if (readings.TryGetValue(HardwareMode.CpuNetwork, out var cr2)) value = cr2.Temperature;
                break;
            case BuiltInChartPreset.NetworkIO:
                if (readings.TryGetValue(HardwareMode.CpuNetwork, out var cr3)) value = cr3.PowerUsage;
                break;
        }

        if (!double.IsNaN(value))
        {
            slot.CurrentValue = value;
            slot.LastTickValue = value;
            slot.WriteBuffer(value, _maxPoints);

            // Dynamic Y-axis for power/network presets
            UpdateDynamicYAxis(slot, preset, value);
        }
        else
        {
            slot.LastTickValue = double.NaN;
            slot.WriteBuffer(double.NaN, _maxPoints);
        }
    }

    private void UpdateDynamicYAxis(ChartSlotState slot, BuiltInChartPreset preset, double value)
    {
        switch (preset)
        {
            case BuiltInChartPreset.GpuPowerUsage:
                // 50W step, grow at 85%
                if (value > slot.YMax * 0.85)
                {
                    var newMax = Math.Ceiling(value * 1.5 / 50) * 50;
                    if (newMax > slot.YMax) slot.YMax = newMax;
                }
                break;

            case BuiltInChartPreset.NetworkIO:
                // KB/s → MB/s switch
                if (!slot.NetworkScaleMB && slot.YMax >= 1024)
                {
                    slot.NetworkScaleMB = true;
                    slot.Unit = "MB/s";
                    slot.YMax = Math.Max(1, Math.Ceiling(value / 1024.0 * 1.5));
                }

                if (slot.NetworkScaleMB)
                {
                    var currentMB = value / 1024.0;
                    if (currentMB > slot.YMax * 0.85)
                    {
                        var newMax = Math.Ceiling(currentMB * 1.5);
                        if (newMax > slot.YMax) slot.YMax = newMax;
                    }
                }
                else
                {
                    // 100-unit step
                    if (value > slot.YMax * 0.85)
                    {
                        var newMax = Math.Ceiling(value * 1.5 / 100) * 100;
                        if (newMax > slot.YMax) slot.YMax = newMax;
                    }
                }
                break;

            // Fixed Y-axis presets: no action needed
        }
    }

    private void ProcessExternalSlot(int index, ChartSlotState slot)
    {
        if (slot.AppliedSensor is not { } sensor)
        {
            // No sensor applied yet — retry periodically
            slot.RetryCounter++;
            if (slot.RetryCounter % 5 == 0)
                TryAutoApplyExternalSensor(index);
            return;
        }

        var provider = GetExternalProvider(slot.Config.ExternalSource);
        if (provider is null) return;

        var entry = provider.ReadEntry(sensor.SensorName, sensor.LabelOrig);
        if (entry is not null)
        {
            slot.UnavailableLogged = false;
            slot.CurrentValue = entry.Value;
            slot.LastTickValue = entry.Value;
            slot.WriteBuffer(entry.Value, _maxPoints);

            // Dynamic Y: peak * 1.1
            if (entry.Value > slot.Peak) slot.Peak = entry.Value;
            slot.YMax = slot.Peak > 0 ? slot.Peak * 1.1 : 0.1;
        }
        else
        {
            slot.LastTickValue = double.NaN;
            slot.WriteBuffer(double.NaN, _maxPoints);
            if (!slot.UnavailableLogged)
            {
                Log.Warning("[Chart] Slot {Index} external sensor read failed", index);
                slot.UnavailableLogged = true;
            }
        }
    }

    private static ISensorDataProvider? GetExternalProvider(ChartSourceType source) => source switch
    {
        ChartSourceType.HWiNFO => ServiceLocator.HwinfoProvider as ISensorDataProvider,
        ChartSourceType.LibreHardwareMonitor => ServiceLocator.LhmSensorProvider,
        _ => null
    };

    private void UpdateAvgSummary()
    {
        // Build average from active built-in slots (first 3)
        var parts = new List<string>();
        for (int i = 0; i < Math.Min(3, 4); i++)
        {
            var slot = _slots[i];
            if (!slot.IsActive || slot.Config.SourceType != ChartSlotSourceType.BuiltIn || slot.BufferCount == 0)
                continue;

            var avg = slot.Sum / slot.BufferCount;
            parts.Add($"{avg:F1}{slot.Unit}");
        }

        AvgSummary = parts.Count > 0 ? $"Avg: {string.Join(" | ", parts)}" : "";
    }

    // ==========================================================================
    // Commands
    // ==========================================================================

    [RelayCommand]
    private void OpenSlotConfig(string slotIndexStr)
    {
        if (int.TryParse(slotIndexStr, out var slotIndex))
            OpenChartConfigRequested?.Invoke(slotIndex);
    }

    /// <summary>
    /// Check if a built-in preset is already used by another slot.
    /// Returns the slot index using it, or -1 if available.
    /// </summary>
    public int FindPresetUsedBySlot(BuiltInChartPreset preset, int excludeSlot)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i == excludeSlot) continue;
            if (_slots[i].Config.SourceType == ChartSlotSourceType.BuiltIn &&
                _slots[i].Config.BuiltInPreset == preset)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Check if a HardwareMode has an available provider.
    /// </summary>
    public static bool IsPresetAvailable(BuiltInChartPreset preset)
    {
        return preset switch
        {
            BuiltInChartPreset.GpuCoreUtilization or
            BuiltInChartPreset.GpuTemperature or
            BuiltInChartPreset.GpuPowerUsage =>
                ServiceLocator.ProvidersByMode.ContainsKey(HardwareMode.Gpu),
            BuiltInChartPreset.CpuUtilization or
            BuiltInChartPreset.MemoryUsage or
            BuiltInChartPreset.NetworkIO =>
                ServiceLocator.ProvidersByMode.ContainsKey(HardwareMode.CpuNetwork),
            _ => false
        };
    }

    public ChartSlotConfig GetSlotConfig(int index) =>
        index >= 0 && index < 4 ? _slots[index].Config.Clone() : new ChartSlotConfig();

    private void SaveChartSlotConfigs()
    {
        var service = ServiceLocator.SettingsService;
        if (service is null) return;

        var settings = service.Load();
        settings.ChartSlots.Clear();
        for (int i = 0; i < 4; i++)
            settings.ChartSlots.Add(_slots[i].Config.Clone());
        service.Save(settings);
    }

    // ==========================================================================
    // Status subscriptions (TB3-2/TB3-3)
    // ==========================================================================

    private void InitializeStatusSubscriptions()
    {
        ServiceLocator.PipeStatusChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsPipeServerRunning = ServiceLocator.IsPipeServerRunning;
                UpdateStatusText();
            });
        };

        IsPipeServerRunning = ServiceLocator.IsPipeServerRunning;
        UpdateStatusText();

        ServiceLocator.AlertHistoryChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(RefreshAlertHistory);
        };
        RefreshAlertHistory();
    }

    private void UpdateStatusText()
    {
        StatusText = IsPipeServerRunning
            ? "Alert Center Ready - Listening for alerts"
            : "Display Only Mode";
    }

    private void RefreshAlertHistory()
    {
        AlertHistoryItems.Clear();
        foreach (var item in ServiceLocator.AlertHistory)
            AlertHistoryItems.Add(item);
    }

    private static string GetVersionString()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return info?.InformationalVersion is { } v ? $"v{v}" : "v?";
    }
}
