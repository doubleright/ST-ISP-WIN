using Color = System.Drawing.Color;
using MiniWinForm;

namespace ControlGallery;

internal sealed class GalleryForm : Form
{
    private readonly int _scale;
    private readonly ToolStripStatusLabel _status;
    private readonly ToolTip _tips = new();
    private readonly TextBox _name = new();
    private readonly TextBox _notes = new() { Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _log = new() { Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical };
    private readonly ListBox _list = new();
    private readonly ListView _details = new() { View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
    private readonly Label _rangeValue = new();
    private readonly MiniWinForm.Timer _timer = new() { Interval = 40 };
    private readonly Image _swatch;
    private int _listSerial = 3;

    internal TabControl TabHost { get; private set; } = null!;

    public GalleryForm()
    {
        _scale = Math.Max(96, (int)Program.GetDpiForSystem());
        _swatch = LoadSwatch();
        Text = "MiniWinForm 控件示例";
        StartPosition = FormStartPosition.CenterScreen;
        Width = S(1080);
        Height = S(760);

        var status = new StatusStrip();
        _status = (ToolStripStatusLabel)status.Items.Add("就绪");
        _status.Spring = true;

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("文件");
        file.DropDownItems.Add(new ToolStripMenuItem("打开…", (_, _) => BrowseFile()));
        file.DropDownItems.Add(new ToolStripMenuItem("另存为…", (_, _) => SaveFile()));
        file.DropDownItems.Add(new ToolStripMenuItem("选择文件夹…", (_, _) => BrowseFolder()));
        file.DropDownItems.Add(new ToolStripMenuItem("退出", (_, _) => Close()));
        var help = new ToolStripMenuItem("帮助");
        help.DropDownItems.Add(new ToolStripMenuItem("关于", (_, _) => ShowAbout()));
        menu.Items.Add(file);
        menu.Items.Add(help);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        TabHost = tabs;
        tabs.TabPages.Add(BuildInputPage());
        tabs.TabPages.Add(BuildChoicePage());
        tabs.TabPages.Add(BuildListPage());
        tabs.TabPages.Add(BuildRangePage());
        tabs.TabPages.Add(BuildDatePage());
        tabs.TabPages.Add(BuildLayoutPage());
        tabs.TabPages.Add(BuildChromePage());
        tabs.SelectedIndexChanged += (_, _) => SetStatus("当前页：" + tabs.SelectedTab?.Text);

        Controls.Add(menu);
        Controls.Add(tabs);
        Controls.Add(status);

        _tips.SetToolTip(_list, "右键可以添加或删除");
        _tips.SetToolTip(_progress, "拖动滑块，或点“跑一遍”");
        _tips.SetToolTip(_details, "Details 视图，整行选择");

        _timer.Tick += (_, _) =>
        {
            if (_progress.Value >= _progress.Maximum)
            {
                _timer.Enabled = false;
                SetStatus("进度完成");
                return;
            }

            _progress.PerformStep();
            _rangeValue.Text = _progress.Value + "%";
        };
        FormClosing += (_, _) =>
        {
            _timer.Enabled = false;
            _tips.Dispose();
            _swatch.Dispose();
        };
    }

    private TabPage BuildInputPage()
    {
        var page = new TabPage("输入");
        Place(page, new Label { Text = "TextBox" }, 16, 18, 100, 22);
        Place(page, _name, 124, 14, 240, 26);
        Place(page, new Label { Text = "TextBox 多行" }, 16, 54, 100, 22);
        Place(page, _notes, 124, 50, 240, 110);

        Place(page, new Label { Text = "RichTextBox" }, 16, 176, 100, 22);
        var rich = new RichTextBox();
        rich.Text = "这是 RichTextBox。\r\n下面的按钮会写入一段 RTF。";
        Place(page, rich, 124, 172, 240, 140);
        Place(page, new Label { Text = "LinkLabel" }, 16, 328, 100, 22);
        var link = new LinkLabel { Text = "查看 <a href=\"readme\">链接说明</a>" };
        Place(page, link, 124, 324, 220, 24);
        link.LinkClicked += (_, e) => SetStatus("LinkLabel " + e.Url);

        Place(page, new Label { Text = "Button" }, 16, 366, 100, 22);
        var insert = new Button { Text = "插入 RTF" };
        var write = new Button { Text = "写入记录" };
        var disabled = new Button { Text = "不可用", Enabled = false };
        Place(page, insert, 124, 362, 100, 30);
        Place(page, write, 232, 362, 100, 30);
        Place(page, disabled, 124, 400, 100, 30);
        insert.Click += (_, _) => rich.Rtf = "{\\rtf1\\ansi\\b MiniWinForm\\b0  控件示例}";
        write.Click += (_, _) => _log.Text = "姓名：" + _name.Text + "\r\n说明：" + _notes.Text;
        Place(page, new Label { Text = "Label：静态文字" }, 232, 404, 160, 22);

        Place(page, new Label { Text = "记录" }, 390, 14, 48, 22);
        Place(page, _log, 390, 40, 630, 470);
        return page;
    }

    private TabPage BuildChoicePage()
    {
        var page = new TabPage("选择");
        Place(page, new Label { Text = "ComboBox DropDownList" }, 16, 16, 180, 22);
        var city = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        city.Items.AddRange("北京", "上海", "广州", "深圳");
        city.SelectedIndex = 0;
        Place(page, city, 210, 12, 200, 24);
        city.SelectedIndexChanged += (_, _) => SetStatus("DropDownList：" + city.SelectedItem);

        Place(page, new Label { Text = "ComboBox DropDown" }, 16, 52, 180, 22);
        var editable = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        editable.Items.AddRange("北京", "上海", "广州", "深圳");
        Place(page, editable, 210, 48, 200, 24);
        editable.SelectedIndexChanged += (_, _) => SetStatus("DropDown：" + editable.SelectedItem);

        Place(page, new Label { Text = "ComboBox Simple" }, 16, 88, 180, 22);
        var simple = new ComboBox { DropDownStyle = ComboBoxStyle.Simple };
        simple.Items.AddRange("北京", "上海", "广州", "深圳");
        simple.SelectedIndex = 0;
        Place(page, simple, 210, 84, 200, 110);
        simple.SelectedIndexChanged += (_, _) => SetStatus("Simple：" + simple.SelectedItem);

        var density = new GroupBox { Text = "GroupBox · RadioButton" };
        Place(page, density, 440, 12, 250, 132);
        var normal = new RadioButton { Text = "标准", Checked = true };
        var compact = new RadioButton { Text = "紧凑" };
        var loose = new RadioButton { Text = "宽松" };
        Place(density, normal, 16, 28, 180, 24);
        Place(density, compact, 16, 56, 180, 24);
        Place(density, loose, 16, 84, 180, 24);
        normal.CheckedChanged += (_, _) => OnDensity(normal);
        compact.CheckedChanged += (_, _) => OnDensity(compact);
        loose.CheckedChanged += (_, _) => OnDensity(loose);

        Place(page, new Label { Text = "CheckBox" }, 440, 156, 120, 22);
        var border = new CheckBox { Text = "显示网格线", Checked = true };
        var fullRow = new CheckBox { Text = "整行选择", Checked = true };
        var multi = new CheckBox { Text = "允许多选" };
        Place(page, border, 440, 182, 180, 24);
        Place(page, fullRow, 440, 210, 180, 24);
        Place(page, multi, 440, 238, 180, 24);
        border.CheckedChanged += (_, _) => _details.GridLines = border.Checked;
        fullRow.CheckedChanged += (_, _) => _details.FullRowSelect = fullRow.Checked;
        multi.CheckedChanged += (_, _) => _details.MultiSelect = multi.Checked;

        Place(page, new Label { Text = "CheckedListBox" }, 720, 16, 160, 22);
        var checks = new CheckedListBox { CheckOnClick = true };
        checks.Items.Add("菜单", true);
        checks.Items.Add("工具提示", true);
        checks.Items.Add("状态栏", false);
        checks.Items.Add("右键菜单", false);
        Place(page, checks, 720, 42, 240, 180);
        checks.ItemCheck += (_, e) => SetStatus(checks.Items[e.Index] + (e.NewValue ? " 已勾选" : " 已取消"));
        return page;
    }

    private TabPage BuildListPage()
    {
        var page = new TabPage("列表");
        Place(page, new Label { Text = "ListBox · 右键 ContextMenuStrip" }, 16, 8, 250, 22);
        _list.Items.AddRange("按钮", "文本框", "组合框");
        Place(page, _list, 16, 32, 250, 340);
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("添加一项", (_, _) => AddListItem()));
        menu.Items.Add(new ToolStripMenuItem("删除所选", (_, _) => RemoveListItem()));
        _list.ContextMenuStrip = menu;
        _list.SelectedIndexChanged += (_, _) => SetStatus("列表：" + (_list.SelectedItem ?? "（无）"));

        var add = new Button { Text = "添加" };
        var remove = new Button { Text = "删除" };
        Place(page, add, 16, 384, 88, 30);
        Place(page, remove, 112, 384, 88, 30);
        add.Click += (_, _) => AddListItem();
        remove.Click += (_, _) => RemoveListItem();

        _details.Columns.Add("控件", 120);
        _details.Columns.Add("类别", 100);
        _details.Columns.Add("说明", 180);
        AddDetail("Button", "按钮", "点击");
        AddDetail("ComboBox", "选择", "下拉列表");
        AddDetail("TreeView", "列表", "节点");
        AddDetail("DateTimePicker", "日期", "长日期、短日期、时间");
        AddDetail("ProgressBar", "数值", "Blocks、Continuous、Marquee");
        Place(page, new Label { Text = "ListView" }, 284, 8, 80, 22);
        var views = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        views.Items.AddRange("Details", "List", "LargeIcon", "SmallIcon");
        views.SelectedIndex = 0;
        Place(page, views, 360, 4, 140, 24);
        views.SelectedIndexChanged += (_, _) =>
        {
            _details.View = views.SelectedIndex switch
            {
                1 => View.List,
                2 => View.LargeIcon,
                3 => View.SmallIcon,
                _ => View.Details,
            };
            SetStatus("ListView " + views.SelectedItem);
        };
        Place(page, _details, 284, 36, 420, 378);
        _details.SelectedIndexChanged += (_, _) =>
        {
            ListViewItem? item = _details.SelectedItems.Count == 0 ? null : _details.SelectedItems[0];
            SetStatus(item is null ? "未选择表格行" : "表格：" + item.Text);
        };

        var tree = new TreeView();
        TreeNode input = tree.Nodes.Add("输入");
        input.Nodes.Add("TextBox");
        input.Nodes.Add("RichTextBox");
        TreeNode lists = tree.Nodes.Add("列表");
        lists.Nodes.Add("ListBox");
        lists.Nodes.Add("ListView");
        input.Expand();
        lists.Expand();
        Place(page, new Label { Text = "TreeView" }, 720, 8, 120, 22);
        Place(page, tree, 720, 36, 280, 378);
        tree.AfterSelect += (_, _) => SetStatus("树节点：" + tree.SelectedNode?.Text);
        return page;
    }

    private TabPage BuildRangePage()
    {
        var page = new TabPage("数值");
        Place(page, new Label { Text = "NumericUpDown" }, 16, 16, 120, 22);
        var number = new NumericUpDown { Minimum = 0, Maximum = 20, Increment = 0.5m, DecimalPlaces = 1, Value = 2 };
        Place(page, number, 150, 12, 120, 26);
        var numberText = new Label { Text = "2.0" };
        Place(page, numberText, 280, 16, 80, 22);
        number.ValueChanged += (_, _) =>
        {
            numberText.Text = number.Value.ToString("0.0");
            SetStatus("NumericUpDown " + numberText.Text);
        };

        Place(page, new Label { Text = "TrackBar" }, 16, 58, 120, 22);
        var track = new TrackBar { Minimum = 0, Maximum = 100, Value = 30, TickFrequency = 10 };
        Place(page, track, 150, 48, 360, 45);
        _progress.Value = track.Value;
        _progress.Step = 4;
        Place(page, new Label { Text = "ProgressBar Continuous" }, 16, 104, 180, 22);
        Place(page, _progress, 210, 106, 300, 18);
        _rangeValue.Text = track.Value + "%";
        Place(page, _rangeValue, 520, 104, 64, 22);
        track.ValueChanged += (_, _) =>
        {
            _progress.Value = track.Value;
            _rangeValue.Text = track.Value + "%";
        };

        var blocks = new ProgressBar { Minimum = 0, Maximum = 100, Value = 65, Style = ProgressBarStyle.Blocks };
        var marquee = new ProgressBar { Style = ProgressBarStyle.Marquee };
        Place(page, new Label { Text = "ProgressBar Blocks" }, 16, 136, 180, 22);
        Place(page, blocks, 210, 138, 300, 18);
        Place(page, new Label { Text = "ProgressBar Marquee" }, 16, 168, 180, 22);
        Place(page, marquee, 210, 170, 300, 18);

        Place(page, new Label { Text = "Timer" }, 16, 206, 120, 22);
        var run = new Button { Text = "跑一遍" };
        Place(page, run, 150, 200, 110, 30);
        run.Click += (_, _) =>
        {
            _progress.Value = 0;
            _rangeValue.Text = "0%";
            _timer.Enabled = true;
        };

        var vertical = new TrackBar { Orientation = Orientation.Vertical, Minimum = 0, Maximum = 10, Value = 4 };
        Place(page, new Label { Text = "TrackBar 垂直" }, 640, 16, 120, 22);
        Place(page, vertical, 640, 40, 45, 180);
        var verticalText = new Label { Text = "4" };
        Place(page, verticalText, 696, 112, 40, 22);
        vertical.ValueChanged += (_, _) => verticalText.Text = vertical.Value.ToString();

        Place(page, new Label { Text = "HScrollBar" }, 16, 250, 120, 22);
        var horizontalBar = new HScrollBar { Minimum = 0, Maximum = 100, Value = 20, LargeChange = 10 };
        Place(page, horizontalBar, 150, 250, 360, 18);
        Place(page, new Label { Text = "VScrollBar" }, 16, 286, 120, 22);
        var verticalBar = new VScrollBar { Minimum = 0, Maximum = 100, Value = 35, LargeChange = 10 };
        Place(page, verticalBar, 150, 286, 18, 150);
        var scrollText = new Label { Text = "水平 20    垂直 35" };
        Place(page, scrollText, 180, 286, 240, 22);
        void ShowScroll() => scrollText.Text = "水平 " + horizontalBar.Value + "    垂直 " + verticalBar.Value;
        horizontalBar.ValueChanged += (_, _) => ShowScroll();
        verticalBar.ValueChanged += (_, _) => ShowScroll();
        return page;
    }

    private TabPage BuildDatePage()
    {
        var page = new TabPage("日期");
        Place(page, new Label { Text = "DateTimePicker Long" }, 16, 16, 170, 22);
        var longDate = new DateTimePicker { Format = DateTimePickerFormat.Long };
        Place(page, longDate, 200, 12, 240, 26);
        Place(page, new Label { Text = "DateTimePicker Short" }, 16, 52, 170, 22);
        var shortDate = new DateTimePicker { Format = DateTimePickerFormat.Short };
        Place(page, shortDate, 200, 48, 180, 26);
        Place(page, new Label { Text = "DateTimePicker Time" }, 16, 88, 170, 22);
        var time = new DateTimePicker { Format = DateTimePickerFormat.Time };
        Place(page, time, 200, 84, 180, 26);
        Place(page, new Label { Text = "DateTimePicker Custom" }, 16, 124, 170, 22);
        var custom = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm",
            ShowUpDown = true,
        };
        Place(page, custom, 200, 120, 210, 26);
        Place(page, new Label { Text = "DateTimePicker 可选" }, 16, 160, 170, 22);
        var optional = new DateTimePicker { ShowCheckBox = true };
        Place(page, optional, 200, 156, 210, 26);
        longDate.ValueChanged += (_, _) => SetStatus("Long " + longDate.Value.ToString("yyyy-MM-dd"));
        shortDate.ValueChanged += (_, _) => SetStatus("Short " + shortDate.Value.ToString("yyyy-MM-dd"));
        time.ValueChanged += (_, _) => SetStatus("Time " + time.Value.ToString("HH:mm:ss"));
        custom.ValueChanged += (_, _) => SetStatus("Custom " + custom.Value.ToString("yyyy-MM-dd HH:mm"));
        optional.ValueChanged += (_, _) => SetStatus(optional.Checked ? "可选 " + optional.Value.ToString("yyyy-MM-dd") : "可选 未勾选");

        Place(page, new Label { Text = "MonthCalendar" }, 520, 16, 140, 22);
        var month = new MonthCalendar();
        Place(page, month, 520, 42, 240, 180);
        month.DateChanged += (_, _) => SetStatus("MonthCalendar " + month.SelectionStart.ToString("yyyy-MM-dd"));
        return page;
    }

    private TabPage BuildLayoutPage()
    {
        var page = new TabPage("布局");
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var splitter = new Splitter { Dock = DockStyle.Left, MinSize = S(120), MinExtra = S(160) };
        var left = new Panel { Dock = DockStyle.Left, Width = S(220), BackColor = Color.FromArgb(245, 247, 250) };
        Place(left, new Label { Text = "Panel" }, 12, 16, 180, 22);
        Place(left, new Label { Text = "向右拖 Splitter。" }, 12, 40, 180, 22);
        Place(left, new Label { Text = "左侧面板会跟着变宽。" }, 12, 64, 180, 22);
        var echo = new TextBox { Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical };
        echo.Text = "右侧面板停靠 Fill。窗口拉大时，多出来的宽度在这一侧。";
        Place(right, echo, 16, 16, 360, 100);
        Place(right, new Label { Text = "PictureBox" }, 16, 128, 90, 22);
        var modes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        modes.Items.AddRange("Zoom", "Normal", "StretchImage", "CenterImage", "AutoSize");
        modes.SelectedIndex = 0;
        Place(right, modes, 110, 124, 160, 24);
        var picture = new PictureBox { Image = _swatch, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        Place(right, picture, 16, 160, 160, 120);
        modes.SelectedIndexChanged += (_, _) =>
        {
            picture.SizeMode = modes.SelectedIndex switch
            {
                1 => PictureBoxSizeMode.Normal,
                2 => PictureBoxSizeMode.StretchImage,
                3 => PictureBoxSizeMode.CenterImage,
                4 => PictureBoxSizeMode.AutoSize,
                _ => PictureBoxSizeMode.Zoom,
            };
            SetStatus("PictureBox " + modes.SelectedItem);
        };
        Place(right, new Label { Text = "色块是运行时生成的 BMP。" }, 190, 200, 220, 22);
        // Dock 从最后一个子控件开始排，所以先放 Fill，再放分隔条，最后放左侧面板。
        page.Controls.Add(right);
        page.Controls.Add(splitter);
        page.Controls.Add(left);
        splitter.SplitterMoved += (_, _) => SetStatus("分隔位置 " + splitter.SplitPosition);
        return page;
    }

    private TabPage BuildChromePage()
    {
        var page = new TabPage("菜单");
        Place(page, new Label { Text = "MenuStrip、ToolStripMenuItem：窗口顶部的「文件」「帮助」。" }, 16, 16, 520, 22);
        Place(page, new Label { Text = "StatusStrip、ToolStripStatusLabel：底部这一条状态栏。" }, 16, 44, 520, 22);
        Place(page, new Label { Text = "TabControl、TabPage：当前这些页面。" }, 16, 72, 520, 22);
        Place(page, new Label { Text = "ContextMenuStrip：到「列表」页，在 ListBox 上右键。" }, 16, 100, 520, 22);
        Place(page, new Label { Text = "ToolTip：鼠标停在下面这个按钮上。" }, 16, 128, 520, 22);
        var hover = new Button { Text = "ToolTip" };
        Place(page, hover, 16, 160, 120, 30);
        _tips.SetToolTip(hover, "这是 ToolTip");

        Place(page, new Label { Text = "Timer：到「数值」页，点「跑一遍」。" }, 16, 208, 520, 22);
        Place(page, new Label { Text = "对话框" }, 16, 248, 80, 22);
        var open = new Button { Text = "OpenFileDialog" };
        var save = new Button { Text = "SaveFileDialog" };
        var folder = new Button { Text = "FolderBrowserDialog" };
        var about = new Button { Text = "TaskDialog" };
        Place(page, open, 16, 276, 180, 30);
        Place(page, save, 208, 276, 180, 30);
        Place(page, folder, 400, 276, 190, 30);
        Place(page, about, 602, 276, 140, 30);
        open.Click += (_, _) => BrowseFile();
        save.Click += (_, _) => SaveFile();
        folder.Click += (_, _) => BrowseFolder();
        about.Click += (_, _) => ShowAbout();
        return page;
    }

    private void AddListItem()
    {
        _listSerial++;
        int index = _list.Items.Add("项目 " + _listSerial);
        _list.SelectedIndex = index;
    }

    private void RemoveListItem()
    {
        if (_list.SelectedIndex < 0)
        {
            return;
        }

        _list.Items.RemoveAt(_list.SelectedIndex);
    }

    private void AddDetail(string name, string kind, string note)
    {
        ListViewItem item = _details.Items.Add(name);
        item.SubItems.Add(kind);
        item.SubItems.Add(note);
    }

    private void OnDensity(RadioButton button)
    {
        if (button.Checked)
        {
            SetStatus("密度：" + button.Text);
        }
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开文件",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetStatus("打开 " + dialog.FileName);
            _log.Text = dialog.FileName;
        }
    }

    private void SaveFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "另存为",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            DefaultExt = "txt",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetStatus("保存 " + dialog.FileName);
        }
    }

    private void BrowseFolder()
    {
        var dialog = new FolderBrowserDialog { Description = "选择一个文件夹" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetStatus("文件夹 " + dialog.SelectedPath);
            _log.Text = dialog.SelectedPath;
        }
    }

    private void ShowAbout()
    {
        TaskDialog.ShowDialog(this, new TaskDialogPage
        {
            Caption = "关于",
            Heading = "MiniWinForm 控件示例",
            Text = "窗口和控件都是 Win32 通用控件，这个示例项目可以 Native AOT 发布。",
            Buttons = { TaskDialogButton.Close },
        });
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
    }

    private int S(int px) => px * _scale / 96;

    private void Place(Control parent, Control control, int x, int y, int width, int height)
    {
        control.Left = S(x);
        control.Top = S(y);
        control.Width = S(width);
        control.Height = S(height);
        parent.Controls.Add(control);
    }

    private static Image LoadSwatch()
    {
        const int width = 64;
        const int height = 48;
        int stride = (width * 3 + 3) & ~3;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 3);
                pixels[offset] = (byte)(30 + (x * 3));
                pixels[offset + 1] = (byte)(70 + y);
                pixels[offset + 2] = (byte)(210 - (x * 2));
            }
        }

        string path = Path.Combine(Path.GetTempPath(), "MiniWinForm.ControlGallery.bmp");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(54 + pixels.Length);
            writer.Write(0);
            writer.Write(54);
            writer.Write(40);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1);
            writer.Write((short)24);
            writer.Write(0);
            writer.Write(pixels.Length);
            writer.Write(2835);
            writer.Write(2835);
            writer.Write(0);
            writer.Write(0);
            writer.Write(pixels);
        }

        return Image.FromFile(path);
    }
}
