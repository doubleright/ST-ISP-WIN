using System.Runtime.InteropServices;
using System.Text;
using MiniWinForm;

namespace ControlGallery;

/// <summary>
/// 控件自检：逐页切换并截图，然后对整棵控件树做「托管属性 ↔ 原生窗口」一致性核对。
/// 用法：ControlGallery.exe --shots &lt;输出目录&gt;
/// </summary>
internal sealed unsafe partial class SelfCheck
{
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const int DIB_RGB_COLORS = 0;
    private const int BI_RGB = 0;

    // 原生控件消息：核对「数据是否真的进了原生控件」
    private const uint TCM_GETITEMCOUNT = 0x1304;
    private const uint CB_GETCOUNT = 0x0146;
    private const uint LB_GETCOUNT = 0x018B;
    private const uint LVM_GETITEMCOUNT = 0x1004;
    private const uint TVM_GETNEXTITEM = 0x110A;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint TBM_GETTHUMBRECT = 0x0419;
    private const uint TBM_SETPOS = 0x0405;
    private const uint WM_HSCROLL = 0x0114;
    private const uint CB_SHOWDROPDOWN = 0x014F;
    private const uint CB_GETITEMHEIGHT = 0x0154;
    private const uint CB_GETCOMBOBOXINFO = 0x0164;
    private const uint CB_GETDROPPEDSTATE = 0x0157;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private readonly Form _form;
    private readonly TabControl _tabs;
    private readonly string _directory;
    private readonly MiniWinForm.Timer _timer = new() { Interval = 550 };
    private readonly StringBuilder _report = new();
    private readonly List<string> _problems = [];
    private int _next;
    private int _pending = -1;
    private int _checked;
    private bool _audited;

    private SelfCheck(Form form, TabControl tabs, string directory)
    {
        _form = form;
        _tabs = tabs;
        _directory = directory;
    }

    internal static void Attach(Form form, TabControl tabs, string directory)
    {
        var check = new SelfCheck(form, tabs, directory);
        form.Shown += (_, _) => check.Start();
    }

    /// <summary>交互抽查：数值控件的方向、步进靠这里兜底，结构体检查不出来。</summary>
    internal static void ProbeInteraction(string outputPath)
    {
        var lines = new List<string>();
        var form = new Form
        {
            Text = "数值控件交互抽查",
            StartPosition = FormStartPosition.CenterScreen,
            Width = 500,
            Height = 340,
        };

        var number = new NumericUpDown
        {
            Left = 24, Top = 24, Width = 120,
            Minimum = 0, Maximum = 10, Increment = 1, Value = 5,
        };
        var horizontalTrack = new TrackBar
        {
            Left = 24, Top = 70, Width = 300, Height = 45,
            Minimum = 0, Maximum = 10, LargeChange = 5, Value = 5,
        };
        var verticalTrack = new TrackBar
        {
            Orientation = Orientation.Vertical,
            Left = 360, Top = 70, Width = 45, Height = 180,
            Minimum = 0, Maximum = 10, LargeChange = 5, Value = 5,
        };
        var horizontalBar = new HScrollBar
        {
            Left = 24, Top = 140, Width = 300, Height = 18,
            Minimum = 0, Maximum = 100, LargeChange = 10, Value = 50,
        };
        var verticalBar = new VScrollBar
        {
            Left = 424, Top = 70, Width = 18, Height = 150,
            Minimum = 0, Maximum = 100, LargeChange = 10, Value = 50,
        };

        foreach (Control control in new Control[] { number, horizontalTrack, verticalTrack, horizontalBar, verticalBar })
        {
            form.Controls.Add(control);
        }

        var timer = new MiniWinForm.Timer { Interval = 450 };
        int step = 0;
        bool busy = false;
        timer.Tick += (_, _) =>
        {
            // 触碰句柄会驱动布局，可能在处理过程中再进一次 Tick，加个重入保护。
            if (busy)
            {
                return;
            }

            busy = true;
            try
            {
                switch (step++)
                {
                    case 0:
                        // 先把句柄建好，避免中途创建窗口引发重入
                        foreach (Control control in form.Controls)
                        {
                            _ = control.Handle;
                        }

                        lines.Add($"起始：NumericUpDown {number.Value}，水平滑块 {horizontalTrack.Value}，垂直滑块 {verticalTrack.Value}");
                        break;
                    case 1:
                        ClickArrow(number, up: true);
                        Record(lines, "点上箭头一次", (int)number.Value, 6);
                        break;
                    case 2:
                        ClickArrow(number, up: false);
                        ClickArrow(number, up: false);
                        Record(lines, "点下箭头两次", (int)number.Value, 4);
                        break;
                    case 3:
                        // 垂直滑块：值往上变大，最大值 9 的滑块应该在上面
                        lines.Add($"垂直滑块样式位 0x{GetWindowLongPtr(verticalTrack.Handle, -16):X}");
                        lines.Add(DescribeThumb("垂直滑块", verticalTrack, 9, 1, vertical: true));
                        break;
                    case 4:
                        // 水平滑块：值往右变大
                        lines.Add(DescribeThumb("水平滑块", horizontalTrack, 9, 1, vertical: false));
                        break;
                    case 5:
                        // 反向：把原生位置拖到 1（靠上），通知父窗口，Value 应换算成 9
                        _ = Send(verticalTrack.Handle, TBM_SETPOS, 1, 1);
                        _ = Send(form.Handle, WM_HSCROLL, 5 /* TB_THUMBTRACK */, verticalTrack.Handle);
                        Record(lines, "垂直滑块原生位置 1 回传", verticalTrack.Value, 9);
                        break;
                    default:
                        timer.Enabled = false;
                        File.WriteAllLines(outputPath, lines, Encoding.UTF8);
                        form.Close();
                        break;
                }
            }
            finally
            {
                busy = false;
            }
        };

        form.Shown += (_, _) => timer.Enabled = true;
        Application.Run(form);
    }

    /// <summary>把值设成大/小两端，比较滑块中心位置，判断方向对不对。</summary>
    private static string DescribeThumb(string name, TrackBar bar, int value, int smallValue, bool vertical)
    {
        bar.Value = value;
        RECT high = ThumbRect(bar.Handle);
        bar.Value = smallValue;
        RECT low = ThumbRect(bar.Handle);

        int highCenter = vertical ? (high.Top + high.Bottom) / 2 : (high.Left + high.Right) / 2;
        int lowCenter = vertical ? (low.Top + low.Bottom) / 2 : (low.Left + low.Right) / 2;

        // 竖的：值大在上（Y 更小）；横的：值大在右（X 更大）
        bool ok = vertical ? highCenter < lowCenter : highCenter > lowCenter;
        string highSide = vertical ? "上" : "右";
        return $"{name}：值 {value} 的滑块在 {highCenter}，值 {smallValue} 在 {lowCenter}（值大的应在{highSide}面）{(ok ? "OK" : "错误")}";
    }

    private static RECT ThumbRect(IntPtr handle)
    {
        RECT rect = default;
        unsafe
        {
            _ = SendMessagePointer(handle, TBM_GETTHUMBRECT, 0, &rect);
        }

        return rect;
    }

    private static void Record(List<string> lines, string what, int actual, int expected)
        => lines.Add($"{what} → {actual}（期望 {expected}）{(actual == expected ? "OK" : "错误")}");

    /// <summary>在控件客户区按比例位置点一下（真实鼠标消息，走控件自己的处理逻辑）。</summary>
    private static void ClickAt(IntPtr handle, double fractionX, double fractionY)
    {
        GetClientRect(handle, out RECT client);
        int x = Math.Clamp((int)(client.Right * fractionX), 1, Math.Max(1, client.Right - 2));
        int y = Math.Clamp((int)(client.Bottom * fractionY), 1, Math.Max(1, client.Bottom - 2));
        nint point = (y << 16) | x;
        _ = Send(handle, WM_LBUTTONDOWN, 1 /* MK_LBUTTON */, point);
        _ = Send(handle, WM_LBUTTONUP, 0, point);
    }

    /// <summary>下拉选择的交互抽查：用真实鼠标事件点下拉项，看事件是否立刻生效。</summary>
    internal static void ProbeComboBox(string outputPath)
    {
        var lines = new List<string>();
        var form = new Form
        {
            Text = "ComboBox 交互抽查",
            StartPosition = FormStartPosition.CenterScreen,
            Width = 460,
            Height = 300,
        };

        var combo = new ComboBox
        {
            Left = 24, Top = 24, Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        combo.Items.AddRange("北京", "上海", "广州", "深圳");
        combo.SelectedIndex = 0;

        int events = 0;
        combo.SelectedIndexChanged += (_, _) => events++;

        form.Controls.Add(combo);

        var timer = new MiniWinForm.Timer { Interval = 450 };
        int step = 0;
        bool busy = false;
        timer.Tick += (_, _) =>
        {
            if (busy)
            {
                return;
            }

            busy = true;
            try
            {
                switch (step++)
                {
                    case 0:
                        _ = combo.Handle;
                        lines.Add($"初始：SelectedIndex {combo.SelectedIndex}（期望 0），事件 {events} 次（期望 0）");
                        break;
                    case 1:
                        // 点下拉箭头展开（等价用户第一次点击；点文本框中心不一定能展开）
                        OpenDropDown(combo);
                        lines.Add("真实点击下拉箭头：已点");
                        break;
                    case 2:
                        // 点下拉第 3 项：这一步以前要等失焦才生效。
                        Record(lines, "展开状态（CB_GETDROPPEDSTATE）", (int)Send(combo.Handle, CB_GETDROPPEDSTATE, 0, 0), 1);
                        lines.Add($"真实鼠标点下拉第 3 项：{(RealClickDropDownItem(combo, 2) ? "已点击" : "找不到下拉窗口")}");
                        break;
                    case 3:
                        Record(lines, "下拉选定后（同一拍）事件次数", events, 1);
                        Record(lines, "下拉选定后（同一拍）SelectedIndex", combo.SelectedIndex, 2);
                        lines.Add($"下拉选定后（同一拍）SelectedItem → {combo.SelectedItem}（期望 广州）{(Equals(combo.SelectedItem, "广州") ? "OK" : "错误")}");
                        break;
                    case 4:
                        OpenDropDown(combo);
                        lines.Add("再次点击下拉箭头：已点");
                        break;
                    case 5:
                        Record(lines, "第二次展开状态（CB_GETDROPPEDSTATE）", (int)Send(combo.Handle, CB_GETDROPPEDSTATE, 0, 0), 1);
                        lines.Add($"真实鼠标点下拉第 2 项：{(RealClickDropDownItem(combo, 1) ? "已点击" : "找不到下拉窗口")}");
                        break;
                    case 6:
                        Record(lines, "第二次选择的累计事件次数（不该重复发）", events, 2);
                        Record(lines, "第二次选择的 SelectedIndex", combo.SelectedIndex, 1);
                        break;
                    default:
                        timer.Enabled = false;
                        File.WriteAllLines(outputPath, lines, Encoding.UTF8);
                        form.Close();
                        break;
                }
            }
            finally
            {
                busy = false;
            }
        };

        form.Shown += (_, _) => timer.Enabled = true;
        Application.Run(form);
    }

    private static void RealClick(int x, int y)
    {
        _ = SetCursorPos(x, y);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
    }

    /// <summary>点组合框右侧的下拉箭头展开列表（点文本框中心不保证能展开）。</summary>
    private static void OpenDropDown(ComboBox combo)
    {
        GetWindowRect(combo.Handle, out RECT box);
        RealClick(box.Right - 12, box.Top + ((box.Bottom - box.Top) / 2));
    }

    /// <summary>用真实鼠标事件点下拉列表第 index 项（合成 WM_LBUTTON* 会被排队，测不出真实时序）。</summary>
    private static bool RealClickDropDownItem(ComboBox combo, int index)
    {
        COMBOBOXINFO info = default;
        info.cbSize = sizeof(COMBOBOXINFO);
        _ = SendMessagePointer(combo.Handle, CB_GETCOMBOBOXINFO, 0, &info);
        if (info.hwndList == IntPtr.Zero)
        {
            return false;
        }

        int itemHeight = (int)Send(combo.Handle, CB_GETITEMHEIGHT, 0, 0);
        if (itemHeight <= 0)
        {
            itemHeight = 16;
        }

        GetWindowRect(info.hwndList, out RECT list);
        int x = list.Left + 20;
        int y = list.Top + (index * itemHeight) + (itemHeight / 2);
        RealClick(x, y);
        return true;
    }

    private static void ClickArrow(NumericUpDown number, bool up)
    {
        IntPtr parent = number.Parent?.Handle ?? IntPtr.Zero;
        IntPtr spin = FindWindowEx(parent, IntPtr.Zero, "msctls_updown32", null);
        if (spin == IntPtr.Zero)
        {
            return;
        }

        GetWindowRect(spin, out RECT rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        int x = width / 2;
        int y = up ? Math.Max(1, height / 4) : Math.Max(1, (height * 3) / 4);
        nint point = (y << 16) | x;
        _ = Send(spin, WM_LBUTTONDOWN, 1 /* MK_LBUTTON */, point);
        _ = Send(spin, WM_LBUTTONUP, 0, point);
    }

    private void Start()
    {
        Directory.CreateDirectory(_directory);
        _form.TopMost = true;
        SetForegroundWindow(_form.Handle);
        WriteHeader();

        _timer.Tick += (_, _) => Step();
        _timer.Enabled = true;
    }

    private void Step()
    {
        // 切页后等一拍再截，保证重绘完成。
        if (_pending >= 0)
        {
            Capture(_pending);
            _pending = -1;
        }

        if (_next < _tabs.TabPages.Count)
        {
            _tabs.SelectedIndex = _next;
            _pending = _next;
            _next++;
            return;
        }

        // 所有页都露过一次（句柄、布局都跑过），再做体检。
        if (!_audited)
        {
            _audited = true;
            _report.AppendLine();
            _report.AppendLine("== 控件树体检 ==");
            Audit(_form, 0);
            Finish();
            return;
        }
    }

    private void Finish()
    {
        _timer.Enabled = false;

        _report.AppendLine();
        _report.AppendLine("== 汇总 ==");
        _report.AppendLine($"核对控件数：{_checked}");
        if (_problems.Count == 0)
        {
            _report.AppendLine("问题：0（托管属性与原生窗口一致）");
        }
        else
        {
            _report.AppendLine($"问题：{_problems.Count}");
            foreach (string problem in _problems)
            {
                _report.AppendLine("  - " + problem);
            }
        }

        File.WriteAllText(Path.Combine(_directory, "report.txt"), _report.ToString(), Encoding.UTF8);
        _form.Close();
    }

    private void WriteHeader()
    {
        RECT window = GetFrameBounds();
        int screenWidth = GetSystemMetrics(0);
        int screenHeight = GetSystemMetrics(1);

        _report.AppendLine("MiniWinForm 控件自检报告");
        _report.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _report.AppendLine($"系统 DPI：{Program.GetDpiForSystem()}  屏幕：{screenWidth}x{screenHeight}");
        _report.AppendLine($"窗口：{window.Right - window.Left}x{window.Bottom - window.Top} @ ({window.Left},{window.Top})");
        if (window.Right > screenWidth || window.Bottom > screenHeight || window.Left < 0 || window.Top < 0)
        {
            _problems.Add($"窗口 {window.Right - window.Left}x{window.Bottom - window.Top} @ ({window.Left},{window.Top}) 超出屏幕 {screenWidth}x{screenHeight}，截图可能被裁");
        }

        _report.AppendLine($"页数：{_tabs.TabPages.Count}");
        _report.AppendLine();
    }

    private void Capture(int index)
    {
        TabPage page = _tabs.TabPages[index];
        string label = page.Text.Length == 0 ? "page" : page.Text;
        string fileName = $"{index + 1:00}-{Sanitize(label)}.bmp";

        if (CaptureWindow(Path.Combine(_directory, fileName)))
        {
            _report.AppendLine($"页 {index + 1}「{label}」→ {fileName}");
        }
        else
        {
            _report.AppendLine($"页 {index + 1}「{label}」→ 截图失败");
            _problems.Add($"{label}：截图失败");
        }
    }

    /// <summary>递归核对一个控件子树。</summary>
    private void Audit(Control control, int depth)
    {
        _checked++;
        string indent = new(' ', depth * 2);
        string type = control.GetType().Name;
        string managedText = control.Text ?? string.Empty;
        string text = managedText.Length > 0 ? Truncate(managedText) : string.Empty;
        IntPtr handle = control.Handle;

        if (handle == IntPtr.Zero)
        {
            // 菜单在这个库里以 HMENU 挂到窗口上，本身没有子窗口。
            if (control is MenuStrip)
            {
                _report.AppendLine($"{indent}{type}：以 HMENU 挂载到窗口，无独立子窗口（设计如此）");
                return;
            }

            _report.AppendLine($"{indent}{type}「{text}」：无窗口句柄");
            _problems.Add($"{type}「{text}」：句柄未创建");
            return;
        }

        string className = ClassNameOf(handle);
        GetWindowRect(handle, out RECT native);
        int width = native.Right - native.Left;
        int height = native.Bottom - native.Top;

        // 托管 Left/Top 是相对父客户区，原生矩形是屏幕坐标，先换算。
        RECT local = native;
        Control? parent = control.Parent;
        if (parent is not null && parent.Handle != IntPtr.Zero)
        {
            MapWindowPoints(IntPtr.Zero, parent.Handle, ref local, 2);
        }

        var notes = new List<string>();

        void Problem(string message)
        {
            notes.Add(message);
            _problems.Add($"{type}「{text}」：{message}");
        }

        if (control.Width <= 0 || control.Height <= 0)
        {
            Problem("尺寸为 0");
        }

        if (Math.Abs(width - control.Width) > 1 || Math.Abs(height - control.Height) > 1)
        {
            Problem($"尺寸不一致 托管 {control.Width}x{control.Height} → 原生 {width}x{height}");
        }

        if (Math.Abs(local.Left - control.Left) > 1 || Math.Abs(local.Top - control.Top) > 1)
        {
            if (parent is null)
            {
                // 顶层窗口：StartPosition 由系统摆放，托管 Left/Top 可能不同步，只记录不报错。
                notes.Add($"托管 Left/Top ({control.Left},{control.Top}) 与屏幕位置 ({local.Left},{local.Top}) 不同步");
            }
            else
            {
                Problem($"位置不一致 托管 ({control.Left},{control.Top}) → 原生 ({local.Left},{local.Top})");
            }
        }

        // 原生可见性受祖先影响，期望值要顺着父链算。
        bool chainVisible = _form.Visible;
        for (Control? up = parent; up is not null; up = up.Parent)
        {
            chainVisible &= up.Visible;
        }

        bool expectedVisible = control.Visible && chainVisible;
        bool nativeVisible = IsWindowVisible(handle);
        if (nativeVisible != expectedVisible)
        {
            Problem($"可见性不一致 托管 {expectedVisible} → 原生 {nativeVisible}");
        }

        string nativeRaw = WindowTextOf(handle);
        if (control is LinkLabel)
        {
            // SysLink 会把 <a> 标签改写成显示文本（并补一个尾随空格），不能逐字比。
            notes.Add(nativeRaw.Length == 0 ? "SysLink 文本为空" : "SysLink 改写后的文本已写入窗口，跳过逐字比对");
        }
        else if (managedText.Length > 0 && Normalize(nativeRaw).Length == 0)
        {
            notes.Add("文本未镜像到窗口（TabPage 属正常）");
        }
        else if (managedText.Length > 0 && Normalize(nativeRaw) != Normalize(StripMarkup(managedText)))
        {
            Problem($"文本不一致 托管「{Truncate(managedText)}」→ 原生「{Truncate(nativeRaw)}」");
        }

        if (parent is not null && GetParent(handle) != parent.Handle)
        {
            Problem($"父窗口不是 {parent.GetType().Name}");
        }

        if (parent is not null && width > 0 && height > 0)
        {
            GetClientRect(parent.Handle, out RECT client);
            if (local.Left < -1 || local.Top < -1 || local.Right > client.Right + 1 || local.Bottom > client.Bottom + 1)
            {
                notes.Add($"超出父客户区 {client.Right}x{client.Bottom}");
            }
        }

        string? itemNote = CheckItemCount(control);
        if (itemNote is not null)
        {
            notes.Add(itemNote);
        }

        _report.AppendLine($"{indent}{type}「{text}」{className} {width}x{height} @ ({local.Left},{local.Top}) → {(notes.Count == 0 ? "ok" : string.Join("；", notes))}");

        foreach (Control child in control.Controls)
        {
            Audit(child, depth + 1);
        }
    }

    /// <summary>核对集合里放进去的条目是否真的进了原生控件。</summary>
    private string? CheckItemCount(Control control)
    {
        IntPtr handle = control.Handle;
        switch (control)
        {
            case TabControl tabs:
                return Compare("页签", Send(handle, TCM_GETITEMCOUNT, 0, 0), tabs.TabPages.Count);
            case ComboBox combo:
                return Compare("条目", Send(handle, CB_GETCOUNT, 0, 0), combo.Items.Count);
            case ListBox list:
                return Compare("条目", Send(handle, LB_GETCOUNT, 0, 0), list.Items.Count);
            case ListView view:
                return Compare("行", Send(handle, LVM_GETITEMCOUNT, 0, 0), view.Items.Count);
            case TreeView tree:
                return Compare("可见节点", CountNativeVisibleItems(handle), CountVisibleNodes(tree.Nodes));
            case HScrollBar:
                return CheckScrollBarStyle(handle, vertical: false);
            case VScrollBar:
                return CheckScrollBarStyle(handle, vertical: true);
            case NumericUpDown number:
                return CheckSpinButton(number);
            default:
                return null;
        }
    }

    private static int CountVisibleNodes(TreeNodeCollection nodes)
    {
        int total = 0;
        foreach (TreeNode node in nodes)
        {
            total++;
            if (node.IsExpanded)
            {
                total += CountVisibleNodes(node.Nodes);
            }
        }

        return total;
    }

    /// <summary>按 TVGN_NEXTVISIBLE 逐项走，折叠节点的子项不会被数到。</summary>
    private static int CountNativeVisibleItems(IntPtr handle)
    {
        int count = 0;
        nint item = Send(handle, TVM_GETNEXTITEM, 0 /* TVGN_ROOT */, 0);
        while (item != 0)
        {
            count++;
            item = Send(handle, TVM_GETNEXTITEM, 6 /* TVGN_NEXTVISIBLE */, item);
        }

        return count;
    }

    /// <summary>SBS_VERT 决定滚动条画成竖的还是横的，画错就只剩半截滑块。</summary>
    private string? CheckScrollBarStyle(IntPtr handle, bool vertical)
    {
        nint style = GetWindowLongPtr(handle, -16 /* GWL_STYLE */);
        bool isVertical = (style & 0x0001 /* SBS_VERT */) != 0;
        if (isVertical == vertical)
        {
            return null;
        }

        _problems.Add($"{(vertical ? "VScrollBar" : "HScrollBar")}：原生样式里 SBS_VERT {(isVertical ? "多余" : "缺失")}");
        return $"滚动条方向样式错误（SBS_VERT {(isVertical ? "多余" : "缺失")}）";
    }

    /// <summary>NumericUpDown 的上下箭头是独立的 msctls_updown32 窗口，挂在父窗口上，必须单独查。</summary>
    private string? CheckSpinButton(NumericUpDown number)
    {
        IntPtr parent = number.Parent?.Handle ?? IntPtr.Zero;
        IntPtr spin = FindWindowEx(parent, IntPtr.Zero, "msctls_updown32", null);
        if (spin == IntPtr.Zero)
        {
            _problems.Add("NumericUpDown：上下箭头窗口（msctls_updown32）未创建");
            return "上下箭头未创建";
        }

        // 用样式位而不是 IsWindowVisible：此时控件所在页可能是隐藏的。
        nint style = GetWindowLongPtr(spin, -16 /* GWL_STYLE */);
        bool spinVisible = (style & 0x10000000 /* WS_VISIBLE */) != 0;
        if (spinVisible != number.Visible)
        {
            _problems.Add($"NumericUpDown：上下箭头可见性与控件不一致 控件 {number.Visible} / 箭头 {spinVisible}");
            return "上下箭头可见性与控件不一致";
        }

        // 箭头是编辑框的兄弟窗口，被压在下层就完全看不见。
        IntPtr above = GetWindow(spin, 3 /* GW_HWNDPREV */);
        if (above == number.Handle)
        {
            _problems.Add("NumericUpDown：上下箭头被输入框盖住（z 序在输入框之下）");
            return "上下箭头被输入框盖住";
        }

        GetWindowRect(spin, out RECT spinRect);
        GetWindowRect(number.Handle, out RECT boxRect);
        if (spinRect.Right <= spinRect.Left || spinRect.Bottom <= spinRect.Top)
        {
            _problems.Add("NumericUpDown：上下箭头窗口尺寸为 0");
            return "上下箭头尺寸为 0";
        }

        bool overlaps = spinRect.Left >= boxRect.Left - 1 && spinRect.Right <= boxRect.Right + 1 &&
            spinRect.Top >= boxRect.Top - 1 && spinRect.Bottom <= boxRect.Bottom + 1;
        if (!overlaps)
        {
            _problems.Add($"NumericUpDown：上下箭头位置跑偏 框 ({boxRect.Left},{boxRect.Top},{boxRect.Right},{boxRect.Bottom}) / 箭头 ({spinRect.Left},{spinRect.Top},{spinRect.Right},{spinRect.Bottom})");
            return "上下箭头位置跑偏";
        }

        return null;
    }

    private string? Compare(string what, nint native, int managed)
    {
        if (native == managed)
        {
            return null;
        }

        _problems.Add($"{what}数不一致 托管 {managed} → 原生 {native}");
        return $"{what}数不一致 托管 {managed} → 原生 {native}";
    }

    private bool CaptureWindow(string path)
    {
        RECT rect = GetFrameBounds();
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return false;
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return false;
            }

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, rect.Left, rect.Top, SRCCOPY | CAPTUREBLT))
            {
                return false;
            }

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = height,
                biPlanes = 1,
                biBitCount = 24,
                biCompression = BI_RGB,
            };

            int stride = (width * 3 + 3) & ~3;
            byte[] pixels = new byte[stride * height];
            if (GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS) == 0)
            {
                return false;
            }

            WriteBmp(path, width, height, pixels);
            return true;
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                SelectObject(memoryDc, previous);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private RECT GetFrameBounds()
    {
        // 窗口矩形含 8px 看不见的拖拽边，直接按屏幕截图会把桌面内容带进来。
        if (DwmGetWindowAttribute(_form.Handle, 9, out RECT frame, sizeof(RECT)) == 0 && frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            return frame;
        }

        GetWindowRect(_form.Handle, out RECT fallback);
        return fallback;
    }

    private static void WriteBmp(string path, int width, int height, byte[] pixels)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream);
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

    private static nint Send(IntPtr handle, uint message, nint wParam, nint lParam)
        => SendMessage(handle, message, wParam, lParam);

    private static string ClassNameOf(IntPtr handle)
    {
        char* buffer = stackalloc char[128];
        int length = GetClassName(handle, buffer, 128);
        return length <= 0 ? "?" : new string(buffer, 0, length);
    }

    private static string WindowTextOf(IntPtr handle)
    {
        char* buffer = stackalloc char[512];
        int length = GetWindowText(handle, buffer, 512);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    /// <summary>SysLink 会把 &lt;a&gt; 标签消化掉，比较时按同样规则处理托管文本。</summary>
    private static string StripMarkup(string value)
    {
        if (!value.Contains('<'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        bool inside = false;
        foreach (char c in value)
        {
            if (c == '<')
            {
                inside = true;
            }
            else if (c == '>')
            {
                inside = false;
            }
            else if (!inside)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>比较文本只在意内容，不纠结首尾空白与连续空白。</summary>
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool pendingSpace = false;
        foreach (char c in value.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string Truncate(string value)
    {
        string flat = value.Replace("\r", " ").Replace("\n", " ");
        return flat.Length <= 30 ? flat : flat[..30] + "…";
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
        }

        return builder.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int cbSize;
        public RECT rcItem;
        public RECT rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndItem;
        public IntPtr hwndList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetParent(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(IntPtr hWnd, int index);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindow(IntPtr hWnd, uint command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial int MapWindowPoints(IntPtr from, IntPtr to, ref RECT points, uint count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, nint extraInfo);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static partial int GetClassName(IntPtr hWnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static partial int GetWindowText(IntPtr hWnd, char* buffer, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(IntPtr hWnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static unsafe partial nint SendMessagePointer(IntPtr hWnd, uint message, nint wParam, void* lParam);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowEx(IntPtr parent, IntPtr child, string className, string? windowName);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateCompatibleDC(IntPtr hDc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr SelectObject(IntPtr hDc, IntPtr obj);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr destDc, int x, int y, int width, int height, IntPtr srcDc, int srcX, int srcY, uint rop);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int GetDIBits(IntPtr hDc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hDc);
}
