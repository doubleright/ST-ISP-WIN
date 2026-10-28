using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace STUB;

internal sealed class ComPort
{
    public required string Name { get; init; }

    public required string Caption { get; init; }

    public override string ToString() => Caption;
}

internal static class ComPorts
{
    public static List<ComPort> List()
    {
        var captions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            ReadSetupApi(captions);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or EntryPointNotFoundException)
        {
        }

        try
        {
            ReadRegistry(captions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return captions
            .OrderBy(pair => ComNumber(pair.Key))
            .Select(pair => new ComPort { Name = pair.Key, Caption = pair.Value })
            .ToList();
    }

    public static string? FindDefaultHex()
    {
        var found = new List<string>();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            AddHex(found, dir.FullName);
        }

        AddHex(found, Environment.CurrentDirectory);
        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => Path.GetFileName(path).Contains("_V", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void AddHex(List<string> found, string directory)
    {
        if (Directory.Exists(directory))
        {
            found.AddRange(Directory.GetFiles(directory, "*.hex"));
        }
    }

    private static int ComNumber(string name)
    {
        return name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(3), out var number)
            ? number
            : int.MaxValue;
    }

    private static void ReadRegistry(Dictionary<string, string> captions)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            if (key.GetValue(valueName) is string port)
            {
                Remember(captions, port, port);
            }
        }
    }

    private static void ReadSetupApi(Dictionary<string, string> captions)
    {
        var guid = new Guid("4D36E978-E325-11CE-BFC1-08002BE10318");
        var info = Native.SetupDiGetClassDevsW(guid, null, 0, 2);
        if (info == new IntPtr(-1))
        {
            return;
        }

        try
        {
            var data = new Native.DeviceInfoData { Size = Marshal.SizeOf<Native.DeviceInfoData>() };
            for (uint index = 0; Native.SetupDiEnumDeviceInfo(info, index, ref data); index++)
            {
                var text = ReadProperty(info, ref data, 12) ?? ReadProperty(info, ref data, 0);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var port = ExtractPort(text);
                if (port is not null)
                {
                    Remember(captions, port, text.Trim());
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(info);
        }
    }

    private static string? ReadProperty(IntPtr info, ref Native.DeviceInfoData data, uint property)
    {
        var buffer = new byte[512];
        if (!Native.SetupDiGetDeviceRegistryPropertyW(info, ref data, property, out _, buffer, (uint)buffer.Length, out _))
        {
            return null;
        }

        var text = Encoding.Unicode.GetString(buffer);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    private static string? ExtractPort(string text)
    {
        var start = text.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var end = text.IndexOf(')', start);
        if (end < 0)
        {
            return null;
        }

        return text[(start + 1)..end].ToUpperInvariant();
    }

    private static void Remember(Dictionary<string, string> captions, string port, string caption)
    {
        port = port.ToUpperInvariant();
        if (!captions.TryGetValue(port, out var current) || current.Equals(port, StringComparison.OrdinalIgnoreCase))
        {
            captions[port] = caption.Contains(port, StringComparison.OrdinalIgnoreCase) ? caption : $"{caption} ({port})";
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceInfoData
        {
            public int Size;
            public Guid ClassGuid;
            public uint DevInst;
            public nuint Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevsW(in Guid classGuid, string? enumerator, nint hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref DeviceInfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceRegistryPropertyW(
            IntPtr deviceInfoSet,
            ref DeviceInfoData deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}

internal sealed class WinSerialPort : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;
    private bool _dtr;
    private bool _rts;

    private WinSerialPort(IntPtr handle) => _handle = handle;

    public static WinSerialPort Open(string portName, int baudRate, bool evenParity)
    {
        var handle = Native.CreateFileW(@"\\.\" + portName, 0xC0000000, 0, 0, 3, 0, 0);
        if (handle == new IntPtr(-1))
        {
            throw new IOException($"Cannot open {portName} ({Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            var port = new WinSerialPort(handle);
            port.Configure(baudRate, evenParity);
            port.SetRts(false);
            port.SetDtr(false);
            return port;
        }
        catch
        {
            Native.CloseHandle(handle);
            throw;
        }
    }

    public int BytesToRead
    {
        get
        {
            if (!Native.ClearCommError(_handle, out _, out var status))
            {
                throw new IOException($"ClearCommError failed ({Marshal.GetLastPInvokeError()}).");
            }

            return (int)status.BytesInQueue;
        }
    }

    public void SetFormat(int baudRate, bool evenParity) => Configure(baudRate, evenParity);

    public void SetDtr(bool asserted)
    {
        _dtr = asserted;
        ApplyLines();
    }

    public void SetRts(bool asserted)
    {
        _rts = asserted;
        ApplyLines();
    }

    public void DiscardInput()
    {
        if (!Native.PurgeComm(_handle, 0x0008))
        {
            throw new IOException($"PurgeComm failed ({Marshal.GetLastPInvokeError()}).");
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        var buffer = data.ToArray();
        if (!Native.WriteFile(_handle, buffer, buffer.Length, out var written, 0) || written != buffer.Length)
        {
            throw new IOException($"WriteFile failed ({Marshal.GetLastPInvokeError()}).");
        }
    }

    public byte[] ReadAvailable(int max, int timeoutMs, CancellationToken cancellationToken)
    {
        var buffer = new byte[max];
        var got = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (got < max && Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slice = (int)Math.Min(got == 0 ? 200 : 80, deadline - Environment.TickCount64);
            var n = Read(buffer, got, max - got, Math.Max(slice, 1), cancellationToken);
            if (n == 0)
            {
                if (got > 0)
                {
                    break;
                }

                continue;
            }

            got += n;
        }

        return buffer[..got];
    }

    public byte[] ReadExact(int count, int timeoutMs, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var got = Read(buffer, 0, count, timeoutMs, cancellationToken);
        if (got != count)
        {
            throw new TimeoutException($"Timed out waiting for {count} bytes (got {got}).");
        }

        return buffer;
    }

    public int ReadByte(int timeoutMs, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        return Read(buffer, 0, 1, timeoutMs, cancellationToken) == 1 ? buffer[0] : -1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Native.CloseHandle(_handle);
    }

    private int Read(byte[] buffer, int offset, int count, int timeoutMs, CancellationToken cancellationToken)
    {
        var got = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (got < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remain = (int)(deadline - Environment.TickCount64);
            if (remain <= 0)
            {
                break;
            }

            SetTimeouts(Math.Min(remain, 250));
            var chunk = new byte[count - got];
            if (!Native.ReadFile(_handle, chunk, chunk.Length, out var n, 0))
            {
                throw new IOException($"ReadFile failed ({Marshal.GetLastPInvokeError()}).");
            }

            if (n > 0)
            {
                Buffer.BlockCopy(chunk, 0, buffer, offset + got, n);
                got += n;
            }
        }

        return got;
    }

    private void Configure(int baudRate, bool evenParity)
    {
        var dcb = new Native.Dcb { Length = 28 };
        if (!Native.GetCommState(_handle, ref dcb))
        {
            throw new IOException($"GetCommState failed ({Marshal.GetLastPInvokeError()}).");
        }

        dcb.BaudRate = (uint)baudRate;
        dcb.ByteSize = 8;
        dcb.Parity = evenParity ? (byte)2 : (byte)0;
        dcb.StopBits = 0;
        var flags = dcb.Flags | 1u;
        flags = evenParity ? flags | 2u : flags & ~2u;
        flags &= ~(1u << 14);
        dcb.Flags = WithLineControl(flags);
        if (!Native.SetCommState(_handle, ref dcb))
        {
            throw new IOException($"SetCommState failed ({Marshal.GetLastPInvokeError()}).");
        }

        SetTimeouts(200);
    }

    private void SetTimeouts(int readConstantMs)
    {
        var timeouts = new Native.CommTimeouts
        {
            ReadIntervalTimeout = 20,
            ReadTotalTimeoutConstant = (uint)readConstantMs,
            WriteTotalTimeoutConstant = 2000,
        };
        if (!Native.SetCommTimeouts(_handle, ref timeouts))
        {
            throw new IOException($"SetCommTimeouts failed ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private void ApplyLines()
    {
        var dcb = new Native.Dcb { Length = 28 };
        if (!Native.GetCommState(_handle, ref dcb))
        {
            throw new IOException($"GetCommState failed ({Marshal.GetLastPInvokeError()}).");
        }

        dcb.Flags = WithLineControl(dcb.Flags);
        if (!Native.SetCommState(_handle, ref dcb))
        {
            throw new IOException($"SetCommState failed ({Marshal.GetLastPInvokeError()}).");
        }

        Escape(_dtr ? 5u : 6u);
        Escape(_rts ? 3u : 4u);
    }

    private uint WithLineControl(uint flags)
    {
        flags &= ~(3u << 4);
        flags &= ~(3u << 12);
        if (_dtr)
        {
            flags |= 1u << 4;
        }

        if (_rts)
        {
            flags |= 1u << 12;
        }

        return flags;
    }

    private void Escape(uint function)
    {
        if (!Native.EscapeCommFunction(_handle, function))
        {
            throw new IOException($"EscapeCommFunction failed ({Marshal.GetLastPInvokeError()}).");
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Dcb
        {
            public uint Length;
            public uint BaudRate;
            public uint Flags;
            public ushort Reserved;
            public ushort XonLim;
            public ushort XoffLim;
            public byte ByteSize;
            public byte Parity;
            public byte StopBits;
            public byte XonChar;
            public byte XoffChar;
            public byte ErrorChar;
            public byte EofChar;
            public byte EvtChar;
            public ushort Reserved1;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CommTimeouts
        {
            public uint ReadIntervalTimeout;
            public uint ReadTotalTimeoutMultiplier;
            public uint ReadTotalTimeoutConstant;
            public uint WriteTotalTimeoutMultiplier;
            public uint WriteTotalTimeoutConstant;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ComStat
        {
            public uint Flags;
            public uint BytesInQueue;
            public uint BytesOutQueue;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetCommState(IntPtr handle, ref Dcb dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetCommState(IntPtr handle, ref Dcb dcb);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool SetCommTimeouts(IntPtr handle, ref CommTimeouts timeouts);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool EscapeCommFunction(IntPtr handle, uint function);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool PurgeComm(IntPtr handle, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ClearCommError(IntPtr handle, out uint errors, out ComStat status);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteFile(IntPtr handle, byte[] buffer, int count, out int written, nint overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadFile(IntPtr handle, byte[] buffer, int count, out int read, nint overlapped);
    }
}
