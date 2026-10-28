using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using MiniWinForm;

namespace STUB;

internal sealed class MainForm : Form
{
    private static readonly Color Card = Color.White;
    private static readonly Color Ink = Color.FromArgb(32, 41, 54);
    private static readonly Color Accent = Color.FromArgb(15, 98, 168);
    private static readonly Color Good = Color.FromArgb(22, 120, 68);
    private static readonly Color Bad = Color.FromArgb(176, 42, 42);
    private static readonly Color Muted = Color.FromArgb(90, 99, 112);

    private readonly int _scale;
    private readonly StringBuilder _logText = new();
    private readonly ListBox _ports = new();
    private readonly RadioButton _baud115200 = new() { Text = "115200（常用）", Checked = true };
    private readonly RadioButton _baud9600 = new() { Text = "9600" };
    private readonly RadioButton _isp = new() { Text = "自动复位进入下载", Checked = true };
    private readonly RadioButton _keep = new() { Text = "已经在下载模式" };
    private readonly CheckBox _verify = new() { Text = "烧录后读回校验", Checked = true };
    private readonly CheckBox _sync = new() { Text = "先发同步字节 0x7F", Checked = true };
    private readonly TextBox _hex = new();
    private readonly TextBox _info = new() { Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, TabStop = false };
    private readonly TextBox _log = new() { Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, TabStop = false };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly Button _refresh = new() { Text = "刷新串口" };
    private readonly Button _browse = new() { Text = "浏览…" };
    private readonly Button _read = new() { Text = "读取板卡" };
    private readonly Button _start = new() { Text = "开始烧录", ForeColor = Accent };
    private readonly Button _cancel = new() { Text = "取消", Enabled = false };
    private readonly Label _portHint = new() { ForeColor = Muted };
    private readonly Label _fileHint = new() { ForeColor = Muted };
    private readonly Label _status = new();
    private readonly Label _percent = new() { Text = "0%", ForeColor = Muted };
    private readonly MiniWinForm.Timer _timer = new() { Interval = 100 };
    private readonly Queue<Action> _posts = new();
    private CancellationTokenSource? _cancelSource;
    private string _phase = "就绪";
    private Color _phaseColor = Muted;
    private string _hintPath = "\0";
    private bool _busy;

    public MainForm()
    {
        _scale = Math.Max(96, (int)GetDpiForSystem());
        Text = "STM32 串口下载";
        StartPosition = FormStartPosition.CenterScreen;
        Width = S(1000);
        Height = S(740);

        var link = Section("1  连接", 16, 12, 968, 188);
        Place(link, _ports, 12, 24, 800, 72);
        Place(link, _refresh, 824, 24, 132, 32);
        Place(link, _portHint, 12, 100, 944, 22);
        Place(link, new Label { Text = "波特率" }, 12, 128, 56, 22);
        Place(link, _baud115200, 72, 126, 168, 24);
        Place(link, _baud9600, 248, 126, 88, 24);
        Place(link, new Label { Text = "进入" }, 360, 128, 44, 22);
        Place(link, _isp, 408, 126, 200, 24);
        Place(link, _keep, 620, 126, 210, 24);
        Stretch(link);
        Stretch(_ports);
        _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Stretch(_portHint);

        var file = Section("2  固件", 16, 208, 968, 112);
        Place(file, _hex, 12, 26, 808, 26);
        Place(file, _browse, 832, 24, 124, 30);
        Place(file, _fileHint, 12, 56, 944, 20);
        Place(file, _verify, 12, 80, 170, 24);
        Place(file, _sync, 196, 80, 220, 24);
        Stretch(file);
        Stretch(_hex);
        _browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Stretch(_fileHint);

        var board = Section("3  板卡", 16, 328, 968, 88);
        _info.Text = "尚未读取。\r\n接好串口后，可以先点「读取板卡」确认型号，再烧录。";
        Place(board, _info, 12, 24, 944, 52);
        Stretch(board);
        Stretch(_info);

        Place(_read, 16, 428, 128, 34);
        Place(_start, 156, 428, 148, 34);
        Place(_cancel, 316, 428, 108, 34);
        Place(_status, 440, 434, 430, 24);
        Place(_percent, 888, 434, 80, 24);
        Place(_progress, 16, 472, 968, 18);
        Stretch(_status);
        _percent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Stretch(_progress);

        var log = Section("4  日志", 16, 500, 968, 200);
        Place(log, _log, 12, 22, 944, 166);
        log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        SetPhase("就绪", Ink);
        _refresh.Click += (_, _) => LoadPorts();
        _browse.Click += (_, _) => BrowseHex();
        _read.Click += (_, _) => ReadPcb();
        _start.Click += (_, _) => StartFlash();
        _cancel.Click += (_, _) => _cancelSource?.Cancel();
        _ports.SelectedIndexChanged += (_, _) => UpdatePortHint();
        _baud115200.CheckedChanged += (_, _) => UpdatePortHint();
        _baud9600.CheckedChanged += (_, _) => UpdatePortHint();
        _isp.CheckedChanged += (_, _) => UpdatePortHint();
        _keep.CheckedChanged += (_, _) => UpdatePortHint();
        _timer.Tick += (_, _) =>
        {
            DrainPosts();
            UpdateFileHint();
        };
        Shown += (_, _) =>
        {
            _timer.Enabled = true;
            SetReadOnly(_info);
            SetReadOnly(_log);
            LoadPorts();
            _hex.Text = ComPorts.FindDefaultHex() ?? string.Empty;
            UpdateFileHint();
            AppendLog(_hex.Text.Length == 0 ? "选择 HEX 文件后即可烧录。" : "已选用 " + Path.GetFileName(_hex.Text));
        };
    }

    private GroupBox Section(string title, int x, int y, int width, int height)
    {
        var box = new GroupBox { Text = title, BackColor = Card };
        Place(box, x, y, width, height);
        return box;
    }

    private int S(int px) => px * _scale / 96;

    private void Place(Control control, int x, int y, int width, int height)
    {
        control.Left = S(x);
        control.Top = S(y);
        control.Width = S(width);
        control.Height = S(height);
        Controls.Add(control);
    }

    private void Place(Control parent, Control control, int x, int y, int width, int height)
    {
        control.Left = S(x);
        control.Top = S(y);
        control.Width = S(width);
        control.Height = S(height);
        parent.Controls.Add(control);
    }

    private static void Stretch(Control control)
    {
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
    }

    private void LoadPorts()
    {
        var selected = (_ports.SelectedItem as ComPort)?.Name;
        _ports.Items.Clear();
        foreach (var port in ComPorts.List())
        {
            _ports.Items.Add(port);
        }

        var index = -1;
        for (var i = 0; i < _ports.Items.Count; i++)
        {
            if (_ports.Items[i] is ComPort port && (port.Name.Equals(selected, StringComparison.OrdinalIgnoreCase) || (selected is null && port.Name.Equals("COM8", StringComparison.OrdinalIgnoreCase))))
            {
                index = i;
                if (selected is not null)
                {
                    break;
                }
            }
        }

        if (index < 0 && _ports.Items.Count > 0)
        {
            index = 0;
        }

        if (index >= 0)
        {
            _ports.SelectedIndex = index;
        }

        UpdatePortHint();
    }

    private void UpdatePortHint()
    {
        if (_ports.Items.Count == 0)
        {
            _portHint.Text = "没有找到串口。接上下载器后，点右边的「刷新串口」。";
            return;
        }

        var port = _ports.SelectedItem as ComPort;
        var name = port is null ? "未选择" : port.Caption;
        var baud = _baud9600.Checked ? "9600" : "115200";
        var entry = _isp.Checked ? "烧录时会自动复位并进入下载" : "保持当前接线，芯片需已经在下载模式";
        _portHint.Text = $"{name} · {baud} · {entry}";
    }

    private void UpdateFileHint()
    {
        var path = _hex.Text.Trim();
        if (path == _hintPath)
        {
            return;
        }

        _hintPath = path;
        if (path.Length == 0)
        {
            _fileHint.ForeColor = Muted;
            _fileHint.Text = "点「浏览」选择要烧录的 HEX 文件。";
            return;
        }

        if (!File.Exists(path))
        {
            _fileHint.ForeColor = Bad;
            _fileHint.Text = "找不到这个文件，请重新选择。";
            return;
        }

        var info = new FileInfo(path);
        _fileHint.ForeColor = Muted;
        _fileHint.Text = $"{info.Name} · {FormatSize(info.Length)}";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }

        return bytes < 1024 * 1024
            ? (bytes / 1024.0).ToString("0.#") + " KB"
            : (bytes / 1024.0 / 1024.0).ToString("0.##") + " MB";
    }

    private void BrowseHex()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择固件 HEX",
            Filter = "Intel HEX (*.hex)|*.hex",
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(_hex.Text))
        {
            dialog.FileName = _hex.Text;
            dialog.InitialDirectory = Path.GetDirectoryName(_hex.Text) ?? string.Empty;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _hex.Text = dialog.FileName;
            _hintPath = "\0";
            UpdateFileHint();
        }
    }

    private void ReadPcb()
    {
        if (!TrySelectPort(out var port, out var baud))
        {
            return;
        }

        SetPhase("正在读取板卡…", Accent);
        Run(() =>
        {
            var pcb = PcbReader.TryRead(port, baud, AppendLog);
            Post(() =>
            {
                ShowPcb(pcb);
                SetPhase(pcb is null ? "没有读到板卡" : "已读到 " + pcb.Name, pcb is null ? Bad : Good);
            });
        });
    }

    private void StartFlash()
    {
        if (!TrySelectPort(out var port, out var baud))
        {
            return;
        }

        var hex = _hex.Text.Trim();
        if (!File.Exists(hex))
        {
            Alert("找不到这个 HEX 文件，请重新选择。");
            return;
        }

        var pcb = PcbReader.TryRead(port, baud, AppendLog);
        ShowPcb(pcb);
        var name = Path.GetFileNameWithoutExtension(hex);
        if (pcb is null)
        {
            if (!Ask("没有读到板卡应答。\r\n仍要强制烧录吗？"))
            {
                return;
            }
        }
        else if (!name.StartsWith(pcb.Number, StringComparison.OrdinalIgnoreCase))
        {
            Alert($"文件 {name} 和板卡 {pcb.Name} 对不上，已停止。");
            return;
        }
        else if (!name.StartsWith(pcb.Name, StringComparison.OrdinalIgnoreCase) && !Ask($"板卡是 {pcb.Name}，文件是 {name}。\r\n版本不一致，仍要继续烧录吗？"))
        {
            return;
        }

        var job = new FlashJob
        {
            PortName = port,
            BaudRate = baud,
            HexPath = hex,
            Verify = _verify.Checked,
            AutoSync = _sync.Checked,
            EnterIsp = _isp.Checked,
        };
        SetPhase("正在烧录…", Accent);
        Run(() =>
        {
            Flasher.Program(job, AppendLog, value => Post(() => SetProgress(value)), _cancelSource!.Token);
            Post(() =>
            {
                SetProgress(100);
                SetPhase("烧录完成", Good);
                AppendLog("烧录完成。");
            });
        });
    }

    private bool TrySelectPort(out string port, out int baud)
    {
        port = (_ports.SelectedItem as ComPort)?.Name ?? string.Empty;
        baud = _baud9600.Checked ? 9600 : 115200;
        if (port.Length == 0)
        {
            Alert("请先在列表里选择一个串口。");
            return false;
        }

        return true;
    }

    private void ShowPcb(PcbIdentity? pcb)
    {
        _info.Text = pcb is null
            ? "没有读到板卡。\r\n请确认串口、波特率和供电。芯片若已经在下载模式，开始烧录时可以选择强制继续。"
            : $"型号    {pcb.Name}\r\n版本    {pcb.VersionText}\r\n原始    {pcb.VersionRaw.Trim()}";
    }

    private void Run(Action work)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        SetIdle(false);
        SetProgress(0);
        _cancelSource = new CancellationTokenSource();
        Task.Run(() =>
        {
            try
            {
                work();
            }
            catch (OperationCanceledException)
            {
                AppendLog("已取消。");
                Post(() => SetPhase("已取消", Ink));
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message);
                Post(() => SetPhase("失败：" + Short(ex.Message), Bad));
            }
            finally
            {
                Post(() =>
                {
                    _busy = false;
                    SetIdle(true);
                    _cancelSource?.Dispose();
                    _cancelSource = null;
                });
            }
        });
    }

    private void SetIdle(bool idle)
    {
        _ports.Enabled = idle;
        _refresh.Enabled = idle;
        _browse.Enabled = idle;
        _read.Enabled = idle;
        _start.Enabled = idle;
        _verify.Enabled = idle;
        _sync.Enabled = idle;
        _baud115200.Enabled = idle;
        _baud9600.Enabled = idle;
        _isp.Enabled = idle;
        _keep.Enabled = idle;
        _hex.Enabled = idle;
        _cancel.Enabled = !idle;
    }

    private void SetPhase(string phase, Color color)
    {
        _phase = phase;
        _phaseColor = color;
        _status.Text = phase;
        _status.ForeColor = color;
    }

    private void SetProgress(int value)
    {
        var percent = Math.Clamp(value, 0, 100);
        _progress.Value = percent;
        _percent.Text = percent + "%";
        _percent.ForeColor = percent == 100 ? Good : percent == 0 ? Muted : Accent;
    }

    private void AppendLog(string message)
    {
        Post(() =>
        {
            _logText.AppendLine(message);
            if (_logText.Length > 200_000)
            {
                _logText.Remove(0, _logText.Length - 150_000);
            }

            _log.Text = _logText.ToString();
            var end = _log.Text.Length;
            SendMessage(_log.Handle, 0x00B1, new IntPtr(end), new IntPtr(end));
            SendMessage(_log.Handle, 0x00B7, IntPtr.Zero, IntPtr.Zero);
            if (_busy && ShowInStatus(message))
            {
                _status.Text = _phase + " · " + FriendlyStep(message);
                _status.ForeColor = _phaseColor;
            }
        });
    }

    private static bool ShowInStatus(string message)
    {
        return message.Length < 100
            && !message.StartsWith("  0x", StringComparison.Ordinal)
            && !message.StartsWith("Vector", StringComparison.Ordinal)
            && !message.StartsWith("OPTR", StringComparison.Ordinal)
            && !message.StartsWith("PCB probe", StringComparison.Ordinal);
    }

    private static string FriendlyStep(string message)
    {
        if (message.StartsWith("Erase (", StringComparison.Ordinal))
        {
            return "正在擦除闪存…";
        }

        if (message == "Erase OK.")
        {
            return "擦除完成";
        }

        if (message.StartsWith("Write", StringComparison.Ordinal))
        {
            return "正在写入…";
        }

        if (message == "Image written.")
        {
            return "写入完成";
        }

        if (message.StartsWith("Go ", StringComparison.Ordinal) || message == "Application started.")
        {
            return "正在启动程序…";
        }

        if (message == "Bootloader ACK.")
        {
            return "已连上下载器";
        }

        if (message.StartsWith("Open ", StringComparison.Ordinal))
        {
            return "正在打开串口…";
        }

        if (message.StartsWith("Chip ID", StringComparison.Ordinal))
        {
            return "已识别芯片";
        }

        if (message.StartsWith("Retry:", StringComparison.Ordinal))
        {
            return "这一步失败了，正在重试…";
        }

        if (message.StartsWith("DTR low", StringComparison.Ordinal))
        {
            return "正在复位并进入下载…";
        }

        if (message.StartsWith("DTR high", StringComparison.Ordinal))
        {
            return "复位已释放";
        }

        if (message.StartsWith("Sync byte", StringComparison.Ordinal))
        {
            return "正在同步…";
        }

        return Short(message);
    }

    private static string Short(string message)
    {
        var line = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return line.Length > 80 ? line[..80] + "…" : line;
    }

    private void Post(Action action)
    {
        lock (_posts)
        {
            _posts.Enqueue(action);
        }
    }

    private void DrainPosts()
    {
        while (true)
        {
            Action? action;
            lock (_posts)
            {
                if (_posts.Count == 0)
                {
                    return;
                }

                action = _posts.Dequeue();
            }

            action();
        }
    }

    private void Alert(string message)
    {
        ShowDialog("无法继续", message, ask: false);
    }

    private bool Ask(string message)
    {
        return ShowDialog("请确认", message, ask: true);
    }

    private bool ShowDialog(string heading, string message, bool ask)
    {
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = heading,
            Text = message,
        };
        if (!ask)
        {
            TaskDialog.ShowDialog(this, page);
            return false;
        }

        var yes = TaskDialogButton.Yes;
        page.Buttons.Add(yes);
        page.Buttons.Add(TaskDialogButton.No);
        return ReferenceEquals(TaskDialog.ShowDialog(this, page), yes);
    }

    private static void SetReadOnly(TextBox box)
    {
        SendMessage(box.Handle, 0x00CF, new IntPtr(1), IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
