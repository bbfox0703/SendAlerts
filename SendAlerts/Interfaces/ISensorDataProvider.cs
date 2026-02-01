using System;
using System.Collections.Generic;
using SendAlerts.Models;

namespace SendAlerts.Core.Interfaces;

/// <summary>
/// Generic sensor data provider interface for chart data sources.
/// Implemented by HWiNFO, LibreHardwareMonitor, etc.
/// </summary>
public interface ISensorDataProvider : IDisposable
{
    /// <summary>
    /// Display name of this data source (e.g. "HWiNFO", "LibreHardwareMonitor")
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Whether the data source is currently available/readable
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Get all sensor groups, optionally filtered by name keywords
    /// </summary>
    List<HwinfoSensorGroup> GetSensorGroups(params string[] nameFilters);

    /// <summary>
    /// Read a specific sensor entry's current value
    /// </summary>
    HwinfoSensorEntry? ReadEntry(string sensorName, string labelOrig);
}
