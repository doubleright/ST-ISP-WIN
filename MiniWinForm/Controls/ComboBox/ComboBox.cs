using System.Collections;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public enum ComboBoxStyle
{
    Simple,
    DropDown,
    DropDownList,
}

public sealed class ComboBox : Control
{
    private readonly ObjectCollection _items;
    private ComboBoxStyle _dropDownStyle = ComboBoxStyle.DropDown;
    private int _selectedIndex = -1;
    private int _maxDropDownItems = 8;

    public ComboBox()
        : base(tabStop: true, width: 121, height: 23)
    {
        _items = new ObjectCollection(this);
    }

    public ObjectCollection Items => _items;

    public ComboBoxStyle DropDownStyle
    {
        get => _dropDownStyle;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_dropDownStyle == value)
            {
                return;
            }

            _dropDownStyle = value;
            RecreateHandle();
        }
    }

    public int MaxDropDownItems
    {
        get => _maxDropDownItems;
        set
        {
            Application.VerifyUiThread();
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _maxDropDownItems = value;
            UpdateNativeBounds();
        }
    }

    public int SelectedIndex
    {
        get
        {
            Application.VerifyUiThread();
            if (IsHandleCreated)
            {
                // 只读不写：读取属性不该改变「是否已经通知过」的状态，否则会吞掉后面的通知。
                return ReadSelectedIndex();
            }

            return _selectedIndex;
        }
        set
        {
            Application.VerifyUiThread();
            if (value < -1 || value >= Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "SelectedIndex must be -1 or a valid item index.");
            }

            SetSelectedIndex(value, updateNative: true, raiseEvent: true);
        }
    }

    public object? SelectedItem => SelectedIndex == -1 ? null : Items[SelectedIndex];

    public event EventHandler? SelectedIndexChanged;

    public event EventHandler? DropDown;

    public event EventHandler? DropDownClosed;

    internal override string NativeClassName => "COMBOBOX";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        WINDOW_STYLE.WS_VSCROLL |
        (WINDOW_STYLE)NativeConstants.CBS_AUTOHSCROLL |
        (WINDOW_STYLE)NativeConstants.CBS_HASSTRINGS |
        (WINDOW_STYLE)(_dropDownStyle switch
        {
            ComboBoxStyle.Simple => NativeConstants.CBS_SIMPLE,
            ComboBoxStyle.DropDownList => NativeConstants.CBS_DROPDOWNLIST,
            _ => NativeConstants.CBS_DROPDOWN,
        });

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    internal override void GetNativeWindowSize(out int width, out int height)
    {
        width = Width;
        if (_dropDownStyle == ComboBoxStyle.Simple)
        {
            height = Height;
            return;
        }

        int itemHeight = 16;
        if (IsHandleCreated)
        {
            int nativeHeight = (int)PInvoke.SendMessage(NativeHandle, NativeConstants.CB_GETITEMHEIGHT, new WPARAM(0), default).Value;
            if (nativeHeight > 0)
            {
                itemHeight = nativeHeight;
            }
        }

        height = Height + (_maxDropDownItems * itemHeight);
    }

    internal override void CreateHandle()
    {
        base.CreateHandle();
        foreach (object item in Items)
        {
            AddNativeString(GetItemText(item));
        }

        if (_selectedIndex >= 0 && _selectedIndex < Items.Count)
        {
            NativeSend.Send(NativeHandle, NativeConstants.CB_SETCURSEL, (nuint)_selectedIndex, 0);
        }
    }

    internal override void OnCommand(int notificationCode)
    {
        switch (notificationCode)
        {
            case NativeConstants.CBN_SELENDOK:
            case NativeConstants.CBN_SELCHANGE:
                // 下拉里选定（SELENDOK）就立刻提交，SELCHANGE 兜底，不等失焦。
                SyncSelectedIndexFromNative();
                break;
            case NativeConstants.CBN_DROPDOWN:
                DropDown?.Invoke(this, EventArgs.Empty);
                break;
            case NativeConstants.CBN_CLOSEUP:
                DropDownClosed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    internal override void OnTextChanged()
    {
        if (_dropDownStyle != ComboBoxStyle.DropDownList || !IsHandleCreated)
        {
            return;
        }

        int index = (int)NativeSend.SendString(
            NativeHandle,
            NativeConstants.CB_FINDSTRINGEXACT,
            unchecked((nuint)(-1)),
            Text);
        if (index == NativeConstants.CB_ERR)
        {
            index = -1;
        }

        SetSelectedIndex(index, updateNative: true, raiseEvent: true);
    }

    /// <summary>把原生当前选中项同步成托管状态，有变化才发事件。</summary>
    private void SyncSelectedIndexFromNative()
    {
        int index = ReadSelectedIndex();
        if (_selectedIndex == index)
        {
            return;
        }

        _selectedIndex = index;
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private int ReadSelectedIndex()
    {
        int index = (int)NativeSend.Send(NativeHandle, NativeConstants.CB_GETCURSEL, 0, 0);
        return index == NativeConstants.CB_ERR ? -1 : index;
    }

    private void SetSelectedIndex(int value, bool updateNative, bool raiseEvent)
    {
        if (_selectedIndex == value && (!updateNative || !IsHandleCreated))
        {
            return;
        }

        _selectedIndex = value;
        if (updateNative && IsHandleCreated)
        {
            NativeSend.Send(NativeHandle, NativeConstants.CB_SETCURSEL, unchecked((nuint)value), 0);
        }

        if (raiseEvent)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AddNativeString(string text)
    {
        if ((int)NativeSend.SendString(NativeHandle, NativeConstants.CB_ADDSTRING, 0, text) == NativeConstants.CB_ERR)
        {
            throw new InvalidOperationException($"CB_ADDSTRING failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }

    private void InsertNativeString(int index, string text)
    {
        if ((int)NativeSend.SendString(NativeHandle, NativeConstants.CB_INSERTSTRING, (nuint)index, text) == NativeConstants.CB_ERR)
        {
            throw new InvalidOperationException($"CB_INSERTSTRING failed. LastError={Marshal.GetLastPInvokeError()}.");
        }
    }

    private static string GetItemText(object item) => item.ToString() ?? string.Empty;

    public sealed class ObjectCollection : IList<object>
    {
        private readonly ComboBox _owner;
        private readonly List<object> _items = [];

        internal ObjectCollection(ComboBox owner)
        {
            _owner = owner;
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public object this[int index]
        {
            get => _items[index];
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _items[index] = value;
                if (_owner.IsHandleCreated)
                {
                    _ = NativeSend.Send(_owner.NativeHandle, NativeConstants.CB_DELETESTRING, (nuint)index, 0);
                    _owner.InsertNativeString(index, GetItemText(value));
                    if (_owner._selectedIndex == index)
                    {
                        NativeSend.Send(_owner.NativeHandle, NativeConstants.CB_SETCURSEL, (nuint)index, 0);
                    }
                }
            }
        }

        public int Add(object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            int index = _items.Count;
            _items.Add(item);
            if (_owner.IsHandleCreated)
            {
                _owner.AddNativeString(GetItemText(item));
            }

            return index;
        }

        void ICollection<object>.Add(object item) => Add(item);

        public void AddRange(params object[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (object item in items)
            {
                Add(item);
            }
        }

        public void Clear()
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
            if (_owner.IsHandleCreated)
            {
                _ = NativeSend.Send(_owner.NativeHandle, NativeConstants.CB_RESETCONTENT, 0, 0);
            }

            _owner.SetSelectedIndex(-1, updateNative: false, raiseEvent: true);
        }

        public bool Contains(object item) => _items.Contains(item);

        public void CopyTo(object[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(object item) => _items.IndexOf(item);

        public void Insert(int index, object item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Insert(index, item);
            if (_owner.IsHandleCreated)
            {
                _owner.InsertNativeString(index, GetItemText(item));
            }

            if (_owner._selectedIndex >= index)
            {
                _owner.SetSelectedIndex(_owner._selectedIndex + 1, updateNative: true, raiseEvent: false);
            }
        }

        public bool Remove(object item)
        {
            int index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
            if (_owner.IsHandleCreated)
            {
                _ = NativeSend.Send(_owner.NativeHandle, NativeConstants.CB_DELETESTRING, (nuint)index, 0);
            }

            if (_owner._selectedIndex == index)
            {
                _owner.SetSelectedIndex(-1, updateNative: true, raiseEvent: true);
            }
            else if (_owner._selectedIndex > index)
            {
                _owner.SetSelectedIndex(_owner._selectedIndex - 1, updateNative: true, raiseEvent: false);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
