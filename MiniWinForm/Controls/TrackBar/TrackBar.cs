using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public enum Orientation
{
    Horizontal,
    Vertical,
}

public sealed class TrackBar : Control
{
    private const int DefaultHorizontalWidth = 104;
    private const int DefaultHorizontalHeight = 45;
    private int _minimum;
    private int _maximum = 10;
    private int _value;
    private int _smallChange = 1;
    private int _largeChange = 5;
    private int _tickFrequency = 1;
    private Orientation _orientation;

    public TrackBar()
        : base(tabStop: true, width: DefaultHorizontalWidth, height: DefaultHorizontalHeight)
    {
    }

    public int Minimum
    {
        get => _minimum;
        set
        {
            Application.VerifyUiThread();
            if (_minimum == value)
            {
                return;
            }

            if (value > _maximum)
            {
                _maximum = value;
            }

            _minimum = value;
            if (_value < _minimum)
            {
                _value = _minimum;
            }

            ApplyRange();
            ApplyValue(redraw: true);
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            Application.VerifyUiThread();
            if (_maximum == value)
            {
                return;
            }

            if (value < _minimum)
            {
                _minimum = value;
            }

            _maximum = value;
            if (_value > _maximum)
            {
                _value = _maximum;
            }

            ApplyRange();
            ApplyValue(redraw: true);
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

            SetValue(value, updateNative: true, raiseScroll: false);
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
            ApplyStep();
        }
    }

    public int LargeChange
    {
        get => _largeChange;
        set
        {
            Application.VerifyUiThread();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _largeChange = value;
            ApplyStep();
        }
    }

    public int TickFrequency
    {
        get => _tickFrequency;
        set
        {
            Application.VerifyUiThread();
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _tickFrequency = value;
            ApplyStep();
        }
    }

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_orientation == value)
            {
                return;
            }

            bool defaultSize = _orientation == Orientation.Horizontal
                ? Width == DefaultHorizontalWidth && Height == DefaultHorizontalHeight
                : Width == DefaultHorizontalHeight && Height == DefaultHorizontalWidth;
            _orientation = value;
            if (defaultSize)
            {
                int width = Width;
                Width = Height;
                Height = width;
            }

            RecreateHandle();
        }
    }

    public event EventHandler? Scroll;

    public event EventHandler? ValueChanged;

    internal override string NativeClassName => "msctls_trackbar32";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        (WINDOW_STYLE)NativeConstants.TBS_AUTOTICKS |
        (WINDOW_STYLE)(_orientation == Orientation.Vertical ? NativeConstants.TBS_VERT : 0);

    internal override void CreateHandle()
    {
        base.CreateHandle();
        ApplyRange();
        ApplyStep();
        ApplyValue(redraw: true);
    }

    internal override void OnScroll(WPARAM wParam)
    {
        _ = wParam;
        int position = (int)NativeSend.Send(NativeHandle, NativeConstants.TBM_GETPOS, 0, 0);
        SetValue(FromNativePosition(position), updateNative: false, raiseScroll: true);
    }

    // 竖向轨道的原生位置 0 在顶部（值越小越靠上），WinForms 的语义是值越大越靠上，
    // 所以竖向要在托管层把位置翻转一次，和 System.Windows.Forms.TrackBar 的做法一致。
    private int ToNativePosition(int value) => _orientation == Orientation.Vertical
        ? _maximum + _minimum - value
        : value;

    private int FromNativePosition(int position) => _orientation == Orientation.Vertical
        ? _maximum + _minimum - position
        : position;

    private void SetValue(int value, bool updateNative, bool raiseScroll)
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
        if (updateNative)
        {
            ApplyValue(redraw: true);
        }

        if (raiseScroll)
        {
            Scroll?.Invoke(this, EventArgs.Empty);
        }

        if (changed)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyRange()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        _ = NativeSend.Send(NativeHandle, NativeConstants.TBM_SETRANGEMIN, 1, _minimum);
        _ = NativeSend.Send(NativeHandle, NativeConstants.TBM_SETRANGEMAX, 1, _maximum);
    }

    private void ApplyValue(bool redraw)
    {
        if (IsHandleCreated)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.TBM_SETPOS, redraw ? 1u : 0u, ToNativePosition(_value));
        }
    }

    private void ApplyStep()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        _ = NativeSend.Send(NativeHandle, NativeConstants.TBM_SETLINESIZE, 0, _smallChange);
        _ = NativeSend.Send(NativeHandle, NativeConstants.TBM_SETPAGESIZE, 0, _largeChange);
        _ = NativeSend.Send(NativeHandle, NativeConstants.TBM_SETTICFREQ, 0, _tickFrequency);
    }
}
