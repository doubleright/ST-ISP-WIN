using System.Collections;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public sealed class TabControl : Control
{
    private readonly List<TabPage> _pages = [];
    private int _selectedIndex = -1;

    public TabControl()
        : base(tabStop: true, width: 200, height: 100)
    {
        TabPages = new TabPageCollection(this);
    }

    public TabPageCollection TabPages { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            Application.VerifyUiThread();
            SetSelectedIndex(value, updateNative: true);
        }
    }

    public TabPage? SelectedTab
    {
        get => _selectedIndex >= 0 && _selectedIndex < _pages.Count ? _pages[_selectedIndex] : null;
        set
        {
            Application.VerifyUiThread();
            if (value is null)
            {
                SelectedIndex = -1;
                return;
            }

            int index = _pages.IndexOf(value);
            if (index < 0)
            {
                throw new ArgumentException("The tab page does not belong to this tab control.", nameof(value));
            }

            SelectedIndex = index;
        }
    }

    public event EventHandler? SelectedIndexChanged;

    internal override string NativeClassName => "SysTabControl32";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        WINDOW_STYLE.WS_CLIPCHILDREN |
        WINDOW_STYLE.WS_CLIPSIBLINGS;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CONTROLPARENT;

    internal override void CreateHandle()
    {
        base.CreateHandle();
        for (int index = 0; index < _pages.Count; index++)
        {
            InsertNativeTab(index, _pages[index].Text);
        }

        ApplySelection(updateNative: true);
    }

    internal override unsafe void GetClientSize(out int left, out int top, out int width, out int height)
    {
        base.GetClientSize(out left, out top, out width, out height);
        if (!IsHandleCreated)
        {
            top += 24;
            height = Math.Max(0, height - 24);
            return;
        }

        RECT rect = new()
        {
            left = 0,
            top = 0,
            right = width,
            bottom = height,
        };
        _ = NativeSend.Send(NativeHandle, NativeConstants.TCM_ADJUSTRECT, 0, ref rect);
        left = rect.left;
        top = rect.top;
        width = Math.Max(0, rect.right - rect.left);
        height = Math.Max(0, rect.bottom - rect.top);
    }

    internal override LRESULT OnNotify(uint code, LPARAM lParam)
    {
        _ = lParam;
        if (unchecked((int)code) != NativeConstants.TCN_SELCHANGE)
        {
            return default;
        }

        int index = (int)NativeSend.Send(NativeHandle, NativeConstants.TCM_GETCURSEL, 0, 0);
        SetSelectedIndex(index, updateNative: false);
        return default;
    }

    internal void UpdatePageText(TabPage page)
    {
        int index = _pages.IndexOf(page);
        if (index >= 0 && IsHandleCreated)
        {
            SetNativeText(index, page.Text);
            InvalidateItem(index);
        }
    }

    internal override void OnControlAdded(Control control)
    {
        if (control is not TabPage page || _pages.Contains(page))
        {
            return;
        }

        _pages.Add(page);
        if (page.Dock == DockStyle.None)
        {
            page.Dock = DockStyle.Fill;
        }

        bool select = _selectedIndex < 0;
        if (select)
        {
            _selectedIndex = 0;
        }

        page.Visible = _pages.Count - 1 == _selectedIndex;
        if (IsHandleCreated)
        {
            InsertNativeTab(_pages.Count - 1, page.Text);
            ApplySelection(updateNative: true);
        }

        if (select)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal override void OnControlRemoved(Control control)
    {
        if (control is not TabPage page)
        {
            return;
        }

        int index = _pages.IndexOf(page);
        if (index < 0)
        {
            return;
        }

        _pages.RemoveAt(index);
        if (IsHandleCreated)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.TCM_DELETEITEM, (nuint)index, 0);
        }

        if (_pages.Count == 0)
        {
            SetSelectedIndex(-1, updateNative: false);
            return;
        }

        if (_selectedIndex >= _pages.Count)
        {
            SetSelectedIndex(_pages.Count - 1, updateNative: true);
            return;
        }

        if (index == _selectedIndex)
        {
            ApplySelection(updateNative: true);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (index < _selectedIndex)
        {
            _selectedIndex--;
        }

        ApplySelection(updateNative: true);
    }

    private void SetSelectedIndex(int value, bool updateNative)
    {
        if (value < -1 || value >= _pages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (_selectedIndex == value)
        {
            ApplySelection(updateNative);
            return;
        }

        _selectedIndex = value;
        ApplySelection(updateNative);
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private unsafe void ApplySelection(bool updateNative)
    {
        for (int index = 0; index < _pages.Count; index++)
        {
            _pages[index].Visible = index == _selectedIndex;
        }

        if (updateNative && IsHandleCreated && _selectedIndex >= 0)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.TCM_SETCURSEL, (nuint)_selectedIndex, 0);
            InvalidateItem(_selectedIndex);
        }

        PerformLayout();
    }

    private void InvalidateItem(int index)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        RECT bounds = default;
        if (NativeSend.Send(NativeHandle, NativeConstants.TCM_GETITEMRECT, (nuint)index, ref bounds) != 0)
        {
            _ = PInvoke.InvalidateRect(NativeHandle, bounds, bErase: false);
        }
    }

    private unsafe void InsertNativeTab(int index, string text)
    {
        SetNativeItem(NativeConstants.TCM_INSERTITEMW, index, text);
    }

    private unsafe void SetNativeText(int index, string text)
    {
        SetNativeItem(NativeConstants.TCM_SETITEMW, index, text);
    }

    private unsafe void SetNativeItem(uint message, int index, string text)
    {
        text ??= string.Empty;
        fixed (char* textPointer = text)
        {
            char* pointer = textPointer;
            char empty = '\0';
            if (text.Length == 0)
            {
                pointer = &empty;
            }

            TCITEMW item = new()
            {
                mask = TCITEMHEADERA_MASK.TCIF_TEXT | TCITEMHEADERA_MASK.TCIF_IMAGE,
                pszText = pointer,
                cchTextMax = text.Length + 1,
                iImage = -1,
            };
            if (NativeSend.Send(NativeHandle, message, (nuint)index, ref item) < 0)
            {
                throw new InvalidOperationException($"Tab item message 0x{message:X} failed. LastError={Marshal.GetLastPInvokeError()}.");
            }
        }
    }

    public sealed class TabPageCollection : IEnumerable<TabPage>
    {
        private readonly TabControl _owner;

        internal TabPageCollection(TabControl owner)
        {
            _owner = owner;
        }

        public int Count => _owner._pages.Count;

        public TabPage this[int index] => _owner._pages[index];

        public TabPage Add(string? text)
        {
            TabPage page = new() { Text = text ?? string.Empty };
            Add(page);
            return page;
        }

        public void Add(TabPage page)
        {
            ArgumentNullException.ThrowIfNull(page);
            _owner.Controls.Add(page);
        }

        public void Remove(TabPage page)
        {
            ArgumentNullException.ThrowIfNull(page);
            _owner.Controls.Remove(page);
        }

        public void Clear()
        {
            while (_owner._pages.Count > 0)
            {
                Remove(_owner._pages[^1]);
            }
        }

        public IEnumerator<TabPage> GetEnumerator() => _owner._pages.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public sealed class TabPage : Control
{
    public TabPage()
        : base(tabStop: false, width: 200, height: 100)
    {
    }

    public TabPage(string? text)
        : this()
    {
        Text = text ?? string.Empty;
    }

    // TabPage 是容器，只作为页签文字的来源，不应该把自己的 Text 画在页面上。
    internal override bool MirrorsTextToHandle => false;

    internal override string NativeClassName => "STATIC";

    internal override WINDOW_STYLE NativeStyle =>
        WINDOW_STYLE.WS_CHILD |
        WINDOW_STYLE.WS_CLIPSIBLINGS |
        WINDOW_STYLE.WS_CLIPCHILDREN;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CONTROLPARENT;

    internal override void OnTextChanged()
    {
        if (Parent is TabControl tabControl)
        {
            tabControl.UpdatePageText(this);
        }
    }
}
