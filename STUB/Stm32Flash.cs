using System.Text;

namespace STUB;

internal sealed record PcbIdentity(string Number, string Revision, string VersionRaw)
{
    public string Name => Number + "R" + Revision;

    public string VersionText
    {
        get
        {
            var raw = VersionRaw.Trim();
            if (raw.Contains('.'))
            {
                return raw;
            }

            if (raw.Length >= 5 && raw.All(char.IsDigit))
            {
                return $"{raw[0]}.{raw.AsSpan(1, 2)}.{raw.AsSpan(3, 2)}";
            }

            return raw;
        }
    }
}

internal sealed record HexSection(uint Address, byte[] Data);

internal sealed class FlashJob
{
    public required string PortName { get; init; }

    public int BaudRate { get; init; } = 115200;

    public required string HexPath { get; init; }

    public bool Verify { get; init; } = true;

    public bool AutoSync { get; init; } = true;

    public bool EnterIsp { get; init; } = true;
}

internal sealed class BootloaderNackException(string message) : Exception(message);

internal static class IntelHex
{
    public static IReadOnlyList<HexSection> Load(string path)
    {
        var runs = new List<Run>();
        uint upper = 0;
        var lineNo = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] != ':')
            {
                throw new InvalidDataException($"HEX line {lineNo} does not start with ':'.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(line.AsSpan(1));
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException($"HEX line {lineNo} is not hexadecimal.", ex);
            }

            if (bytes.Length < 5 || bytes.Length != bytes[0] + 5)
            {
                throw new InvalidDataException($"HEX line {lineNo} has a bad length.");
            }

            var sum = 0;
            foreach (var value in bytes)
            {
                sum = (sum + value) & 0xFF;
            }

            if (sum != 0)
            {
                throw new InvalidDataException($"HEX line {lineNo} checksum failed.");
            }

            var offset = (uint)((bytes[1] << 8) | bytes[2]);
            switch (bytes[3])
            {
                case 0x00:
                    Append(runs, upper + offset, bytes.AsSpan(4, bytes[0]));
                    break;
                case 0x01:
                    return Finish(runs);
                case 0x02:
                    upper = (uint)(((bytes[4] << 8) | bytes[5]) << 4);
                    break;
                case 0x04:
                    upper = (uint)(((bytes[4] << 8) | bytes[5]) << 16);
                    break;
                case 0x03:
                case 0x05:
                    break;
                default:
                    throw new InvalidDataException($"HEX line {lineNo} has unknown type 0x{bytes[3]:X2}.");
            }
        }

        return Finish(runs);
    }

    private static void Append(List<Run> runs, uint address, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        if (runs.Count > 0 && address == runs[^1].End)
        {
            Add(runs[^1].Data, data);
            return;
        }

        if (runs.Count > 0 && address >= runs[^1].Address && address < runs[^1].End)
        {
            throw new InvalidDataException($"HEX data overlaps at 0x{address:X8}.");
        }

        var run = new Run(address);
        Add(run.Data, data);
        runs.Add(run);
    }

    private static void Add(List<byte> target, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            target.Add(value);
        }
    }

    private static IReadOnlyList<HexSection> Finish(List<Run> runs)
    {
        if (runs.Count == 0)
        {
            throw new InvalidDataException("HEX file has no data records.");
        }

        return runs.Select(run => new HexSection(run.Address, run.Data.ToArray())).ToList();
    }

    private sealed class Run(uint address)
    {
        public uint Address { get; } = address;

        public List<byte> Data { get; } = [];

        public uint End => Address + (uint)Data.Count;
    }
}

internal static class PcbReader
{
    public static PcbIdentity? TryRead(string portName, int baudRate, Action<string> log)
    {
        try
        {
            using var port = WinSerialPort.Open(portName, baudRate, evenParity: false);
            // DTR low resets this board; release it and keep RTS low so the application runs.
            port.SetRts(false);
            port.SetDtr(false);
            Thread.Sleep(80);
            port.SetDtr(true);
            port.SetRts(false);
            Thread.Sleep(800);
            port.DiscardInput();
            port.Write([0xA5, 0x5A]);
            var data = port.ReadAvailable(64, 1200, CancellationToken.None);
            if (data.Length < 10 || !data.Take(10).All(value => value is >= 32 and < 127))
            {
                log(data.Length == 0
                    ? "PCB probe got no data."
                    : "PCB probe: " + Convert.ToHexString(data));
                return null;
            }

            var identity = new PcbIdentity(
                Encoding.ASCII.GetString(data, 0, 4),
                Encoding.ASCII.GetString(data, 4, 1),
                Encoding.ASCII.GetString(data, 5, 5));
            log($"PCB {identity.Name}  version {identity.VersionText} ({identity.VersionRaw.Trim()})");
            return identity;
        }
        catch (Exception ex)
        {
            log("PCB probe failed: " + ex.Message);
            return null;
        }
    }
}

internal static class Flasher
{
    public const uint FlashBase = 0x08000000;

    public static void Probe(FlashJob job, Action<string> log, CancellationToken cancellationToken)
    {
        log($"Open {job.PortName} @ {job.BaudRate}, 8E1");
        using var port = WinSerialPort.Open(job.PortName, job.BaudRate, evenParity: true);
        Thread.Sleep(100);
        var loader = new Stm32Bootloader(port);
        loader.EnterIsp(job.AutoSync, log, cancellationToken);
        log($"Chip ID 0x{loader.GetProductId(cancellationToken):X4}");
        log("Vector " + Convert.ToHexString(loader.ReadMemory(Flasher.FlashBase, 16, cancellationToken)));
        log("OPTR " + Convert.ToHexString(loader.ReadMemory(0x1FFF7800, 8, cancellationToken)));
        RunApplication(port, loader, log, cancellationToken);
    }

    private static byte[] Ask(WinSerialPort port, int baudRate, bool evenParity, CancellationToken cancellationToken)
    {
        port.SetFormat(baudRate, evenParity);
        Thread.Sleep(200);
        port.DiscardInput();
        port.Write([0xA5, 0x5A]);
        return port.ReadAvailable(64, 800, cancellationToken);
    }

    public static bool StartApplication(FlashJob job, Action<string> log, CancellationToken cancellationToken)
    {
        log($"Open {job.PortName} @ {job.BaudRate}, 8E1");
        using var port = WinSerialPort.Open(job.PortName, job.BaudRate, evenParity: true);
        var loader = new Stm32Bootloader(port);
        loader.EnterIsp(job.AutoSync, log, cancellationToken);
        if (!RunApplication(port, loader, log, cancellationToken))
        {
            return false;
        }

        Thread.Sleep(400);
        try
        {
            var data = Ask(port, job.BaudRate, evenParity: false, cancellationToken);
            if (data.Length == 0)
            {
                data = Ask(port, job.BaudRate, evenParity: true, cancellationToken);
            }

            if (data.Length >= 10 && data.Take(10).All(value => value is >= 32 and < 127))
            {
                var identity = new PcbIdentity(
                    Encoding.ASCII.GetString(data, 0, 4),
                    Encoding.ASCII.GetString(data, 4, 1),
                    Encoding.ASCII.GetString(data, 5, 5));
                log($"PCB {identity.Name}  version {identity.VersionText} ({identity.VersionRaw.Trim()})");
            }
            else
            {
                log(data.Length == 0 ? "Application did not answer A5 5A." : "Application: " + Convert.ToHexString(data));
            }
        }
        catch (IOException ex)
        {
            log("UART dropped after start: " + ex.Message);
        }

        return true;
    }

    public static void Program(FlashJob job, Action<string> log, Action<int> progress, CancellationToken cancellationToken)
    {
        var image = IntelHex.Load(job.HexPath)
            .Where(section => section.Address is >= FlashBase and <= 0x08FFFFFF)
            .ToList();
        if (image.Count == 0)
        {
            throw new InvalidDataException("HEX file has no STM32 flash image.");
        }

        var bytes = image.Sum(section => section.Data.Length);
        log($"{Path.GetFileName(job.HexPath)}  {image.Count} section(s), {bytes} bytes");
        foreach (var section in image)
        {
            log($"  0x{section.Address:X8}  {section.Data.Length} bytes");
        }

        Exception? last = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WinSerialPort? port = null;
            try
            {
                log($"Open {job.PortName} @ {job.BaudRate}, 8E1");
                port = WinSerialPort.Open(job.PortName, job.BaudRate, evenParity: true);
                Thread.Sleep(100);
                var loader = new Stm32Bootloader(port);
                if (job.EnterIsp)
                {
                    loader.EnterIsp(job.AutoSync, log, cancellationToken);
                }
                else
                {
                    loader.Init(cancellationToken);
                    log("Bootloader ACK.");
                }

                try
                {
                    log($"Chip ID 0x{loader.GetProductId(cancellationToken):X4}");
                }
                catch (Exception ex) when (ex is TimeoutException or BootloaderNackException or InvalidOperationException)
                {
                    log("GetID skipped: " + ex.Message);
                }

                log($"Erase ({attempt}/5)...");
                progress(0);
                loader.MassErase(cancellationToken);
                var blank = loader.ReadMemory(FlashBase, 256, cancellationToken);
                if (blank.Any(value => value != 0xFF))
                {
                    throw new InvalidOperationException("Flash is not blank after erase.");
                }

                log("Erase OK.");
                log(job.Verify ? "Write and verify..." : "Write...");
                loader.WriteImage(image, job.Verify, progress, cancellationToken);
                log("Image written.");
                progress(100);
                RunApplication(port, loader, log, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 5)
            {
                last = ex;
                log("Retry: " + ex.Message);
                Thread.Sleep(800);
            }
            finally
            {
                if (port is not null)
                {
                    Release(port);
                }
            }
        }

        throw last ?? new InvalidOperationException("Flash failed.");
    }

    private static bool RunApplication(WinSerialPort port, Stm32Bootloader loader, Action<string> log, CancellationToken cancellationToken)
    {
        try
        {
            var vector = loader.ReadMemory(FlashBase, 8, cancellationToken);
            var reset = BitConverter.ToUInt32(vector, 4);
            if ((reset & 1) == 0)
            {
                reset |= 1;
            }

            // This image's reset handler loads MSP. NRST is left released so the jump is not undone.
            port.SetRts(false);
            port.SetDtr(true);
            log($"Go 0x{reset:X8}");
            loader.Go(reset, cancellationToken);
            log("Application started.");
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or BootloaderNackException or InvalidOperationException or IOException)
        {
            log("Go failed: " + ex.Message);
            return false;
        }
    }

    private static void Release(WinSerialPort port) => port.Dispose();
}

internal sealed class Stm32Bootloader(WinSerialPort port)
{
    private const byte Ack = 0x79;
    private bool _ready;

    public void EnterIsp(bool autoSync, Action<string> log, CancellationToken cancellationToken)
    {
        port.DiscardInput();
        port.SetDtr(false);
        port.SetRts(true);
        log("DTR low (reset), RTS high (ISP).");
        Thread.Sleep(100);
        port.SetDtr(true);
        port.SetRts(true);
        log("DTR high (reset released), RTS stays high.");
        Thread.Sleep(30);

        if (!autoSync)
        {
            Init(cancellationToken);
            return;
        }

        var deadline = Environment.TickCount64 + 4000;
        var noise = 0;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port.BytesToRead == 0)
            {
                port.Write([0x7F]);
            }

            if (port.BytesToRead > 0)
            {
                var value = port.ReadByte(50, cancellationToken);
                if (value == Ack)
                {
                    _ready = true;
                    log("Bootloader ACK.");
                    return;
                }

                if (value >= 0 && noise < 6)
                {
                    noise++;
                    log($"Sync byte 0x{value:X2}.");
                }
            }
            else
            {
                Thread.Sleep(10);
            }
        }

        throw new TimeoutException("Bootloader did not ACK 0x7F.");
    }

    public void Init(CancellationToken cancellationToken)
    {
        port.DiscardInput();
        port.Write([0x7F]);
        WaitAck(1000, cancellationToken);
        _ready = true;
    }

    public int GetProductId(CancellationToken cancellationToken)
    {
        Command(0x02, cancellationToken);
        var count = port.ReadByte(1000, cancellationToken);
        if (count < 1)
        {
            throw new TimeoutException("GetID length timed out.");
        }

        var data = port.ReadExact(count + 1, 1000, cancellationToken);
        WaitAck(1000, cancellationToken);
        return (data[0] << 8) | data[1];
    }

    public void Go(uint address, CancellationToken cancellationToken)
    {
        if ((address & 1) == 0)
        {
            address |= 1;
        }

        Command(0x21, cancellationToken);
        SendAddress(address, cancellationToken);
    }

    public void MassErase(CancellationToken cancellationToken)
    {
        EnsureReady();
        port.DiscardInput();
        port.Write([0x44, 0xBB]);
        WaitAck(2000, cancellationToken);
        port.Write([0xFF, 0xFF, 0x00]);
        WaitAck(30000, cancellationToken);
        Thread.Sleep(200);
    }

    public byte[] ReadMemory(uint address, int size, CancellationToken cancellationToken)
    {
        if (size is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        Command(0x11, cancellationToken);
        SendAddress(address, cancellationToken);
        var length = (byte)(size - 1);
        port.Write([length, (byte)~length]);
        WaitAck(2000, cancellationToken);
        return port.ReadExact(size, 2000, cancellationToken);
    }

    public void WriteImage(IReadOnlyList<HexSection> sections, bool verify, Action<int> progress, CancellationToken cancellationToken)
    {
        sections = Align8(sections);
        var total = sections.Sum(section => section.Data.Length);
        var done = 0;
        foreach (var section in sections)
        {
            var offset = 0;
            while (offset < section.Data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(256, section.Data.Length - offset);
                if (count < section.Data.Length - offset)
                {
                    count &= ~7;
                }

                WriteMemory((uint)(section.Address + offset), Pad(section.Data.AsSpan(offset, count)), cancellationToken);
                offset += count;
                done += count;
                progress(total == 0 ? 100 : done * 100 / total);
            }
        }

        if (!verify)
        {
            return;
        }

        done = 0;
        foreach (var section in sections)
        {
            var offset = 0;
            while (offset < section.Data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(256, section.Data.Length - offset);
                if (count < section.Data.Length - offset)
                {
                    count &= ~7;
                }

                var readSize = (count + 7) & ~7;
                var matched = false;
                for (var tryNo = 0; tryNo < 5 && !matched; tryNo++)
                {
                    var actual = ReadMemory((uint)(section.Address + offset), readSize, cancellationToken);
                    matched = actual.AsSpan(0, count).SequenceEqual(section.Data.AsSpan(offset, count));
                }

                if (!matched)
                {
                    throw new InvalidOperationException($"Verify failed at 0x{section.Address + offset:X8}.");
                }

                offset += count;
                done += count;
                progress(total == 0 ? 100 : done * 100 / total);
            }
        }
    }

    private static IReadOnlyList<HexSection> Align8(IReadOnlyList<HexSection> source)
    {
        var list = source.OrderBy(section => section.Address).ToList();
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < list.Count; i++)
            {
                var aligned = list[i].Address & ~7u;
                if (aligned != list[i].Address)
                {
                    if (i > 0 && list[i - 1].Address + (uint)list[i - 1].Data.Length > aligned)
                    {
                        list[i - 1] = Merge(list[i - 1], list[i]);
                        list.RemoveAt(i);
                    }
                    else
                    {
                        var pad = (int)(list[i].Address - aligned);
                        var data = new byte[pad + list[i].Data.Length];
                        Array.Fill(data, (byte)0xFF, 0, pad);
                        Buffer.BlockCopy(list[i].Data, 0, data, pad, list[i].Data.Length);
                        list[i] = new HexSection(aligned, data);
                    }

                    changed = true;
                    break;
                }

                if ((list[i].Data.Length & 7) != 0)
                {
                    var pad = 8 - (list[i].Data.Length & 7);
                    var end = list[i].Address + (uint)list[i].Data.Length;
                    if (i + 1 < list.Count && list[i + 1].Address < end + (uint)pad)
                    {
                        list[i] = Merge(list[i], list[i + 1]);
                        list.RemoveAt(i + 1);
                    }
                    else
                    {
                        var data = new byte[list[i].Data.Length + pad];
                        Buffer.BlockCopy(list[i].Data, 0, data, 0, list[i].Data.Length);
                        Array.Fill(data, (byte)0xFF, list[i].Data.Length, pad);
                        list[i] = new HexSection(list[i].Address, data);
                    }

                    changed = true;
                    break;
                }
            }
        }

        return list;
    }

    private static HexSection Merge(HexSection left, HexSection right)
    {
        var start = left.Address;
        var end = Math.Max(left.Address + (uint)left.Data.Length, right.Address + (uint)right.Data.Length);
        var data = new byte[end - start];
        Array.Fill(data, (byte)0xFF);
        Buffer.BlockCopy(left.Data, 0, data, 0, left.Data.Length);
        Buffer.BlockCopy(right.Data, 0, data, (int)(right.Address - start), right.Data.Length);
        return new HexSection(start, data);
    }

    private void WriteMemory(uint address, byte[] data, CancellationToken cancellationToken)
    {
        Command(0x31, cancellationToken);
        SendAddress(address, cancellationToken);
        var payload = new byte[data.Length + 2];
        payload[0] = (byte)(data.Length - 1);
        data.CopyTo(payload.AsSpan(1));
        byte xor = 0;
        for (var i = 0; i < payload.Length - 1; i++)
        {
            xor ^= payload[i];
        }

        payload[^1] = xor;
        port.Write(payload);
        WaitAck(5000, cancellationToken);
    }

    private static byte[] Pad(ReadOnlySpan<byte> data)
    {
        var size = (data.Length + 7) & ~7;
        if (size == data.Length)
        {
            return data.ToArray();
        }

        var padded = new byte[size];
        padded.AsSpan().Fill(0xFF);
        data.CopyTo(padded);
        return padded;
    }

    private void Command(byte command, CancellationToken cancellationToken)
    {
        EnsureReady();
        port.DiscardInput();
        port.Write([command, (byte)~command]);
        WaitAck(2000, cancellationToken);
    }

    private void SendAddress(uint address, CancellationToken cancellationToken)
    {
        var b0 = (byte)(address >> 24);
        var b1 = (byte)(address >> 16);
        var b2 = (byte)(address >> 8);
        var b3 = (byte)address;
        port.Write([b0, b1, b2, b3, (byte)(b0 ^ b1 ^ b2 ^ b3)]);
        WaitAck(2000, cancellationToken);
    }

    private void WaitAck(int timeoutMs, CancellationToken cancellationToken)
    {
        var value = port.ReadByte(timeoutMs, cancellationToken);
        if (value == Ack)
        {
            return;
        }

        if (value == 0x1F)
        {
            throw new BootloaderNackException("Bootloader returned NACK.");
        }

        if (value < 0)
        {
            throw new TimeoutException("Bootloader ACK timed out.");
        }

        throw new InvalidOperationException($"Bootloader returned 0x{value:X2}.");
    }

    private void EnsureReady()
    {
        if (!_ready)
        {
            throw new InvalidOperationException("Bootloader is not initialized.");
        }
    }
}
