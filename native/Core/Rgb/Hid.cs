using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LedControl.Core.Rgb;

/// <summary>What Windows reports about one HID interface (one top-level collection).</summary>
public sealed record HidInfo(string Path, ushort Vid, ushort Pid, ushort Version, ushort UsagePage, ushort Usage,
                             int InputLength, int OutputLength, int FeatureLength, string Product);

/// <summary>
/// A small HID wrapper over hid.dll and SetupAPI: list devices, open one, and send or read reports.
/// Report buffers always include the report ID in byte 0, as Windows expects.
/// </summary>
public sealed partial class HidDevice : IDisposable
{
    public HidInfo Info { get; }
    readonly SafeFileHandle handle;
    readonly FileStream stream;

    HidDevice(HidInfo info, SafeFileHandle h)
    {
        Info = info;
        handle = h;
        stream = new FileStream(h, FileAccess.ReadWrite, 0, isAsync: true);
    }

    /// <summary>Every HID interface present, read without opening any of them for reading or writing.</summary>
    public static List<HidInfo> List(ushort? vid = null)
    {
        var list = new List<HidInfo>();
        HidD_GetHidGuid(out var guid);
        nint set = SetupDiGetClassDevsW(ref guid, 0, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == -1) return list;
        try
        {
            var ifd = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref ifd); i++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref ifd, 0, 0, out int size, 0);
                if (size <= 0) continue;
                nint buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref ifd, buf, size, out _, 0)) continue;
                    string path = Marshal.PtrToStringUni(buf + 4) ?? "";
                    var info = Query(path);
                    if (info != null && (vid == null || info.Vid == vid)) list.Add(info);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    /// <summary>Reads attributes and report sizes. Opens with no read/write access, so it works even when another app holds the device.</summary>
    static HidInfo? Query(string path)
    {
        using var h = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
        if (h.IsInvalid) return null;
        var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(h, ref attr)) return null;
        ushort page = 0, usage = 0; int inLen = 0, outLen = 0, featLen = 0;
        if (HidD_GetPreparsedData(h, out nint pre))
        {
            if (HidP_GetCaps(pre, out var caps) == HIDP_STATUS_SUCCESS)
            {
                page = caps.UsagePage; usage = caps.Usage;
                inLen = caps.InputReportByteLength; outLen = caps.OutputReportByteLength; featLen = caps.FeatureReportByteLength;
            }
            HidD_FreePreparsedData(pre);
        }
        var name = new byte[256];
        string product = HidD_GetProductString(h, name, name.Length) ? System.Text.Encoding.Unicode.GetString(name).Split('\0')[0] : "";
        return new HidInfo(path, attr.VendorID, attr.ProductID, attr.VersionNumber, page, usage, inLen, outLen, featLen, product);
    }

    public static HidDevice Open(HidInfo info)
    {
        var h = CreateFileW(info.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, 0);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Couldn't open {info.Product} ({info.Vid:X4}:{info.Pid:X4})");
        return new HidDevice(info, h);
    }

    /// <summary>Sends an output report. <paramref name="data"/> starts with the report ID and is zero-padded to the device's report length.</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        var buf = new byte[Math.Max(Info.OutputLength, data.Length)];
        data.CopyTo(buf);
        stream.Write(buf, 0, buf.Length);
        stream.Flush();
    }

    /// <summary>Reads one input report, or null on timeout.</summary>
    public byte[]? Read(int timeoutMs)
    {
        var buf = new byte[Info.InputLength];
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            int n = stream.ReadAsync(buf, 0, buf.Length, cts.Token).GetAwaiter().GetResult();
            return n > 0 ? buf : null;
        }
        catch (OperationCanceledException) { return null; }
    }

    /// <summary>Reads input reports until one matches, or null once <paramref name="totalMs"/> has passed.</summary>
    public byte[]? ReadUntil(Func<byte[], bool> match, int totalMs = 1000)
    {
        var until = Environment.TickCount64 + totalMs;
        while (true)
        {
            long left = until - Environment.TickCount64;
            if (left <= 0) return null;
            var r = Read((int)left);
            if (r != null && match(r)) return r;
        }
    }

    public void FlushInput() => HidD_FlushQueue(handle);

    public void SetFeature(ReadOnlySpan<byte> data)
    {
        var buf = new byte[Math.Max(Info.FeatureLength, data.Length)];
        data.CopyTo(buf);
        if (!HidD_SetFeature(handle, buf, buf.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Sending a feature report failed");
    }

    /// <summary>Reads a feature report. Byte 0 of the result is the report ID.</summary>
    public byte[] GetFeature(byte reportId)
    {
        var buf = new byte[Info.FeatureLength];
        buf[0] = reportId;
        if (!HidD_GetFeature(handle, buf, buf.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Reading a feature report failed");
        return buf;
    }

    public void Dispose()
    {
        stream.Dispose();
        handle.Dispose();
    }

    // ------------------------------------------------------------ Win32

    const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public nint Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out nint data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(nint data, out HIDP_CAPS caps);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] static extern bool HidD_FlushQueue(SafeFileHandle h);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern nint SetupDiGetClassDevsW(ref Guid g, nint e, nint w, uint f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(nint set, nint dev, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref SP_DEVICE_INTERFACE_DATA d, nint detail, int size, out int required, nint dev);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint sec, uint disp, uint flags, nint template);
}
