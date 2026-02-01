using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using Serilog;

namespace SendAlerts.Desktop.Implementations;

/// <summary>
/// HWiNFO64 Shared Memory Reader
/// 透過 Memory Mapped File 讀取 HWiNFO64 的感測器資料
///
/// HWiNFO SHM Layout:
///   Header (HwinfoShmHeader)
///   → Sensor Sections (HwinfoShmSensor[])
///   → Reading Entries (HwinfoShmReading[])
/// </summary>
public class HwinfoSharedMemoryReader : IHwinfoProvider
{
    private const string ShmName = "Global\\HWiNFO_SENS_SM2";

    // HWiNFO Shared Memory 結構定義
    // Reference: https://www.hwinfo.com/forum/threads/shared-memory-layout.5049/

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct HwinfoShmHeader
    {
        public uint Signature;         // "HWiS" = 0x53695748
        public uint Version;           // SHM version
        public uint Revision;          // SHM revision
        public long PollTime;          // Polling time in ms (Int64)
        public uint OffsetOfSensorSection;
        public uint SizeOfSensorElement;
        public uint NumSensorElements;
        public uint OffsetOfReadingSection;
        public uint SizeOfReadingElement;
        public uint NumReadingElements;
    }

    private enum HwinfoReadingType : uint
    {
        None = 0,
        Temp = 1,
        Voltage = 2,
        Fan = 3,
        Current = 4,
        Power = 5,
        Clock = 6,
        Usage = 7,
        Other = 8
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct HwinfoShmSensor
    {
        public uint SensorId;
        public uint SensorInst;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SensorNameOrig;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SensorNameUser;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    private struct HwinfoShmReading
    {
        public HwinfoReadingType ReadingType;
        public uint SensorIndex;
        public uint ReadingId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string LabelOrig;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string LabelUser;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string Unit;
        public double Value;
        public double ValueMin;
        public double ValueMax;
        public double ValueAvg;
    }

    private const uint ExpectedSignature = 0x53695748; // "HWiS"

    public bool IsAvailable
    {
        get
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(ShmName, MemoryMappedFileRights.Read);
                using var accessor = mmf.CreateViewAccessor(0, Marshal.SizeOf<HwinfoShmHeader>(), MemoryMappedFileAccess.Read);
                accessor.Read(0, out HwinfoShmHeader header);
                return header.Signature == ExpectedSignature;
            }
            catch
            {
                return false;
            }
        }
    }

    public List<HwinfoSensorGroup> GetSensorGroups(params string[] nameFilters)
    {
        var groups = new List<HwinfoSensorGroup>();

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(ShmName, MemoryMappedFileRights.Read);
            using var accessor = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);

            var headerSize = Marshal.SizeOf<HwinfoShmHeader>();
            var headerBytes = new byte[headerSize];
            accessor.Read(headerBytes, 0, headerSize);
            var header = BytesToStruct<HwinfoShmHeader>(headerBytes);

            if (header.Signature != ExpectedSignature)
                return groups;

            // Read sensors
            var sensors = new HwinfoShmSensor[header.NumSensorElements];
            for (uint i = 0; i < header.NumSensorElements; i++)
            {
                var offset = header.OffsetOfSensorSection + i * header.SizeOfSensorElement;
                accessor.Position = offset;
                var sensorBytes = new byte[header.SizeOfSensorElement];
                accessor.Read(sensorBytes, 0, (int)Math.Min(header.SizeOfSensorElement, (uint)Marshal.SizeOf<HwinfoShmSensor>()));
                sensors[i] = BytesToStruct<HwinfoShmSensor>(sensorBytes);
            }

            // Read readings
            var readings = new HwinfoShmReading[header.NumReadingElements];
            for (uint i = 0; i < header.NumReadingElements; i++)
            {
                var offset = header.OffsetOfReadingSection + i * header.SizeOfReadingElement;
                accessor.Position = offset;
                var readingBytes = new byte[header.SizeOfReadingElement];
                accessor.Read(readingBytes, 0, (int)Math.Min(header.SizeOfReadingElement, (uint)Marshal.SizeOf<HwinfoShmReading>()));
                readings[i] = BytesToStruct<HwinfoShmReading>(readingBytes);
            }

            // Group readings by sensor
            var sensorMap = new Dictionary<uint, List<HwinfoSensorEntry>>();
            foreach (var reading in readings)
            {
                if (reading.ReadingType == HwinfoReadingType.None)
                    continue;

                if (!sensorMap.ContainsKey(reading.SensorIndex))
                    sensorMap[reading.SensorIndex] = new List<HwinfoSensorEntry>();

                var label = string.IsNullOrWhiteSpace(reading.LabelUser) ? reading.LabelOrig : reading.LabelUser;
                sensorMap[reading.SensorIndex].Add(new HwinfoSensorEntry(
                    label?.Trim() ?? "",
                    reading.LabelOrig?.Trim() ?? "",
                    reading.Unit?.Trim() ?? "",
                    reading.Value,
                    reading.ValueMin,
                    reading.ValueMax,
                    reading.ValueAvg));
            }

            // Build groups
            for (uint i = 0; i < header.NumSensorElements; i++)
            {
                var sensorName = string.IsNullOrWhiteSpace(sensors[i].SensorNameUser)
                    ? sensors[i].SensorNameOrig
                    : sensors[i].SensorNameUser;
                sensorName = sensorName?.Trim() ?? $"Sensor {i}";

                // Apply name filters
                if (nameFilters.Length > 0)
                {
                    var match = nameFilters.Any(f =>
                        sensorName.Contains(f, StringComparison.OrdinalIgnoreCase));
                    if (!match) continue;
                }

                if (sensorMap.TryGetValue(i, out var entries) && entries.Count > 0)
                {
                    groups.Add(new HwinfoSensorGroup(sensorName, entries));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[HWiNFO] Failed to read Shared Memory");
        }

        return groups;
    }

    public HwinfoSensorEntry? ReadEntry(string sensorName, string labelOrig)
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(ShmName, MemoryMappedFileRights.Read);
            using var accessor = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);

            var headerSize = Marshal.SizeOf<HwinfoShmHeader>();
            var headerBytes = new byte[headerSize];
            accessor.Read(headerBytes, 0, headerSize);
            var header = BytesToStruct<HwinfoShmHeader>(headerBytes);

            if (header.Signature != ExpectedSignature)
                return null;

            // Find sensor index by name
            uint? targetSensorIndex = null;
            for (uint i = 0; i < header.NumSensorElements; i++)
            {
                var offset = header.OffsetOfSensorSection + i * header.SizeOfSensorElement;
                accessor.Position = offset;
                var sensorBytes = new byte[header.SizeOfSensorElement];
                accessor.Read(sensorBytes, 0, (int)Math.Min(header.SizeOfSensorElement, (uint)Marshal.SizeOf<HwinfoShmSensor>()));
                var sensor = BytesToStruct<HwinfoShmSensor>(sensorBytes);

                var name = string.IsNullOrWhiteSpace(sensor.SensorNameUser)
                    ? sensor.SensorNameOrig
                    : sensor.SensorNameUser;
                if (string.Equals(name?.Trim(), sensorName, StringComparison.OrdinalIgnoreCase))
                {
                    targetSensorIndex = i;
                    break;
                }
            }

            if (targetSensorIndex is null)
                return null;

            // Find reading
            for (uint i = 0; i < header.NumReadingElements; i++)
            {
                var offset = header.OffsetOfReadingSection + i * header.SizeOfReadingElement;
                accessor.Position = offset;
                var readingBytes = new byte[header.SizeOfReadingElement];
                accessor.Read(readingBytes, 0, (int)Math.Min(header.SizeOfReadingElement, (uint)Marshal.SizeOf<HwinfoShmReading>()));
                var reading = BytesToStruct<HwinfoShmReading>(readingBytes);

                if (reading.SensorIndex == targetSensorIndex.Value &&
                    string.Equals(reading.LabelOrig?.Trim(), labelOrig, StringComparison.OrdinalIgnoreCase))
                {
                    var label = string.IsNullOrWhiteSpace(reading.LabelUser) ? reading.LabelOrig : reading.LabelUser;
                    return new HwinfoSensorEntry(
                        label?.Trim() ?? "",
                        reading.LabelOrig?.Trim() ?? "",
                        reading.Unit?.Trim() ?? "",
                        reading.Value,
                        reading.ValueMin,
                        reading.ValueMax,
                        reading.ValueAvg);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[HWiNFO] Failed to read sensor {Sensor}/{Label}", sensorName, labelOrig);
        }

        return null;
    }

    private static T BytesToStruct<T>(byte[] bytes) where T : struct
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    public void Dispose()
    {
        // No persistent handles to dispose — each read opens/closes the MMF
    }
}
