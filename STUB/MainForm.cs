using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using MiniWinForm;
using Font = MiniWinForm.Font;
using FontStyle = MiniWinForm.FontStyle;

namespace STUB;

internal sealed class MainForm : Form
{
    private static readonly Color Card = Color.White;
    private static readonly Color Line = Color.FromArgb(217, 224, 236);
    private static readonly Color ButtonLine = Color.FromArgb(203, 213, 227);
    private static readonly Color Ink = Color.FromArgb(31, 41, 55);
    private static readonly Color Muted = Color.FromArgb(110, 122, 136);
    private static readonly Color Accent = Color.FromArgb(20, 98, 168);
    private static readonly Color Soft = Color.FromArgb(247, 249, 252);
    private static readonly Color Good = Color.FromArgb(22, 120, 68);
    private static readonly Color Bad = Color.FromArgb(176, 42, 42);
    private static readonly Color Warn = Color.FromArgb(176, 82, 10);

    private readonly Font _headerFont = new("Segoe UI", 15.5f, FontStyle.Bold);
    private readonly Font _subtitleFont = new("Segoe UI", 9f);
    private readonly Font _cardFont = new("Segoe UI", 10f, FontStyle.Bold);
    private readonly Font _logFont = new("Consolas", 9f);

    private readonly int _scale;
    private readonly StringBuilder _logText = new();
    private readonly Panel _header = new() { BackColor = Card };
    private readonly Panel _headerLine = new() { BackColor = Line };
    private readonly GroupBox _portBox = new();
    private readonly GroupBox _fileBox = new();
    private readonly GroupBox _logBox = new();
    private readonly Label _title = new() { ForeColor = Ink };
    private readonly Label _subtitle = new() { ForeColor = Muted };
    private readonly Label _langLabel = new() { ForeColor = Muted };
    private readonly ComboBox _lang = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 4 };
    private readonly ComboBox _ports = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 10 };
    private readonly Label _baudLabel = new() { ForeColor = Muted };
    private readonly RadioButton _baud115200 = new() { GroupName = "baud", Text = "115200", Checked = true };
    private readonly RadioButton _baud9600 = new() { GroupName = "baud", Text = "9600" };
    private readonly Panel _portSplit = new() { BackColor = Line };
    private readonly Label _entryLabel = new() { ForeColor = Muted };
    private readonly RadioButton _isp = new() { GroupName = "entry", Checked = true };
    private readonly RadioButton _keep = new() { GroupName = "entry" };
    private readonly CheckBox _verify = new() { Checked = true };
    private readonly CheckBox _sync = new() { Checked = true };
    private readonly CheckBox _force = new();
    private readonly TextBox _hex = new();
    private readonly Label _boardNameCap = new() { ForeColor = Muted };
    private readonly Label _boardName = new() { ForeColor = Ink };
    private readonly Label _boardVersionCap = new() { ForeColor = Muted };
    private readonly Label _boardVersion = new() { ForeColor = Ink };
    private readonly Label _boardRawCap = new() { ForeColor = Muted };
    private readonly Label _boardRaw = new() { ForeColor = Ink };
    private readonly Label _boardHint = new() { ForeColor = Muted };
    private readonly TextBox _log = new() { Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, TabStop = false };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly Button _refresh = new();
    private readonly Button _browse = new();
    private readonly Button _read = new();
    private readonly Button _erase = new();
    private readonly Button _start = new();
    private readonly Button _cancel = new() { Enabled = false };
    private readonly Button _clear = new();
    private readonly Label _portHint = new() { ForeColor = Muted };
    private readonly Label _fileHint = new() { ForeColor = Muted };
    private readonly Label _status = new() { ForeColor = Muted };
    private readonly Label _percent = new() { Text = "0%", ForeColor = Muted };
    private readonly MiniWinForm.Timer _timer = new() { Interval = 100 };
    private readonly Queue<Action> _posts = new();
    private CancellationTokenSource? _cancelSource;
    private Phase _phase = Phase.Idle;
    private string _phaseArg = string.Empty;
    private string _step = string.Empty;
    private string _timing = string.Empty;
    private string _hintPath = "\0";
    private PcbIdentity? _pcb;
    private DateTime _runStarted;
    private bool _boardRead;
    private bool _busy;
    private bool _applyingLanguage;
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _loadingSettings = true;

    public MainForm()
    {
        _scale = Math.Max(96, (int)GetDpiForSystem());
        StartPosition = FormStartPosition.CenterScreen;
        // 窗口尺寸含标题栏与边框（96dpi 下约 16x39），这里按客户区 1000x751 反推。
        Width = S(1016);
        Height = S(790);

        _lang.Items.Add("中文");
        _lang.Items.Add("English");

        _title.Font = _headerFont;
        _subtitle.Font = _subtitleFont;
        _log.Font = _logFont;

        Place(_header, 0, 0, 1000, 64);
        Place(_header, _title, 20, 8, 640, 32);
        Place(_header, _subtitle, 22, 39, 660, 20);
        Place(_header, _langLabel, 742, 27, 80, 20);
        Place(_header, _lang, 828, 23, 156, 26);
        _header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _title.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _subtitle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _langLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _lang.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        Place(_headerLine, 0, 63, 1000, 1);
        _headerLine.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        SetupCard(_portBox, 16, 76, 968, 146);
        Place(_portBox, _refresh, 862, 8, 92, 26);
        Place(_portBox, _ports, 14, 48, 340, 26);
        Place(_portBox, _portHint, 368, 52, 588, 20);
        Place(_portBox, _baudLabel, 14, 110, 72, 22);
        Place(_portBox, _baud115200, 88, 106, 104, 24);
        Place(_portBox, _baud9600, 196, 106, 80, 24);
        Place(_portBox, _portSplit, 290, 106, 1, 24);
        Place(_portBox, _entryLabel, 314, 110, 74, 22);
        Place(_portBox, _isp, 392, 106, 200, 24);
        Place(_portBox, _keep, 596, 106, 220, 24);
        Stretch(_portBox);
        Stretch(_portHint);
        _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        SetupCard(_fileBox, 16, 232, 968, 184);
        Place(_fileBox, _hex, 14, 48, 772, 28);
        Place(_fileBox, _browse, 794, 46, 160, 30);
        Place(_fileBox, _fileHint, 14, 82, 940, 20);
        Place(_fileBox, _verify, 14, 108, 180, 24);
        Place(_fileBox, _sync, 200, 108, 160, 24);
        Place(_fileBox, _force, 372, 108, 150, 24);
        // caption 宽度按英文 "Version" 定（约 43px @9pt），否则英文下会跟值粘在一起。
        Place(_fileBox, _boardNameCap, 14, 146, 56, 20);
        Place(_fileBox, _boardName, 74, 146, 160, 20);
        Place(_fileBox, _boardVersionCap, 246, 146, 56, 20);
        Place(_fileBox, _boardVersion, 306, 146, 160, 20);
        Place(_fileBox, _boardRawCap, 478, 146, 56, 20);
        Place(_fileBox, _boardRaw, 538, 146, 402, 20);
        Place(_fileBox, _boardHint, 14, 146, 940, 20);
        Stretch(_fileBox);
        Stretch(_hex);
        Stretch(_fileHint);
        Stretch(_boardRaw);
        Stretch(_boardHint);
        _browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        Place(_read, 16, 426, 116, 36);
        Place(_erase, 140, 426, 104, 36);
        Place(_start, 252, 426, 140, 36);
        Place(_cancel, 400, 426, 104, 36);
        Place(_status, 516, 433, 452, 22);
        Stretch(_status);
        Place(_progress, 16, 472, 880, 16);
        Stretch(_progress);
        Place(_percent, 904, 470, 64, 20);
        _percent.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        SetupCard(_logBox, 16, 498, 968, 245);
        Place(_logBox, _clear, 862, 8, 92, 26);
        Place(_logBox, _log, 14, 48, 940, 187);
        _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        StyleButton(_refresh, Card, Ink, ButtonLine);
        StyleButton(_browse, Card, Ink, ButtonLine);
        StyleButton(_read, Card, Ink, ButtonLine);
        StyleButton(_erase, Card, Warn, ButtonLine);
        StyleButton(_start, Accent, Color.White, Accent);
        StyleButton(_cancel, Card, Muted, ButtonLine);
        StyleButton(_clear, Card, Ink, ButtonLine);

        _refresh.Click += (_, _) => LoadPorts();
        _browse.Click += (_, _) => BrowseHex();
        _read.Click += (_, _) => ReadBoard();
        _erase.Click += (_, _) => EraseFlash();
        _start.Click += (_, _) => StartFlash();
        _cancel.Click += (_, _) => _cancelSource?.Cancel();
        _clear.Click += (_, _) => ClearLog();
        _ports.SelectedIndexChanged += (_, _) => OnOptionChanged();
        _baud115200.CheckedChanged += (_, _) => OnOptionChanged();
        _baud9600.CheckedChanged += (_, _) => OnOptionChanged();
        _isp.CheckedChanged += (_, _) => OnOptionChanged();
        _keep.CheckedChanged += (_, _) => OnOptionChanged();
        _verify.CheckedChanged += (_, _) => OnOptionChanged();
        _sync.CheckedChanged += (_, _) => OnOptionChanged();
        _force.CheckedChanged += (_, _) => OnOptionChanged();
        _lang.SelectedIndexChanged += (_, _) => OnLanguageChanged();
        Texts.Changed += OnTextsChanged;
        _timer.Tick += (_, _) =>
        {
            DrainPosts();
            UpdateFileHint();
        };
        Shown += (_, _) =>
        {
            _timer.Enabled = true;
            SetReadOnly(_log);
            ApplySettings();
            LoadPorts();
            UpdateFileHint(force: true);
            AppendLog(_hex.Text.Length == 0 ? "No HEX selected." : "HEX " + _hex.Text);
        };
        FormClosing += (_, _) => SaveSettings();

        ApplyText();
        SetPhase(Phase.Idle);
    }

    private void SetupCard(GroupBox box, int x, int y, int width, int height)
    {
        box.Style = GroupBoxStyle.Card;
        box.BackColor = Card;
        box.ForeColor = Ink;
        box.BorderColor = Line;
        box.Font = _cardFont;
        Place(box, x, y, width, height);
    }

    private static void StyleButton(Button button, Color background, Color foreground, Color border)
    {
        button.Style = ButtonStyle.Flat;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.BorderColor = border;
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

    private void OnLanguageChanged()
    {
        if (_applyingLanguage)
        {
            return;
        }

        Texts.Set(_lang.SelectedIndex == 1 ? Lang.En : Lang.Zh);
    }

    private void OnTextsChanged()
    {
        ApplyText();
        RenderStatus();
    }

    /// <summary>把整窗文案按当前语言重刷一遍，动态提示再单独算。</summary>
    private void ApplyText()
    {
        _applyingLanguage = true;
        try
        {
            Text = Texts.T("AppTitle");
            _title.Text = Texts.T("AppTitle");
            _subtitle.Text = Texts.T("HeaderSubtitle");
            _langLabel.Text = Texts.T("Language");
            _lang.SelectedIndex = Texts.IsEnglish ? 1 : 0;

            _portBox.Text = Texts.T("PortSection");
            _fileBox.Text = Texts.T("FirmwareSection");
            _logBox.Text = Texts.T("LogSection");

            _baudLabel.Text = Texts.T("BaudRate");
            _entryLabel.Text = Texts.T("EntryMode");
            _isp.Text = Texts.T("EntryIsp");
            _keep.Text = Texts.T("EntryKeep");
            _verify.Text = Texts.T("Verify");
            _sync.Text = Texts.T("AutoSync");
            _force.Text = Texts.T("Force");
            _refresh.Text = Texts.T("RefreshPorts");
            _browse.Text = Texts.T("BrowseHex");
            _read.Text = Texts.T("ReadPcb");
            _erase.Text = Texts.T("Erase");
            _start.Text = Texts.T("StartIsp");
            _cancel.Text = Texts.T("Cancel");
            _clear.Text = Texts.T("ClearLog");

            _boardNameCap.Text = Texts.T("BoardNameLabel");
            _boardVersionCap.Text = Texts.T("BoardVersionLabel");
            _boardRawCap.Text = Texts.T("BoardRawLabel");
        }
        finally
        {
            _applyingLanguage = false;
        }

        UpdatePortHint();
        UpdateFileHint(force: true);
        ShowBoard();
    }

    /// <summary>启动时把上次的固件路径和选项套回界面；没有记录就退回默认扫描。</summary>
    private void ApplySettings()
    {
        try
        {
            _baud115200.Checked = _settings.Baud != 9600;
            _baud9600.Checked = _settings.Baud == 9600;
            _isp.Checked = !_settings.KeepInBootloader;
            _keep.Checked = _settings.KeepInBootloader;
            _verify.Checked = _settings.Verify;
            _sync.Checked = _settings.Sync;
            _force.Checked = _settings.Force;

            // 记忆的路径即使文件已经不在了也照填，交给下面的提示去报红，比悄悄换成别的固件安全。
            _hex.Text = _settings.Hex.Length > 0
                ? _settings.Hex
                : ComPorts.FindDefaultHex() ?? string.Empty;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    /// <summary>任一选项/串口变了：刷新提示行并立刻落盘，避免异常退出丢配置。</summary>
    private void OnOptionChanged()
    {
        UpdatePortHint();
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_loadingSettings)
        {
            return;
        }

        _settings.Hex = _hex.Text.Trim();
        _settings.Port = (_ports.SelectedItem as ComPort)?.Name ?? string.Empty;
        _settings.Baud = _baud9600.Checked ? 9600 : 115200;
        _settings.KeepInBootloader = _keep.Checked;
        _settings.Verify = _verify.Checked;
        _settings.Sync = _sync.Checked;
        _settings.Force = _force.Checked;
        _settings.Save();
    }

    private void LoadPorts()
    {
        // 优先沿用当前选择，其次上次记住的端口；都没有才退回产线常用的 COM8。
        var wanted = (_ports.SelectedItem as ComPort)?.Name;
        if (string.IsNullOrEmpty(wanted))
        {
            wanted = _settings.Port;
        }

        _ports.Items.Clear();
        foreach (var port in ComPorts.List())
        {
            _ports.Items.Add(port);
        }

        var index = FindPort(wanted);
        if (index < 0)
        {
            index = FindPort("COM8");
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

    private int FindPort(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return -1;
        }

        for (var i = 0; i < _ports.Items.Count; i++)
        {
            if (_ports.Items[i] is ComPort port && port.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void UpdatePortHint()
    {
        if (_ports.Items.Count == 0)
        {
            _portHint.Text = Texts.T("NoPorts");
            return;
        }

        var name = _ports.SelectedItem is ComPort port ? port.Caption : Texts.T("PortNone");
        var baud = _baud9600.Checked ? "9600" : "115200";
        var entry = _isp.Checked ? Texts.T("EntryHintIsp") : Texts.T("EntryHintKeep");
        _portHint.Text = string.Format(Texts.T("PortHint"), name, baud, entry);
    }

    private void UpdateFileHint(bool force = false)
    {
        var path = _hex.Text.Trim();
        if (!force && path == _hintPath)
        {
            return;
        }

        _hintPath = path;

        // 路径一变就记下来（手输也算）。这个方法是 timer 每 100ms 调的，
        // 靠上面的 _hintPath 去重，只在真正变化时落盘，等于天然的 debounce。
        SaveSettings();

        if (path.Length == 0)
        {
            _fileHint.ForeColor = Muted;
            _fileHint.Text = Texts.T("FileHintEmpty");
            return;
        }

        if (!File.Exists(path))
        {
            _fileHint.ForeColor = Bad;
            _fileHint.Text = Texts.T("FileHintMissing");
            return;
        }

        var info = new FileInfo(path);
        _fileHint.ForeColor = Muted;
        _fileHint.Text = string.Format(Texts.T("FileInfo"), info.Name, FormatSize(info.Length));
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
            Title = Texts.T("HexDialogTitle"),
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
            UpdateFileHint(force: true);
            SaveSettings();
        }
    }

    private void ClearLog()
    {
        _logText.Clear();
        _log.Text = string.Empty;
        _step = string.Empty;
        RenderStatus();
    }

    private void ReadBoard()
    {
        if (!TrySelectPort(out var port, out var baud))
        {
            return;
        }

        SetPhase(Phase.Reading);
        Run(() =>
        {
            var pcb = PcbReader.TryRead(port, baud, AppendLog);
            Post(() =>
            {
                SetBoard(pcb);
                SetPhase(pcb is null ? Phase.NoBoard : Phase.ReadOk, pcb?.Name ?? string.Empty);
            });
        });
    }

    private void EraseFlash()
    {
        if (!TrySelectPort(out var port, out var baud))
        {
            return;
        }

        if (!Ask(Texts.T("ConfirmErase")))
        {
            return;
        }

        var job = new FlashJob
        {
            PortName = port,
            BaudRate = baud,
            HexPath = "erase",
            AutoSync = _sync.Checked,
            EnterIsp = _isp.Checked,
        };
        SetPhase(Phase.Erasing);
        Run(() =>
        {
            Flasher.Erase(job, AppendLog, _cancelSource!.Token);
            Finish(Phase.Erased);
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
            Alert(Texts.T("HexMissing"));
            return;
        }

        var force = _force.Checked;
        PcbIdentity? pcb = null;
        if (!force)
        {
            SetPhase(Phase.Reading);
            pcb = PcbReader.TryRead(port, baud, AppendLog);
            SetBoard(pcb);
        }

        var name = Path.GetFileNameWithoutExtension(hex);
        if (pcb is null && !force)
        {
            if (!Ask(Texts.T("ConfirmNoPcb")))
            {
                SetPhase(Phase.Idle);
                return;
            }

            _force.Checked = true;
        }
        else if (pcb is not null && !force && !name.StartsWith(pcb.Number, StringComparison.OrdinalIgnoreCase))
        {
            Alert(string.Format(Texts.T("HexMismatch"), name, pcb.Name));
            SetPhase(Phase.Idle);
            return;
        }
        else if (pcb is not null && !force
                 && !name.StartsWith(pcb.Name, StringComparison.OrdinalIgnoreCase)
                 && !Ask(string.Format(Texts.T("ConfirmRevision"), pcb.Name, name)))
        {
            SetPhase(Phase.Idle);
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
        SetPhase(Phase.Flashing);
        Run(() =>
        {
            Flasher.Program(job, AppendLog, value => Post(() => SetProgress(value)), _cancelSource!.Token);
            Finish(Phase.Done);
        });
    }

    private bool TrySelectPort(out string port, out int baud)
    {
        port = (_ports.SelectedItem as ComPort)?.Name ?? string.Empty;
        baud = _baud9600.Checked ? 9600 : 115200;
        if (port.Length == 0)
        {
            Alert(Texts.T("SelectPort"));
            return false;
        }

        return true;
    }

    private void SetBoard(PcbIdentity? pcb)
    {
        _boardRead = true;
        _pcb = pcb;
        ShowBoard();
    }

    private void ShowBoard()
    {
        // 未读取 / 无响应时用单行提示铺满整行；读到板卡则展开「型号 / 版本 / 原始」三段文字。
        var hint = !_boardRead
            ? Texts.T("BoardEmpty")
            : _pcb is null
                ? Texts.T("BoardNoResponse")
                : null;

        if (hint is not null)
        {
            _boardHint.Text = hint;
            _boardHint.Visible = true;
            _boardNameCap.Visible = false;
            _boardName.Visible = false;
            _boardVersionCap.Visible = false;
            _boardVersion.Visible = false;
            _boardRawCap.Visible = false;
            _boardRaw.Visible = false;
            return;
        }

        var pcb = _pcb!;
        var raw = pcb.VersionRaw.Trim();
        var showRaw = raw.Length > 0;

        _boardHint.Visible = false;
        _boardNameCap.Visible = true;
        _boardName.Visible = true;
        _boardName.Text = pcb.Name;
        _boardVersionCap.Visible = true;
        _boardVersion.Visible = true;
        _boardVersion.Text = pcb.VersionText;
        _boardRawCap.Visible = showRaw;
        _boardRaw.Visible = showRaw;
        _boardRaw.Text = raw;
    }

    private void Run(Action work)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _runStarted = DateTime.Now;
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
                AppendLog("Canceled.");
                Post(() => SetPhase(Phase.Canceled));
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message);
                Post(() => SetPhase(Phase.Failed, Short(ex.Message)));
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

    /// <summary>成功收尾：满进度 + 完成状态 + 「用时 xx · 完成 hh:mm:ss」。</summary>
    private void Finish(Phase phase)
    {
        var finished = DateTime.Now;
        var elapsed = finished - _runStarted;
        var timing = string.Format(
            Texts.T("DoneTiming"),
            FormatDuration(elapsed),
            finished.ToString("HH:mm:ss"));
        Post(() =>
        {
            SetProgress(100);
            SetPhase(phase, string.Empty, timing);
        });
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return elapsed.TotalHours >= 1
            ? string.Create(culture, $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(culture, $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}");
    }

    private void SetIdle(bool idle)
    {
        _ports.Enabled = idle;
        _refresh.Enabled = idle;
        _browse.Enabled = idle;
        _read.Enabled = idle;
        _erase.Enabled = idle;
        _start.Enabled = idle;
        _verify.Enabled = idle;
        _sync.Enabled = idle;
        _force.Enabled = idle;
        _baud115200.Enabled = idle;
        _baud9600.Enabled = idle;
        _isp.Enabled = idle;
        _keep.Enabled = idle;
        _hex.Enabled = idle;
        _cancel.Enabled = !idle;
    }

    private void SetPhase(Phase phase, string arg = "", string timing = "")
    {
        _phase = phase;
        _phaseArg = arg;
        _step = string.Empty;
        _timing = timing;
        RenderStatus();
    }

    private void RenderStatus()
    {
        var text = PhaseText();
        if (_step.Length > 0)
        {
            text += " · " + _step;
        }

        if (_timing.Length > 0)
        {
            text += " · " + _timing;
        }

        _status.Text = text;
        _status.ForeColor = PhaseColor();
    }

    private string PhaseText()
    {
        return _phase switch
        {
            Phase.Idle => Texts.T("StatusIdle"),
            Phase.Reading => Texts.T("PhaseReading"),
            Phase.ReadOk => string.Format(Texts.T("PhaseReadOk"), _phaseArg),
            Phase.NoBoard => Texts.T("PhaseNoBoard"),
            Phase.Erasing => Texts.T("PhaseErasing"),
            Phase.Erased => Texts.T("PhaseErased"),
            Phase.Flashing => Texts.T("PhaseFlashing"),
            Phase.Done => Texts.T("PhaseDone"),
            Phase.Canceled => Texts.T("PhaseCanceled"),
            Phase.Failed => string.Format(Texts.T("PhaseFailed"), _phaseArg),
            _ => string.Empty,
        };
    }

    private Color PhaseColor()
    {
        return _phase switch
        {
            Phase.ReadOk or Phase.Erased or Phase.Done => Good,
            Phase.NoBoard or Phase.Failed => Bad,
            Phase.Reading or Phase.Erasing or Phase.Flashing => Accent,
            _ => Ink,
        };
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
                _step = FriendlyStep(message);
                RenderStatus();
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
            return Texts.T("StepErase");
        }

        if (message is "Erase OK." or "Erase OK. Flash is blank.")
        {
            return Texts.T("StepEraseOk");
        }

        if (message.StartsWith("Write", StringComparison.Ordinal))
        {
            return Texts.T("StepWrite");
        }

        if (message == "Image written.")
        {
            return Texts.T("StepWriteDone");
        }

        if (message.StartsWith("Go ", StringComparison.Ordinal) || message == "Application started.")
        {
            return Texts.T("StepStart");
        }

        if (message == "Bootloader ACK.")
        {
            return Texts.T("StepAck");
        }

        if (message.StartsWith("Open ", StringComparison.Ordinal))
        {
            return Texts.T("StepOpen");
        }

        if (message.StartsWith("Chip ID", StringComparison.Ordinal))
        {
            return Texts.T("StepChip");
        }

        if (message.StartsWith("Retry:", StringComparison.Ordinal))
        {
            return Texts.T("StepRetry");
        }

        if (message.StartsWith("DTR low", StringComparison.Ordinal))
        {
            return Texts.T("StepReset");
        }

        if (message.StartsWith("DTR high", StringComparison.Ordinal))
        {
            return Texts.T("StepResetDone");
        }

        if (message.StartsWith("Sync byte", StringComparison.Ordinal))
        {
            return Texts.T("StepSync");
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
        ShowDialog("Alert", message, ask: false);
    }

    private bool Ask(string message)
    {
        return ShowDialog("Ask", message, ask: true);
    }

    private bool ShowDialog(string kind, string message, bool ask)
    {
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = Texts.T(kind == "Ask" ? "DlgAsk" : "DlgAlert"),
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

    private enum Phase
    {
        Idle,
        Reading,
        ReadOk,
        NoBoard,
        Erasing,
        Erased,
        Flashing,
        Done,
        Canceled,
        Failed,
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
