using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Core.Interfaces;
using SendAlerts.Interfaces;
using SendAlerts.Models;
using SendAlerts.Services;
using Serilog;

namespace SendAlerts.ViewModels;

public partial class ChartConfigDialogViewModel : ViewModelBase
{
    private readonly int _slotIndex;
    private readonly MainViewModel _mainVm;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    // Source type radio buttons
    [ObservableProperty] private bool _isOffSelected;
    [ObservableProperty] private bool _isBuiltInSelected;
    [ObservableProperty] private bool _isExternalSelected;

    // Built-in preset list
    public ObservableCollection<PresetItem> PresetItems { get; } = new();
    [ObservableProperty] private PresetItem? _selectedPreset;

    // External sensor
    [ObservableProperty] private int _selectedExternalSourceIndex; // 0=HWiNFO, 1=LHM
    [ObservableProperty] private string _sensorFilterText = "";
    public ObservableCollection<HwinfoSensorItem> FilteredSensorItems { get; } = new();
    [ObservableProperty] private HwinfoSensorItem? _selectedSensor;

    private readonly List<HwinfoSensorItem> _allSensorItems = new();

    public ChartSlotConfig? Result { get; private set; }

    public string Title { get; }

    public ChartConfigDialogViewModel()
    {
        // Design-time
        _mainVm = new MainViewModel();
        Title = "Chart Config";
    }

    public ChartConfigDialogViewModel(int slotIndex, MainViewModel mainVm)
    {
        _slotIndex = slotIndex;
        _mainVm = mainVm;
        Title = $"{_loc["Chart_ConfigTitle"] ?? "Chart Config"} — Slot {slotIndex + 1}";

        var config = mainVm.GetSlotConfig(slotIndex);

        // Initialize source type
        switch (config.SourceType)
        {
            case ChartSlotSourceType.Off: IsOffSelected = true; break;
            case ChartSlotSourceType.BuiltIn: IsBuiltInSelected = true; break;
            case ChartSlotSourceType.External: IsExternalSelected = true; break;
        }

        // Build preset list
        foreach (BuiltInChartPreset preset in Enum.GetValues<BuiltInChartPreset>())
        {
            if (preset == BuiltInChartPreset.None) continue;

            var usedBy = mainVm.FindPresetUsedBySlot(preset, slotIndex);
            var available = MainViewModel.IsPresetAvailable(preset);

            var item = new PresetItem
            {
                Preset = preset,
                DisplayName = GetPresetDisplayName(preset),
                IsEnabled = available && usedBy < 0,
                StatusHint = !available ? "(N/A)" : usedBy >= 0 ? $"(Chart {usedBy + 1})" : ""
            };
            PresetItems.Add(item);

            if (config.SourceType == ChartSlotSourceType.BuiltIn && config.BuiltInPreset == preset)
                SelectedPreset = item;
        }

        // External source
        if (config.SourceType == ChartSlotSourceType.External)
        {
            SelectedExternalSourceIndex = config.ExternalSource == ChartSourceType.LibreHardwareMonitor ? 1 : 0;
        }

        RefreshSensorList();

        // Try to select current sensor
        if (config.SourceType == ChartSlotSourceType.External &&
            !string.IsNullOrEmpty(config.SensorName) && !string.IsNullOrEmpty(config.SensorEntry))
        {
            foreach (var item in FilteredSensorItems)
            {
                if (item.SensorName == config.SensorName && item.LabelOrig == config.SensorEntry)
                {
                    SelectedSensor = item;
                    break;
                }
            }
        }
    }

    partial void OnSelectedExternalSourceIndexChanged(int value) => RefreshSensorList();
    partial void OnSensorFilterTextChanged(string value) => ApplySensorFilter();

    [RelayCommand]
    private void RefreshSensorList()
    {
        _allSensorItems.Clear();
        FilteredSensorItems.Clear();

        var source = SelectedExternalSourceIndex == 1
            ? ChartSourceType.LibreHardwareMonitor
            : ChartSourceType.HWiNFO;

        ISensorDataProvider? provider = source switch
        {
            ChartSourceType.HWiNFO => ServiceLocator.HwinfoProvider as ISensorDataProvider,
            ChartSourceType.LibreHardwareMonitor => ServiceLocator.LhmSensorProvider,
            _ => null
        };

        if (provider is null || !provider.IsAvailable) return;

        var groups = provider.GetSensorGroups();
        foreach (var group in groups)
        {
            foreach (var entry in group.Entries)
            {
                _allSensorItems.Add(new HwinfoSensorItem(
                    group.SensorName, entry.LabelOrig, entry.Label, entry.Unit));
            }
        }

        ApplySensorFilter();
    }

    private void ApplySensorFilter()
    {
        var prev = SelectedSensor;
        FilteredSensorItems.Clear();
        var filter = SensorFilterText?.Trim() ?? "";

        foreach (var item in _allSensorItems)
        {
            if (filter.Length == 0 ||
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredSensorItems.Add(item);
            }
        }

        if (prev is not null && FilteredSensorItems.Contains(prev))
            SelectedSensor = prev;
    }

    public bool TryBuildResult()
    {
        if (IsOffSelected)
        {
            Result = new ChartSlotConfig { SourceType = ChartSlotSourceType.Off };
            return true;
        }

        if (IsBuiltInSelected)
        {
            if (SelectedPreset is null || !SelectedPreset.IsEnabled) return false;
            Result = new ChartSlotConfig
            {
                SourceType = ChartSlotSourceType.BuiltIn,
                BuiltInPreset = SelectedPreset.Preset
            };
            return true;
        }

        if (IsExternalSelected)
        {
            if (SelectedSensor is null) return false;
            Result = new ChartSlotConfig
            {
                SourceType = ChartSlotSourceType.External,
                ExternalSource = SelectedExternalSourceIndex == 1
                    ? ChartSourceType.LibreHardwareMonitor
                    : ChartSourceType.HWiNFO,
                SensorName = SelectedSensor.SensorName,
                SensorEntry = SelectedSensor.LabelOrig
            };
            return true;
        }

        return false;
    }

    private static string GetPresetDisplayName(BuiltInChartPreset preset) => preset switch
    {
        BuiltInChartPreset.GpuCoreUtilization => "GPU Core Utilization",
        BuiltInChartPreset.GpuTemperature => "GPU Temperature",
        BuiltInChartPreset.GpuPowerUsage => "GPU Power Usage",
        BuiltInChartPreset.CpuUtilization => "CPU Utilization",
        BuiltInChartPreset.MemoryUsage => "Memory Usage",
        BuiltInChartPreset.NetworkIO => "Network I/O",
        _ => preset.ToString()
    };
}

public class PresetItem
{
    public BuiltInChartPreset Preset { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string StatusHint { get; set; } = "";
    public string FullDisplay => string.IsNullOrEmpty(StatusHint) ? DisplayName : $"{DisplayName}  {StatusHint}";
}
