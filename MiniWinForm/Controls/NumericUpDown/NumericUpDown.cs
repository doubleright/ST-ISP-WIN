using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public sealed class NumericUpDown : Control
{
    private decimal _minimum;
    private decimal _maximum = 100;
    private decimal _value;
    private decimal _increment = 1;
    private int _decimalPlaces;
    private bool _updatingText;
    private HWND _upDownHandle;

    public NumericUpDown()
        : base(tabStop: true, width: 120, height: 23)
    {
        HandleDestroyed += (_, _) => DestroyUpDown();
    }

    public decimal Minimum
    {
        get => _minimum;
        set
        {
            Application.VerifyUiThread();
            if (value > _maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Minimum cannot be greater than Maximum.");
            }

            _minimum = value;
            if (_value < _minimum)
            {
                SetValue(_minimum, updateText: true, raiseEvent: true);
            }
        }
    }

    public decimal Maximum
    {
        get => _maximum;
        set
        {
            Application.VerifyUiThread();
            if (value < _minimum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum cannot be less than Minimum.");
            }

            _maximum = value;
            if (_value > _maximum)
            {
                SetValue(_maximum, updateText: true, raiseEvent: true);
            }
        }
    }

    public decimal Increment
    {
        get => _increment;
        set
        {
            Application.VerifyUiThread();
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Increment cannot be negative.");
            }

            _increment = value;
        }
    }

    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            Application.VerifyUiThread();
            if (value is < 0 or > 99)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "DecimalPlaces must be from 0 through 99.");
            }

            _decimalPlaces = value;
            if (IsHandleCreated)
            {
                SetValue(_value, updateText: true, raiseEvent: false);
            }
        }
    }

    public decimal Value
    {
        get => _value;
        set
        {
            Application.VerifyUiThread();
            if (value < _minimum || value > _maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be between Minimum and Maximum.");
            }

            SetValue(Round(value), updateText: true, raiseEvent: true);
        }
    }

    public event EventHandler? ValueChanged;

    internal override string NativeClassName => "EDIT";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        (WINDOW_STYLE)NativeConstants.ES_AUTOHSCROLL |
        (WINDOW_STYLE)NativeConstants.ES_RIGHT;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    public void UpButton() => CommitDelta(1);

    public void DownButton() => CommitDelta(-1);

    internal override void CreateHandle()
    {
        base.CreateHandle();
        WriteText(Format(_value));
        CreateUpDown();
    }

    internal override void OnCommand(int notificationCode)
    {
        if (_updatingText)
        {
            return;
        }

        if (notificationCode == NativeConstants.EN_KILLFOCUS)
        {
            if (TryParse(Text, out decimal parsed))
            {
                SetValue(Clamp(parsed), updateText: true, raiseEvent: true);
            }
            else
            {
                WriteText(Format(_value));
            }
        }
    }

    internal override unsafe LRESULT OnNotify(uint code, LPARAM lParam)
    {
        if (unchecked((int)code) != NativeConstants.UDN_DELTAPOS)
        {
            return default;
        }

        NMUPDOWN* change = (NMUPDOWN*)lParam.Value;

        // Win32 约定：iDelta 上箭头为 -1、下箭头为 +1，方向和步进是反的，取反再走。
        CommitDelta(-change->iDelta);
        return new LRESULT(1);
    }

    internal override bool ProcessWindowMessage(uint message, WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        result = default;
        if (message == NativeConstants.WM_WINDOWPOSCHANGED)
        {
            PositionUpDown();
            return false;
        }

        if (message == NativeConstants.WM_KEYDOWN)
        {
            int key = unchecked((int)(wParam.Value & 0xFFFF));
            if (key == NativeConstants.VK_UP)
            {
                CommitDelta(1);
                return true;
            }

            if (key == NativeConstants.VK_DOWN)
            {
                CommitDelta(-1);
                return true;
            }
        }

        if (message == NativeConstants.WM_MOUSEWHEEL)
        {
            int delta = unchecked((short)((wParam.Value >> 16) & 0xFFFF));
            if (delta != 0)
            {
                CommitDelta(delta > 0 ? 1 : -1);
            }

            return true;
        }

        return false;
    }

    internal override void OnVisibleChanged()
    {
        if (_upDownHandle != default)
        {
            NativeControl.Show(_upDownHandle, Visible);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (_upDownHandle != default)
        {
            NativeControl.SetEnabled(_upDownHandle, Enabled);
        }
    }

    private void CommitDelta(int steps)
    {
        if (steps == 0 || _increment == 0)
        {
            return;
        }

        SetValue(Clamp(_value + (_increment * steps)), updateText: true, raiseEvent: true);
    }

    private void SetValue(decimal value, bool updateText, bool raiseEvent)
    {
        value = Round(value);
        bool changed = _value != value;
        _value = value;
        if (updateText && IsHandleCreated)
        {
            WriteText(Format(_value));
        }

        if (changed && raiseEvent)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private decimal Clamp(decimal value)
    {
        if (value < _minimum)
        {
            return _minimum;
        }

        if (value > _maximum)
        {
            return _maximum;
        }

        return value;
    }

    private decimal Round(decimal value)
    {
        return Math.Round(value, _decimalPlaces, MidpointRounding.AwayFromZero);
    }

    private string Format(decimal value) => value.ToString("F" + _decimalPlaces, CultureInfo.CurrentCulture);

    private static bool TryParse(string text, out decimal value)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    private void WriteText(string text)
    {
        _updatingText = true;
        Text = text;
        _updatingText = false;
    }

    private unsafe void CreateUpDown()
    {
        DestroyUpDown();
        fixed (char* className = "msctls_updown32")
        {
            WINDOW_STYLE style = WINDOW_STYLE.WS_CHILD | (WINDOW_STYLE)NativeConstants.UDS_NOTHOUSANDS;
            if (Visible)
            {
                style |= WINDOW_STYLE.WS_VISIBLE;
            }

            if (!Enabled)
            {
                style |= WINDOW_STYLE.WS_DISABLED;
            }

            _upDownHandle = PInvoke.CreateWindowEx(
                0,
                new PCWSTR(className),
                default,
                style,
                0,
                0,
                0,
                0,
                Parent!.NativeHandle,
                default,
                Application.Instance != default ? Application.Instance : NativeApplication.GetModuleHandle(),
                null);
        }

        if (_upDownHandle == default)
        {
            throw new InvalidOperationException($"CreateWindowEx failed for up-down. LastError={Marshal.GetLastPInvokeError()}.");
        }

        NativeControl.ApplyDefaultFont(_upDownHandle);
        NativeControl.RegisterNotifySource(_upDownHandle, this);
        PositionUpDown();
    }

    private void PositionUpDown()
    {
        if (_upDownHandle == default || !PInvoke.IsWindow(_upDownHandle))
        {
            return;
        }

        int upWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVSCROLL);
        if (upWidth < 1)
        {
            upWidth = 16;
        }

        int offsetLeft = 0;
        int offsetTop = 0;
        Parent?.GetChildWindowOffset(out offsetLeft, out offsetTop);
        int width = Math.Min(upWidth, Math.Max(Width, 1));
        NativeControl.Move(_upDownHandle, Left + offsetLeft + Math.Max(0, Width - width), Top + offsetTop, width, Math.Max(Height, 1));

        // 箭头和编辑框是兄弟窗口：布局时编辑框会被提到最上层，把箭头整个盖住，这里置顶压回去。
        if (!PInvoke.SetWindowPos(
            _upDownHandle,
            new HWND(0) /* HWND_TOP */,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE))
        {
            throw new InvalidOperationException($"SetWindowPos failed for up-down. LastError={Marshal.GetLastPInvokeError()}.");
        }

        _ = NativeSend.Send(NativeHandle, NativeConstants.EM_SETMARGINS, NativeConstants.EC_RIGHTMARGIN, upWidth << 16);
    }

    private void DestroyUpDown()
    {
        if (_upDownHandle == default)
        {
            return;
        }

        NativeControl.UnregisterNotifySource(_upDownHandle);
        if (PInvoke.IsWindow(_upDownHandle))
        {
            _ = PInvoke.DestroyWindow(_upDownHandle);
        }

        _upDownHandle = default;
    }
}
