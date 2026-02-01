using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// LibreHardwareMonitor sensor data provider for the chart.
/// Exposes all hardware sensors via ISensorDataProvider interface.
/// </summary>
public class LhmSensorProvider : ISensorDataProvider
{
    private readonly Computer _computer;
    private bool _disposed;

    public string SourceName => "LibreHardwareMonitor";
    public bool IsAvailable { get; private set; }

    public LhmSensorProvider()
    {
        _computer = new Computer
        {
            IsGpuEnabled = true,
            IsCpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true
        };

        try
        {
            _computer.Open();
            IsAvailable = _computer.Hardware.Count > 0;
            Log.Information("[LHM] Provider initialized, hardware count: {Count}", _computer.Hardware.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LHM] Failed to initialize LibreHardwareMonitor");
            IsAvailable = false;
        }
    }

    public List<HwinfoSensorGroup> GetSensorGroups(params string[] nameFilters)
    {
        var groups = new List<HwinfoSensorGroup>();
        if (!IsAvailable) return groups;

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                CollectHardware(hardware, groups, nameFilters);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[LHM] Failed to enumerate sensors");
        }

        return groups;
    }

    private static void CollectHardware(IHardware hardware, List<HwinfoSensorGroup> groups, string[] nameFilters)
    {
        hardware.Update();

        var sensorName = hardware.Name;

        // Apply name filters
        if (nameFilters.Length > 0)
        {
            var match = nameFilters.Any(f =>
                sensorName.Contains(f, StringComparison.OrdinalIgnoreCase));
            if (!match)
            {
                // Still check sub-hardware
                foreach (var sub in hardware.SubHardware)
                    CollectHardware(sub, groups, nameFilters);
                return;
            }
        }

        var entries = new List<HwinfoSensorEntry>();
        foreach (var sensor in hardware.Sensors)
        {
            // Use "SensorType/Name" as LabelOrig to ensure uniqueness
            var labelOrig = $"{sensor.SensorType}/{sensor.Name}";
            var label = $"{sensor.Name} [{sensor.SensorType}]";
            var unit = SensorTypeToUnit(sensor.SensorType);
            var value = sensor.Value ?? 0;

            entries.Add(new HwinfoSensorEntry(
                label, labelOrig, unit,
                value,
                sensor.Min ?? 0,
                sensor.Max ?? 0,
                0)); // LHM doesn't track avg
        }

        if (entries.Count > 0)
        {
            groups.Add(new HwinfoSensorGroup(sensorName, entries));
        }

        // Recurse into sub-hardware
        foreach (var sub in hardware.SubHardware)
        {
            CollectHardware(sub, groups, nameFilters);
        }
    }

    public HwinfoSensorEntry? ReadEntry(string sensorName, string labelOrig)
    {
        if (!IsAvailable) return null;

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                var result = FindAndReadEntry(hardware, sensorName, labelOrig);
                if (result is not null) return result;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[LHM] Failed to read sensor {Sensor}/{Label}", sensorName, labelOrig);
        }

        return null;
    }

    private static HwinfoSensorEntry? FindAndReadEntry(IHardware hardware, string sensorName, string labelOrig)
    {
        if (string.Equals(hardware.Name, sensorName, StringComparison.OrdinalIgnoreCase))
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                var sensorLabelOrig = $"{sensor.SensorType}/{sensor.Name}";
                if (string.Equals(sensorLabelOrig, labelOrig, StringComparison.OrdinalIgnoreCase) &&
                    sensor.Value.HasValue)
                {
                    var label = $"{sensor.Name} [{sensor.SensorType}]";
                    var unit = SensorTypeToUnit(sensor.SensorType);
                    return new HwinfoSensorEntry(
                        label, sensorLabelOrig, unit,
                        sensor.Value.Value,
                        sensor.Min ?? 0,
                        sensor.Max ?? 0,
                        0);
                }
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            var result = FindAndReadEntry(sub, sensorName, labelOrig);
            if (result is not null) return result;
        }

        return null;
    }

    private static string SensorTypeToUnit(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Power => "W",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "\u00b0C",
        SensorType.Load => "%",
        SensorType.Frequency => "Hz",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Factor => "",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.TimeSpan => "s",
        SensorType.Energy => "Wh",
        SensorType.Noise => "dBA",
        SensorType.Humidity => "%",
        _ => ""
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _computer.Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[LHM] Error closing Computer");
        }
    }
}
