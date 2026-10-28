using System.Runtime.InteropServices;
using MiniWinForm;

namespace STUB;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            RunWindow();
            return 0;
        }

        AttachConsole();
        try
        {
            return RunCommand(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void RunWindow()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.Run(new MainForm());
    }

    private static int RunCommand(string[] args)
    {
        string? port = null;
        string? hex = null;
        var baud = 115200;
        var verify = true;
        var sync = true;
        var enter = true;
        var force = false;
        var readOnly = false;
        var probe = false;
        var start = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--flash":
                    break;
                case "--read":
                    readOnly = true;
                    break;
                case "--probe":
                    probe = true;
                    break;
                case "--start":
                    start = true;
                    break;
                case "--port":
                    port = Next(args, ref i);
                    break;
                case "--hex":
                    hex = Next(args, ref i);
                    break;
                case "--baud":
                    baud = int.Parse(Next(args, ref i));
                    break;
                case "--no-verify":
                    verify = false;
                    break;
                case "--no-sync":
                    sync = false;
                    break;
                case "--keep-lines":
                    enter = false;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("STUB [--read | --probe | --start | --flash] [--port COM8] [--baud 115200] [--hex file.hex] [--no-verify] [--no-sync] [--keep-lines] [--force]");
                    return 0;
                default:
                    throw new ArgumentException("Unknown argument: " + args[i]);
            }
        }

        port ??= "COM8";
        void Log(string message)
        {
            Console.WriteLine(message);
            Console.Out.Flush();
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "serial-flash.log"), message + Environment.NewLine);
        }

        if (probe || start)
        {
            var probeJob = new FlashJob { PortName = port, BaudRate = baud, HexPath = "probe", EnterIsp = enter, AutoSync = sync };
            if (start)
            {
                return Flasher.StartApplication(probeJob, Log, CancellationToken.None) ? 0 : 1;
            }

            Flasher.Probe(probeJob, Log, CancellationToken.None);
            return 0;
        }

        var pcb = PcbReader.TryRead(port, baud, Log);
        if (readOnly)
        {
            return pcb is null ? 1 : 0;
        }

        hex ??= ComPorts.FindDefaultHex() ?? throw new FileNotFoundException("No HEX file found.");
        if (!File.Exists(hex))
        {
            throw new FileNotFoundException("HEX file was not found.", hex);
        }

        var name = Path.GetFileNameWithoutExtension(hex);
        if (pcb is null && !force)
        {
            throw new InvalidOperationException("PCB did not respond. Pass --force to program anyway.");
        }

        if (pcb is not null && !name.StartsWith(pcb.Number, StringComparison.OrdinalIgnoreCase) && !force)
        {
            throw new InvalidOperationException($"HEX {name} does not match PCB {pcb.Name}.");
        }

        if (pcb is not null && !name.StartsWith(pcb.Name, StringComparison.OrdinalIgnoreCase))
        {
            Log($"Revision differs: PCB {pcb.Name}, file {name}.");
        }

        var job = new FlashJob
        {
            PortName = port,
            BaudRate = baud,
            HexPath = hex,
            Verify = verify,
            AutoSync = sync,
            EnterIsp = enter,
        };
        var lastStep = -1;
        Flasher.Program(job, Log, percent =>
        {
            var step = percent / 5;
            if (step == lastStep)
            {
                return;
            }

            lastStep = step;
            Log($"{percent}%");
        }, CancellationToken.None);
        Log("Complete.");
        return 0;
    }

    private static string Next(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException("Missing value after " + args[index]);
        }

        index++;
        return args[index];
    }

    private static void AttachConsole()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        if (!Native.AttachConsole(-1))
        {
            Native.AllocConsole();
        }

        var stream = new FileStream("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        var writer = new StreamWriter(stream) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    private static class Native
    {
        [DllImport("kernel32.dll")]
        internal static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        internal static extern bool AllocConsole();
    }
}
