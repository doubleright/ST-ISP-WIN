using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public abstract class ScrollBar : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _smallChange = 1;
    private int _largeChange = 10;

    private protected ScrollBar(int width, int height)
        : base(tabStop: true, width: width, height: height)
    {
    }

    public int Minimum
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
                _value = _minimum;
            }

            ApplyScrollInfo();
        }
    }

    public int Maximum
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
                _value = _maximum;
            }

            ApplyScrollInfo();
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            Application.VerifyUiThread();
            if (value < _minimum || value > _maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be between Minimum and Maximum.");
            }

            SetValue(value, raiseScroll: false);
        }
    }

    public int SmallChange
    {
        get => _smallChange;
        set
        {
            Application.VerifyUiThread();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _smallChange = value;
        }
    }

    public int LargeChange
    {
        get => _largeChange;
        set
        {
            Application.VerifyUiThread();
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _largeChange = value;
            ApplyScrollInfo();
        }
    }

    public event EventHandler? Scroll;

    public event EventHandler? ValueChanged;

    internal override string NativeClassName => "SCROLLBAR";

    internal override void CreateHandle()
    {
        base.CreateHandle();
        ApplyScrollInfo();
    }

    internal override void OnScroll(WPARAM wParam)
    {
        int code = unchecked((short)(wParam.Value & 0xFFFF));
        switch (code)
        {
            case NativeConstants.SB_LINEUP:
                SetValue(_value - _smallChange, raiseScroll: true);
                break;
            case NativeConstants.SB_LINEDOWN:
                SetValue(_value + _smallChange, raiseScroll: true);
                break;
            case NativeConstants.SB_PAGEUP:
                SetValue(_value - _largeChange, raiseScroll: true);
                break;
            case NativeConstants.SB_PAGEDOWN:
                SetValue(_value + _largeChange, raiseScroll: true);
                break;
            case NativeConstants.SB_THUMBTRACK:
            case NativeConstants.SB_THUMBPOSITION:
                SetValue(ReadTrackPosition(), raiseScroll: true);
                break;
            case NativeConstants.SB_TOP:
                SetValue(_minimum, raiseScroll: true);
                break;
            case NativeConstants.SB_BOTTOM:
                SetValue(_maximum, raiseScroll: true);
                break;
            case NativeConstants.SB_ENDSCROLL:
                Scroll?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void SetValue(int value, bool raiseScroll)
    {
        if (value < _minimum)
        {
            value = _minimum;
        }

        if (value > _maximum)
        {
            value = _maximum;
        }

        bool changed = _value != value;
        _value = value;
        ApplyScrollInfo();
        if (raiseScroll)
        {
            Scroll?.Invoke(this, EventArgs.Empty);
        }

        if (changed)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private unsafe int ReadTrackPosition()
    {
        SCROLLINFO info = new()
        {
            cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(),
            fMask = SCROLLINFO_MASK.SIF_TRACKPOS,
        };
        _ = NativeSend.Send(NativeHandle, NativeConstants.SBM_GETSCROLLINFO, 0, ref info);
        return info.nTrackPos;
    }

    private unsafe void ApplyScrollInfo()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        SCROLLINFO info = new()
        {
            cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(),
            fMask = SCROLLINFO_MASK.SIF_RANGE | SCROLLINFO_MASK.SIF_POS | SCROLLINFO_MASK.SIF_PAGE,
            nMin = _minimum,
            nMax = _maximum,
            nPos = _value,
            nPage = (uint)Math.Max(_largeChange, 1),
        };
        _ = NativeSend.Send(NativeHandle, NativeConstants.SBM_SETSCROLLINFO, 1, ref info);
    }
}

public sealed class HScrollBar : ScrollBar
{
    public HScrollBar()
        : base(width: 80, height: 17)
    {
    }
}

public sealed class VScrollBar : ScrollBar
{
    public VScrollBar()
        : base(width: 17, height: 80)
    {
    }

    internal override WINDOW_STYLE NativeStyle => base.NativeStyle | (WINDOW_STYLE)NativeConstants.SBS_VERT;
}
