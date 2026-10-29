using System.Collections;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public enum View
{
    LargeIcon,
    Details,
    SmallIcon,
    List,
}

public sealed class ListView : Control
{
    private readonly List<ColumnHeader> _columns = [];
    private readonly List<ListViewItem> _items = [];
    private View _view = View.Details;
    private bool _multiSelect = true;
    private bool _fullRowSelect;
    private bool _gridLines;
    private bool _updating;

    public ListView()
        : base(tabStop: true, width: 121, height: 97)
    {
        Columns = new ColumnHeaderCollection(this);
        Items = new ListViewItemCollection(this);
    }

    public ColumnHeaderCollection Columns { get; }

    public ListViewItemCollection Items { get; }

    public View View
    {
        get => _view;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_view == value)
            {
                return;
            }

            _view = value;
            RecreateHandle();
        }
    }

    public bool MultiSelect
    {
        get => _multiSelect;
        set
        {
            Application.VerifyUiThread();
            if (_multiSelect == value)
            {
                return;
            }

            _multiSelect = value;
            RecreateHandle();
        }
    }

    public bool FullRowSelect
    {
        get => _fullRowSelect;
        set
        {
            Application.VerifyUiThread();
            if (_fullRowSelect == value)
            {
                return;
            }

            _fullRowSelect = value;
            ApplyExtendedStyle();
        }
    }

    public bool GridLines
    {
        get => _gridLines;
        set
        {
            Application.VerifyUiThread();
            if (_gridLines == value)
            {
                return;
            }

            _gridLines = value;
            ApplyExtendedStyle();
        }
    }

    public IReadOnlyList<ListViewItem> SelectedItems => _items.Where(item => item.Selected).ToArray();

    public event EventHandler? SelectedIndexChanged;

    internal int ColumnCount => _columns.Count;

    internal int ItemCount => _items.Count;

    internal ColumnHeader GetColumn(int index) => _columns[index];

    internal ListViewItem GetItem(int index) => _items[index];

    internal override string NativeClassName => "SysListView32";

    internal override WINDOW_STYLE NativeStyle
    {
        get
        {
            WINDOW_STYLE style = base.NativeStyle |
                WINDOW_STYLE.WS_VSCROLL |
                (WINDOW_STYLE)NativeConstants.LVS_SHOWSELALWAYS |
                (WINDOW_STYLE)NativeConstants.LVS_NOSORTHEADER;
            if (!_multiSelect)
            {
                style |= (WINDOW_STYLE)NativeConstants.LVS_SINGLESEL;
            }

            style |= _view switch
            {
                View.Details => (WINDOW_STYLE)NativeConstants.LVS_REPORT,
                View.SmallIcon => (WINDOW_STYLE)NativeConstants.LVS_SMALLICON,
                View.List => (WINDOW_STYLE)NativeConstants.LVS_LIST,
                _ => 0,
            };
            return style;
        }
    }

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    internal override void CreateHandle()
    {
        base.CreateHandle();
        ApplyExtendedStyle();
        for (int index = 0; index < _columns.Count; index++)
        {
            InsertNativeColumn(index, _columns[index]);
        }

        for (int index = 0; index < _items.Count; index++)
        {
            InsertNativeItem(index, _items[index]);
        }
    }

    internal override unsafe LRESULT OnNotify(uint code, LPARAM lParam)
    {
        if (unchecked((int)code) != NativeConstants.LVN_ITEMCHANGED)
        {
            return default;
        }

        NMLISTVIEW* change = (NMLISTVIEW*)lParam.Value;
        if (change->iItem < 0 || change->iItem >= _items.Count)
        {
            return default;
        }

        const uint selectedFlag = (uint)LIST_VIEW_ITEM_STATE_FLAGS.LVIS_SELECTED;
        bool wasSelected = (change->uOldState & selectedFlag) != 0;
        bool selected = (change->uNewState & selectedFlag) != 0;
        if (wasSelected == selected)
        {
            return default;
        }

        ListViewItem item = _items[change->iItem];
        item.SetSelected(selected);
        if (!_updating)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        return default;
    }

    internal void AddColumn(ColumnHeader column)
    {
        column.Owner = this;
        _columns.Add(column);
        if (IsHandleCreated)
        {
            InsertNativeColumn(_columns.Count - 1, column);
        }
    }

    internal void AddItem(ListViewItem item)
    {
        item.Owner = this;
        _items.Add(item);
        if (IsHandleCreated)
        {
            InsertNativeItem(_items.Count - 1, item);
        }
    }

    internal void RemoveItem(ListViewItem item)
    {
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        _items.RemoveAt(index);
        item.Owner = null;
        if (IsHandleCreated)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.LVM_DELETEITEM, (nuint)index, 0);
        }
    }

    internal void ClearItems()
    {
        foreach (ListViewItem item in _items)
        {
            item.Owner = null;
            item.SetSelected(false);
        }

        _items.Clear();
        if (IsHandleCreated)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.LVM_DELETEALLITEMS, 0, 0);
        }
    }

    internal void RefreshItem(ListViewItem item)
    {
        int index = _items.IndexOf(item);
        if (index >= 0 && IsHandleCreated)
        {
            WriteItemText(index, item);
        }
    }

    internal void RefreshColumn(ColumnHeader column)
    {
        int index = _columns.IndexOf(column);
        if (index >= 0 && IsHandleCreated)
        {
            WriteColumn(NativeConstants.LVM_SETCOLUMNW, index, column);
        }
    }

    internal void SetItemSelected(ListViewItem item, bool selected)
    {
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        item.SetSelected(selected);
        if (!IsHandleCreated)
        {
            return;
        }

        _updating = true;
        try
        {
            LVITEMW nativeItem = new()
            {
                mask = LIST_VIEW_ITEM_FLAGS.LVIF_STATE,
                stateMask = LIST_VIEW_ITEM_STATE_FLAGS.LVIS_SELECTED,
                state = selected ? LIST_VIEW_ITEM_STATE_FLAGS.LVIS_SELECTED : 0,
            };
            _ = NativeSend.Send(NativeHandle, NativeConstants.LVM_SETITEMSTATE, (nuint)index, ref nativeItem);
        }
        finally
        {
            _updating = false;
        }
    }

    private void ApplyExtendedStyle()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        uint mask = NativeConstants.LVS_EX_DOUBLEBUFFER | NativeConstants.LVS_EX_FULLROWSELECT | NativeConstants.LVS_EX_GRIDLINES;
        uint value = NativeConstants.LVS_EX_DOUBLEBUFFER;
        if (_fullRowSelect)
        {
            value |= NativeConstants.LVS_EX_FULLROWSELECT;
        }

        if (_gridLines)
        {
            value |= NativeConstants.LVS_EX_GRIDLINES;
        }

        _ = NativeSend.Send(NativeHandle, NativeConstants.LVM_SETEXTENDEDLISTVIEWSTYLE, mask, (nint)value);
    }

    private unsafe void InsertNativeColumn(int index, ColumnHeader column)
    {
        WriteColumn(NativeConstants.LVM_INSERTCOLUMNW, index, column);
    }

    private unsafe void WriteColumn(uint message, int index, ColumnHeader column)
    {
        string text = column.Text ?? string.Empty;
        fixed (char* textPointer = text)
        {
            char* pointer = textPointer;
            char empty = '\0';
            if (text.Length == 0)
            {
                pointer = &empty;
            }

            LVCOLUMNW nativeColumn = new()
            {
                mask = LVCOLUMNW_MASK.LVCF_TEXT | LVCOLUMNW_MASK.LVCF_WIDTH | LVCOLUMNW_MASK.LVCF_FMT,
                fmt = LVCOLUMNW_FORMAT.LVCFMT_LEFT,
                cx = column.Width,
                pszText = pointer,
            };
            _ = NativeSend.Send(NativeHandle, message, (nuint)index, ref nativeColumn);
        }
    }

    private void InsertNativeItem(int index, ListViewItem item)
    {
        WriteItemText(index, item);
        if (item.Selected)
        {
            SetItemSelected(item, true);
        }
    }

    private unsafe void WriteItemText(int index, ListViewItem item)
    {
        for (int column = 0; column < item.SubItems.Count; column++)
        {
            string text = item.SubItems[column].Text ?? string.Empty;
            fixed (char* textPointer = text)
            {
                char* pointer = textPointer;
                char empty = '\0';
                if (text.Length == 0)
                {
                    pointer = &empty;
                }

                LVITEMW nativeItem = new()
                {
                    mask = LIST_VIEW_ITEM_FLAGS.LVIF_TEXT,
                    iItem = index,
                    iSubItem = column,
                    pszText = pointer,
                };
                uint message = column == 0 && !ItemExists(index) ? NativeConstants.LVM_INSERTITEMW : NativeConstants.LVM_SETITEMW;
                nint result = NativeSend.Send(NativeHandle, message, 0, ref nativeItem);
                if (message == NativeConstants.LVM_INSERTITEMW && result < 0)
                {
                    throw new InvalidOperationException($"LVM_INSERTITEMW failed. LastError={Marshal.GetLastPInvokeError()}.");
                }
            }
        }
    }

    private bool ItemExists(int index)
    {
        const uint getItemCount = 0x1004;
        return index < (int)NativeSend.Send(NativeHandle, getItemCount, 0, 0);
    }
}

public sealed class ColumnHeader
{
    private string _text = string.Empty;
    private int _width = 60;

    public ColumnHeader()
    {
    }

    public ColumnHeader(string? text, int width)
    {
        _text = text ?? string.Empty;
        _width = width;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            Owner?.RefreshColumn(this);
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            _width = value;
            Owner?.RefreshColumn(this);
        }
    }

    internal ListView? Owner { get; set; }
}

public sealed class ColumnHeaderCollection : IEnumerable<ColumnHeader>
{
    private readonly ListView _owner;

    internal ColumnHeaderCollection(ListView owner)
    {
        _owner = owner;
    }

    public int Count => _owner.ColumnCount;

    public ColumnHeader Add(string? text, int width)
    {
        ColumnHeader column = new(text, width);
        _owner.AddColumn(column);
        return column;
    }

    public IEnumerator<ColumnHeader> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    public ColumnHeader this[int index] => GetColumn(index);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private ColumnHeader GetColumn(int index) => _owner.GetColumn(index);
}

public sealed class ListViewSubItem
{
    private string _text;

    public ListViewSubItem()
        : this(string.Empty)
    {
    }

    public ListViewSubItem(string? text)
    {
        _text = text ?? string.Empty;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            Owner?.RefreshItem(Item!);
        }
    }

    internal ListViewItem? Item { get; set; }

    private ListView? Owner => Item?.Owner;
}

public sealed class ListViewSubItemCollection : IEnumerable<ListViewSubItem>
{
    private readonly ListViewItem _owner;
    private readonly List<ListViewSubItem> _items = [];

    internal ListViewSubItemCollection(ListViewItem owner)
    {
        _owner = owner;
    }

    public int Count => _items.Count;

    public ListViewSubItem this[int index] => _items[index];

    public ListViewSubItem Add(string? text)
    {
        ListViewSubItem item = new(text) { Item = _owner };
        _items.Add(item);
        _owner.Owner?.RefreshItem(_owner);
        return item;
    }

    public IEnumerator<ListViewSubItem> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class ListViewItem
{
    private bool _selected;

    public ListViewItem()
        : this(string.Empty)
    {
    }

    public ListViewItem(string? text)
    {
        SubItems = new ListViewSubItemCollection(this);
        SubItems.Add(text);
    }

    public ListViewSubItemCollection SubItems { get; }

    public object? Tag { get; set; }

    public string Text
    {
        get => SubItems[0].Text;
        set => SubItems[0].Text = value ?? string.Empty;
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (Owner is not null)
            {
                Owner.SetItemSelected(this, value);
                return;
            }

            SetSelected(value);
        }
    }

    internal ListView? Owner { get; set; }

    internal void SetSelected(bool value)
    {
        _selected = value;
    }
}

public sealed class ListViewItemCollection : IEnumerable<ListViewItem>
{
    private readonly ListView _owner;

    internal ListViewItemCollection(ListView owner)
    {
        _owner = owner;
    }

    public int Count => _owner.ItemCount;

    public ListViewItem this[int index] => _owner.GetItem(index);

    public ListViewItem Add(string? text)
    {
        ListViewItem item = new(text);
        Add(item);
        return item;
    }

    public void Add(ListViewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Owner is not null)
        {
            throw new InvalidOperationException("The item already belongs to a list view.");
        }

        _owner.AddItem(item);
    }

    public void Remove(ListViewItem item) => _owner.RemoveItem(item);

    public void Clear() => _owner.ClearItems();

    public IEnumerator<ListViewItem> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
