// ==========================================================================
// NvI2CScanner — NVIDIA GPU I2C Bus Scanner
//
// Scans the I2C bus on NVIDIA GPUs via NvAPI to discover devices.
// READ-ONLY tool, no writes are performed.
//
// Usage:  dotnet run                          (quick scan)
//         dotnet run -- --full                (all addresses 0x03-0x77)
//         dotnet run -- --diag                (diagnostic: show error codes)
//         dotnet run -- --dump 0x2B 0x80 24 --port 1
//
// REQUIRES: Administrator privileges, NVIDIA driver installed
// ==========================================================================

using System.Runtime.InteropServices;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║     NvI2CScanner — NVIDIA GPU I2C Bus Scanner  ║");
Console.WriteLine("║     READ-ONLY: No writes will be performed      ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.WriteLine();

// Parse args
bool fullScan = args.Contains("--full");
bool diagMode = args.Contains("--diag");
bool dumpMode = args.Contains("--dump");
byte dumpAddr = 0, dumpReg = 0;
int dumpLen = 1;
int? dumpPort = null;

if (dumpMode)
{
    int idx = Array.IndexOf(args, "--dump");
    if (idx + 3 >= args.Length)
    {
        Console.WriteLine("Usage: --dump <i2cAddr> <register> <length> [--port N]");
        Console.WriteLine("  Example: --dump 0x2B 0x80 24 --port 1");
        return 1;
    }
    dumpAddr = ParseByte(args[idx + 1]);
    dumpReg = ParseByte(args[idx + 2]);
    dumpLen = int.Parse(args[idx + 3]);

    int portIdx = Array.IndexOf(args, "--port");
    if (portIdx >= 0 && portIdx + 1 < args.Length)
        dumpPort = int.Parse(args[portIdx + 1]);
}

// --- Initialize NvAPI ---
if (!NvApi.Initialize())
{
    Console.WriteLine("[ERROR] Failed to initialize NvAPI. Is NVIDIA driver installed?");
    return 1;
}

Console.WriteLine($"[OK] NvAPI initialized (I2CReadEx available: {NvApi.HasI2CReadEx})");

if (!NvApi.HasI2CReadEx)
{
    Console.WriteLine("[ERROR] NvAPI_I2CReadEx not available in this driver version.");
    return 1;
}

// --- Check admin ---
bool isAdmin = false;
if (OperatingSystem.IsWindows())
{
    var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
    var principal = new System.Security.Principal.WindowsPrincipal(identity);
    isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
}
Console.WriteLine($"[{(isAdmin ? "OK" : "WARN")}] Running as Administrator: {isAdmin}");
if (!isAdmin)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("  *** I2C access typically requires Administrator privileges! ***");
    Console.ResetColor();
}

// --- Enumerate GPUs ---
var gpus = NvApi.EnumerateGPUs();
if (gpus.Count == 0)
{
    Console.WriteLine("[ERROR] No NVIDIA GPUs found.");
    return 1;
}

Console.WriteLine($"[OK] Found {gpus.Count} GPU(s):");
Console.WriteLine();

for (int gi = 0; gi < gpus.Count; gi++)
{
    var gpu = gpus[gi];
    Console.WriteLine($"━━━ GPU {gi}: {gpu.Name} ━━━");
    Console.WriteLine($"    PCI SubSystem ID: 0x{gpu.SubSystemId:X8}  (Vendor: 0x{(gpu.SubSystemId & 0xFFFF):X4})");
    Console.WriteLine($"    Bus ID: {gpu.BusId}");
    Console.WriteLine();

    if (dumpMode)
    {
        if (dumpPort.HasValue)
        {
            DumpRegister(gpu.Handle, (byte)dumpPort.Value, dumpAddr, dumpReg, dumpLen);
        }
        else
        {
            for (byte port = 0; port < 8; port++)
                DumpRegister(gpu.Handle, port, dumpAddr, dumpReg, dumpLen);
        }
    }
    else if (diagMode)
    {
        RunDiagnostic(gpu.Handle);
    }
    else
    {
        ScanAllStrategies(gpu.Handle, fullScan);
    }

    Console.WriteLine();
}

Console.WriteLine("Done. Press any key to exit...");
Console.ReadKey(true);
return 0;

// ==========================================================================
// Diagnostic: try various combinations on a known address to find what works
// ==========================================================================

static void RunDiagnostic(IntPtr gpuHandle)
{
    Console.WriteLine("  ╔═══════════════════════════════════════════╗");
    Console.WriteLine("  ║  DIAGNOSTIC MODE — Testing I2C strategies ║");
    Console.WriteLine("  ╚═══════════════════════════════════════════╝");
    Console.WriteLine();

    // Test address 0x50 (EDID EEPROM, almost always present on GPUs)
    byte[] testAddrs = [0x50, 0x51, 0x2B, 0x48, 0x60, 0x08];

    // Strategy matrix
    var strategies = new (string name, byte isDDC, byte isPortIdSet, byte regAddrSize, byte regAddr, NvApi.NvI2CSpeed speed)[]
    {
        ("Standard (portId=set, regSize=1, 100K)",       0, 1, 1, 0x00, NvApi.NvI2CSpeed.Speed100Khz),
        ("DDC port (isDDC=1, portId=set, 100K)",         1, 1, 1, 0x00, NvApi.NvI2CSpeed.Speed100Khz),
        ("No portId (isPortIdSet=0, 100K)",              0, 0, 1, 0x00, NvApi.NvI2CSpeed.Speed100Khz),
        ("No regAddr (regAddrSize=0, 100K)",             0, 1, 0, 0x00, NvApi.NvI2CSpeed.Speed100Khz),
        ("DDC, no portId (isDDC=1, isPortIdSet=0)",      1, 0, 1, 0x00, NvApi.NvI2CSpeed.Speed100Khz),
        ("Speed default (I2CSpeedKhz=0)",                0, 1, 1, 0x00, NvApi.NvI2CSpeed.Default),
        ("Speed 400K",                                   0, 1, 1, 0x00, NvApi.NvI2CSpeed.Speed400Khz),
        ("Speed 33K",                                    0, 1, 1, 0x00, NvApi.NvI2CSpeed.Speed33Khz),
    };

    // Table header
    Console.Write("  {0,-50}", "Strategy");
    foreach (var addr in testAddrs) Console.Write($" 0x{addr:X2}  ");
    Console.WriteLine();
    Console.Write("  {0,-50}", new string('─', 50));
    foreach (var _ in testAddrs) Console.Write(" ─────");
    Console.WriteLine();

    for (byte port = 0; port < 8; port++)
    {
        bool anyOnPort = false;

        foreach (var strat in strategies)
        {
            var results = new List<(byte addr, NvApi.NvStatus status)>();
            foreach (var addr in testAddrs)
            {
                var s = NvApi.I2CProbe(gpuHandle, port, addr, strat.isDDC, strat.isPortIdSet,
                                        strat.regAddrSize, strat.regAddr, strat.speed);
                results.Add((addr, s));
                if (s == NvApi.NvStatus.OK) anyOnPort = true;
            }

            // Only print if at least one non-PortIdNotFound result, or first strategy per port
            bool interesting = results.Exists(r => r.status != NvApi.NvStatus.PortIdNotFound);
            if (!interesting && strat != strategies[0]) continue;

            if (strat == strategies[0])
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"  ── Port {port} ──");
                Console.ResetColor();
            }

            Console.Write($"  {strat.name,-50}");
            foreach (var (addr, status) in results)
            {
                if (status == NvApi.NvStatus.OK)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"  OK  ");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" {(int)status,4} ");
                    Console.ResetColor();
                }
            }
            Console.WriteLine();
        }

        if (anyOnPort)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  *** Found responding device(s) on Port {port}! ***");
            Console.ResetColor();
        }
    }

    Console.WriteLine();
    Console.WriteLine("  Status codes: 0=OK, -1=Error, -5=InvalidArg, -104=NotSupported, -105=PortIdNotFound");
}

// ==========================================================================
// Scan with multiple strategies
// ==========================================================================

static void ScanAllStrategies(IntPtr gpuHandle, bool fullScan)
{
    byte startAddr = fullScan ? (byte)0x03 : (byte)0x08;
    byte endAddr = 0x77;
    int found = 0;
    var foundDevices = new List<(byte port, byte addr, string strategy)>();

    // Try multiple strategies
    var strategies = new (string name, byte isDDC, byte isPortIdSet, NvApi.NvI2CSpeed speed)[]
    {
        ("std",    0, 1, NvApi.NvI2CSpeed.Speed100Khz),
        ("ddc",    1, 1, NvApi.NvI2CSpeed.Speed100Khz),
        ("noPort", 0, 0, NvApi.NvI2CSpeed.Speed100Khz),
        ("ddc-np", 1, 0, NvApi.NvI2CSpeed.Speed100Khz),
    };

    for (byte port = 0; port < 8; port++)
    {
        foreach (var strat in strategies)
        {
            // Quick probe: test 0x50 first to see if this port+strategy combo works at all
            // (skip if PortIdNotFound)
            var probeStatus = NvApi.I2CProbe(gpuHandle, port, 0x50, strat.isDDC, strat.isPortIdSet,
                                              1, 0x00, strat.speed);
            bool portValid = probeStatus != NvApi.NvStatus.PortIdNotFound &&
                             probeStatus != NvApi.NvStatus.InvalidArgument;

            if (!portValid) continue;

            Console.WriteLine($"  Port {port} [{strat.name}] (probe 0x50 → {probeStatus}):");
            Console.Write("        ");
            for (int c = 0; c < 16; c++) Console.Write($"  {c:X1}");
            Console.WriteLine();

            bool anyHit = false;
            for (byte row = 0; row <= 0x70; row += 0x10)
            {
                Console.Write($"  0x{row:X2}:  ");
                for (byte col = 0; col < 16; col++)
                {
                    byte addr = (byte)(row | col);
                    if (addr < startAddr || addr > endAddr)
                    {
                        Console.Write(" --");
                        continue;
                    }

                    var status = NvApi.I2CProbe(gpuHandle, port, addr, strat.isDDC, strat.isPortIdSet,
                                                 1, 0x00, strat.speed);
                    if (status == NvApi.NvStatus.OK)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write($" {addr:X2}");
                        Console.ResetColor();
                        found++;
                        anyHit = true;
                        foundDevices.Add((port, addr, strat.name));
                    }
                    else
                    {
                        Console.Write(" --");
                    }
                }
                Console.WriteLine();
            }

            if (!anyHit)
                Console.WriteLine("    (no devices found with this strategy)");

            Console.WriteLine();
        }
    }

    Console.WriteLine($"  Total responding addresses: {found}");

    if (foundDevices.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  Detected devices:");
        foreach (var (port, addr, strat) in foundDevices)
        {
            string hint = GuessDevice(addr);
            Console.Write($"    Port {port} [{strat}], 0x{addr:X2}");
            if (hint.Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  ({hint})");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("  Tip: Use --dump <addr> <reg> <len> --port <N> to read registers.");
    }
}

static string GuessDevice(byte addr) => addr switch
{
    >= 0x08 and <= 0x0F => "PMBus / Smart Battery",
    >= 0x20 and <= 0x27 => "GPIO Expander (PCF8574/MCP23008)",
    0x2B => "Possible INA3221 / ASUS Astral 12VHPWR monitor",
    >= 0x2C and <= 0x2F => "INA3221 / current-voltage sensor",
    >= 0x40 and <= 0x4F => "INA219/INA226 power monitor or TMP75 temp sensor",
    >= 0x50 and <= 0x57 => "EEPROM (EDID/config)",
    >= 0x58 and <= 0x5F => "EEPROM / misc",
    >= 0x60 and <= 0x6F => "VRM Controller (IR35217/MP2884/RAA229xxx) - DO NOT WRITE",
    >= 0x70 and <= 0x77 => "I2C Multiplexer (PCA9548)",
    _ => ""
};

// ==========================================================================
// Dump
// ==========================================================================

static void DumpRegister(IntPtr gpuHandle, byte port, byte deviceAddr, byte startReg, int length)
{
    Console.WriteLine($"  [Port {port}] Device 0x{deviceAddr:X2}, Register 0x{startReg:X2}, Length {length}:");

    var status = NvApi.I2CReadBytes(gpuHandle, port, deviceAddr, startReg, length, out byte[]? data);
    if (status == NvApi.NvStatus.OK && data != null)
    {
        PrintHexDump(data, startReg);
        PrintInterpretation(data, startReg);
    }
    else
    {
        // Also try DDC mode
        status = NvApi.I2CReadBytesDDC(gpuHandle, port, deviceAddr, startReg, length, out data);
        if (status == NvApi.NvStatus.OK && data != null)
        {
            Console.WriteLine($"    (succeeded with DDC mode)");
            PrintHexDump(data, startReg);
            PrintInterpretation(data, startReg);
        }
        else
        {
            Console.WriteLine($"    Failed (status: {(int)status} = {status})");

            // Byte-by-byte fallback
            Console.WriteLine($"    Trying byte-by-byte...");
            var bytes = new byte[length];
            var ok = new bool[length];
            bool anyOk = false;
            for (int i = 0; i < length; i++)
            {
                byte reg = (byte)(startReg + i);
                var s = NvApi.I2CReadByte(gpuHandle, port, deviceAddr, reg, out bytes[i]);
                ok[i] = s == NvApi.NvStatus.OK;
                if (ok[i]) anyOk = true;
            }

            if (anyOk)
            {
                PrintHexDump(bytes, startReg, ok);
                PrintInterpretation(bytes, startReg);
            }
            else
            {
                Console.WriteLine($"    Device 0x{deviceAddr:X2} not responding on port {port}.");
            }
        }
    }
    Console.WriteLine();
}

static void PrintHexDump(byte[] data, byte startReg, bool[]? mask = null)
{
    for (int i = 0; i < data.Length; i += 16)
    {
        Console.Write($"    0x{(startReg + i):X2}: ");
        int end = Math.Min(i + 16, data.Length);

        for (int j = i; j < end; j++)
        {
            if (mask != null && !mask[j])
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("?? ");
                Console.ResetColor();
            }
            else
            {
                Console.Write($"{data[j]:X2} ");
            }
        }

        for (int j = end; j < i + 16; j++)
            Console.Write("   ");

        Console.Write(" |");
        for (int j = i; j < end; j++)
        {
            char c = (data[j] >= 0x20 && data[j] < 0x7F) ? (char)data[j] : '.';
            Console.Write(c);
        }
        Console.WriteLine("|");
    }
}

static void PrintInterpretation(byte[] data, byte startReg)
{
    if (data.Length < 2) return;

    Console.WriteLine("    ── u16 big-endian interpretation ──");
    for (int i = 0; i + 1 < data.Length; i += 2)
    {
        ushort val = (ushort)((data[i] << 8) | data[i + 1]);
        float milli = val / 1000f;
        Console.Write($"    [{i / 2,2}] 0x{(startReg + i):X2}: 0x{val:X4} = {val,6}");
        if (val > 0 && val < 65000)
            Console.Write($"  ({milli:F3} if mV/mA)");
        Console.WriteLine();
    }

    if (data.Length >= 24)
    {
        Console.WriteLine("    ── Reversed pin order (Astral 12VHPWR format) ──");
        var reversed = new ushort[data.Length / 2];
        for (int i = 0; i < reversed.Length; i++)
        {
            int ri = reversed.Length - 1 - i;
            reversed[i] = (ushort)((data[ri * 2] << 8) | data[ri * 2 + 1]);
        }
        for (int i = 0; i < reversed.Length; i += 2)
        {
            float current = reversed[i] / 1000f;
            float voltage = reversed[i + 1] / 1000f;
            float power = current * voltage;
            Console.WriteLine($"    Pin {i / 2 + 1}: {current:F3}A x {voltage:F3}V = {power:F3}W");
        }
    }
}

static byte ParseByte(string s)
{
    s = s.Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return byte.Parse(s[2..], System.Globalization.NumberStyles.HexNumber);
    return byte.Parse(s);
}

// ==========================================================================
// NvAPI minimal interop
// ==========================================================================

static class NvApi
{
    public enum NvStatus : int
    {
        OK = 0,
        Error = -1,
        NoImplementation = -3,
        InvalidArgument = -5,
        NvidiaDeviceNotFound = -6,
        EndEnumeration = -7,
        NotSupported = -104,
        PortIdNotFound = -105,
        FunctionNotFound = -1000,
    }

    public enum NvI2CSpeed : uint
    {
        Default = 0,
        Speed3Khz = 1,
        Speed10Khz = 2,
        Speed33Khz = 3,
        Speed100Khz = 4,
        Speed200Khz = 5,
        Speed400Khz = 6,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NvPhysicalGpuHandle { internal IntPtr ptr; }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvI2CInfo
    {
        public uint Version;
        public uint DisplayMask;
        public byte IsDDCPort;
        public byte I2CDevAddress;
        public IntPtr I2CRegAddress;
        public uint RegAddrSize;
        public IntPtr Data;
        public uint Size;
        public uint I2CSpeed;
        public NvI2CSpeed I2CSpeedKhz;
        public byte PortId;
        public uint IsPortIdSet;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_EnumPhysicalGPUsDelegate(
        [Out] NvPhysicalGpuHandle[] handles, out int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_GPU_GetFullNameDelegate(
        NvPhysicalGpuHandle handle, StringBuilder name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_GPU_GetPCIIdentifiersDelegate(
        NvPhysicalGpuHandle handle, out uint deviceId, out uint subSystemId,
        out uint revisionId, out uint extDeviceId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_GPU_GetBusIdDelegate(
        NvPhysicalGpuHandle handle, out uint busId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvStatus NvAPI_I2CReadExDelegate(
        NvPhysicalGpuHandle handle, ref NvI2CInfo i2cInfo, ref uint readData);

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface",
               CallingConvention = CallingConvention.Cdecl, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr NvAPI64_QueryInterface(uint interfaceId);

    private static NvAPI_EnumPhysicalGPUsDelegate? _enumGPUs;
    private static NvAPI_GPU_GetFullNameDelegate? _getFullName;
    private static NvAPI_GPU_GetPCIIdentifiersDelegate? _getPCIIds;
    private static NvAPI_GPU_GetBusIdDelegate? _getBusId;
    private static NvAPI_I2CReadExDelegate? _i2cReadEx;

    public static bool HasI2CReadEx => _i2cReadEx != null;

    private static T? GetDelegate<T>(uint id) where T : class
    {
        IntPtr ptr = NvAPI64_QueryInterface(id);
        return ptr != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T : null;
    }

    public static bool Initialize()
    {
        try
        {
            var init = GetDelegate<NvAPI_InitializeDelegate>(0x0150E828);
            if (init == null || init() != NvStatus.OK)
                return false;

            _enumGPUs = GetDelegate<NvAPI_EnumPhysicalGPUsDelegate>(0xE5AC921F);
            _getFullName = GetDelegate<NvAPI_GPU_GetFullNameDelegate>(0xCEEE8E9F);
            _getPCIIds = GetDelegate<NvAPI_GPU_GetPCIIdentifiersDelegate>(0x2DDFB66E);
            _getBusId = GetDelegate<NvAPI_GPU_GetBusIdDelegate>(0x1BE0B8E5);
            _i2cReadEx = GetDelegate<NvAPI_I2CReadExDelegate>(0x4D7B0709);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public record GpuInfo(IntPtr Handle, string Name, uint SubSystemId, uint BusId);

    public static List<GpuInfo> EnumerateGPUs()
    {
        var result = new List<GpuInfo>();
        if (_enumGPUs == null) return result;

        var handles = new NvPhysicalGpuHandle[64];
        if (_enumGPUs(handles, out int count) != NvStatus.OK) return result;

        for (int i = 0; i < count; i++)
        {
            var h = handles[i];
            var sb = new StringBuilder(64);
            _getFullName?.Invoke(h, sb);

            uint subSysId = 0, busId = 0;
            _getPCIIds?.Invoke(h, out _, out subSysId, out _, out _);
            _getBusId?.Invoke(h, out busId);

            result.Add(new GpuInfo(h.ptr, sb.ToString(), subSysId, busId));
        }

        return result;
    }

    /// <summary>
    /// Probe a device with configurable strategy parameters.
    /// </summary>
    public static NvStatus I2CProbe(IntPtr gpuHandle, byte port, byte deviceAddr,
                                     byte isDDCPort, byte isPortIdSet,
                                     byte regAddrSize, byte regAddr,
                                     NvI2CSpeed speed)
    {
        if (_i2cReadEx == null) return NvStatus.FunctionNotFound;

        unsafe
        {
            byte dataBuf = 0;
            byte regBuf = regAddr;

            var i2c = new NvI2CInfo
            {
                Version = (uint)(Marshal.SizeOf<NvI2CInfo>() | (3 << 16)),
                DisplayMask = 0,
                IsDDCPort = isDDCPort,
                I2CDevAddress = (byte)(deviceAddr << 1),
                I2CRegAddress = (IntPtr)(&regBuf),
                RegAddrSize = regAddrSize,
                Data = (IntPtr)(&dataBuf),
                Size = 1,
                I2CSpeed = 0xFFFF,
                I2CSpeedKhz = speed,
                PortId = port,
                IsPortIdSet = isPortIdSet,
            };

            var handle = new NvPhysicalGpuHandle { ptr = gpuHandle };
            uint readData = 0;
            return _i2cReadEx(handle, ref i2c, ref readData);
        }
    }

    public static NvStatus I2CReadByte(IntPtr gpuHandle, byte port, byte deviceAddr,
                                        byte register, out byte value)
    {
        value = 0;
        if (_i2cReadEx == null) return NvStatus.FunctionNotFound;

        unsafe
        {
            byte dataBuf = 0;
            byte regBuf = register;

            var i2c = new NvI2CInfo
            {
                Version = (uint)(Marshal.SizeOf<NvI2CInfo>() | (3 << 16)),
                DisplayMask = 0,
                IsDDCPort = 0,
                I2CDevAddress = (byte)(deviceAddr << 1),
                I2CRegAddress = (IntPtr)(&regBuf),
                RegAddrSize = 1,
                Data = (IntPtr)(&dataBuf),
                Size = 1,
                I2CSpeed = 0xFFFF,
                I2CSpeedKhz = NvI2CSpeed.Speed100Khz,
                PortId = port,
                IsPortIdSet = 1,
            };

            var handle = new NvPhysicalGpuHandle { ptr = gpuHandle };
            uint readData = 0;
            var status = _i2cReadEx(handle, ref i2c, ref readData);

            if (status == NvStatus.OK)
                value = dataBuf;

            return status;
        }
    }

    public static NvStatus I2CReadBytes(IntPtr gpuHandle, byte port, byte deviceAddr,
                                         byte register, int length, out byte[]? data)
    {
        data = null;
        if (_i2cReadEx == null) return NvStatus.FunctionNotFound;
        if (length <= 0 || length > 256) return NvStatus.InvalidArgument;

        unsafe
        {
            byte[] dataBuf = new byte[length];
            byte regBuf = register;

            fixed (byte* pData = dataBuf)
            {
                var i2c = new NvI2CInfo
                {
                    Version = (uint)(Marshal.SizeOf<NvI2CInfo>() | (3 << 16)),
                    DisplayMask = 0,
                    IsDDCPort = 0,
                    I2CDevAddress = (byte)(deviceAddr << 1),
                    I2CRegAddress = (IntPtr)(&regBuf),
                    RegAddrSize = 1,
                    Data = (IntPtr)pData,
                    Size = (uint)length,
                    I2CSpeed = 0xFFFF,
                    I2CSpeedKhz = NvI2CSpeed.Speed100Khz,
                    PortId = port,
                    IsPortIdSet = 1,
                };

                var handle = new NvPhysicalGpuHandle { ptr = gpuHandle };
                uint readData = 0;
                var status = _i2cReadEx(handle, ref i2c, ref readData);

                if (status == NvStatus.OK)
                    data = dataBuf;

                return status;
            }
        }
    }

    public static NvStatus I2CReadBytesDDC(IntPtr gpuHandle, byte port, byte deviceAddr,
                                             byte register, int length, out byte[]? data)
    {
        data = null;
        if (_i2cReadEx == null) return NvStatus.FunctionNotFound;
        if (length <= 0 || length > 256) return NvStatus.InvalidArgument;

        unsafe
        {
            byte[] dataBuf = new byte[length];
            byte regBuf = register;

            fixed (byte* pData = dataBuf)
            {
                var i2c = new NvI2CInfo
                {
                    Version = (uint)(Marshal.SizeOf<NvI2CInfo>() | (3 << 16)),
                    DisplayMask = 0,
                    IsDDCPort = 1,
                    I2CDevAddress = (byte)(deviceAddr << 1),
                    I2CRegAddress = (IntPtr)(&regBuf),
                    RegAddrSize = 1,
                    Data = (IntPtr)pData,
                    Size = (uint)length,
                    I2CSpeed = 0xFFFF,
                    I2CSpeedKhz = NvI2CSpeed.Speed100Khz,
                    PortId = port,
                    IsPortIdSet = 1,
                };

                var handle = new NvPhysicalGpuHandle { ptr = gpuHandle };
                uint readData = 0;
                var status = _i2cReadEx(handle, ref i2c, ref readData);

                if (status == NvStatus.OK)
                    data = dataBuf;

                return status;
            }
        }
    }
}
