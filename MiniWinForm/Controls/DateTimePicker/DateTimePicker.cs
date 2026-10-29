using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public enum DateTimePickerFormat
{
    Long,
    Short,
    Time,
    Custom,
}

public sealed class DateTimePicker : Control
{
    private static readonly DateTime MinimumDate = new(1753, 1, 1);
    private static readonly DateTime MaximumDate = new(9998, 12, 31);
    private DateTime _value = DateTime.Now;
    private DateTimePickerFormat _format = DateTimePickerFormat.Short;
    private string _customFormat = "yyyy-MM-dd";
    private bool _showCheckBox;
    private bool _showUpDown;
    private bool _checked = true;
    private bool _applying;

    public DateTimePicker()
        : base(tabStop: true, width: 200, height: 23)
    {
    }

    public DateTime Value
    {
        get => _value;
        set
        {
            Application.VerifyUiThread();
            ValidateDate(value);
            if (_value == value)
            {
                return;
            }

            _value = value;
            _checked = true;
            ApplyValue();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DateTimePickerFormat Format
    {
        get => _format;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_format == value)
            {
                return;
            }

            _format = value;
            RecreateHandle();
        }
    }

    public string CustomFormat
    {
        get => _customFormat;
        set
        {
            Application.VerifyUiThread();
            _customFormat = value ?? string.Empty;
            ApplyFormat();
        }
    }

    public bool ShowCheckBox
    {
        get => _showCheckBox;
        set
        {
            Application.VerifyUiThread();
            if (_showCheckBox == value)
            {
                return;
            }

            _showCheckBox = value;
            RecreateHandle();
        }
    }

    public bool ShowUpDown
    {
        get => _showUpDown;
        set
        {
            Application.VerifyUiThread();
            if (_showUpDown == value)
            {
                return;
            }

            _showUpDown = value;
            RecreateHandle();
        }
    }

    public bool Checked
    {
        get => !_showCheckBox || _checked;
        set
        {
            Application.VerifyUiThread();
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            ApplyValue();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    internal override string NativeClassName => "SysDateTimePick32";

    internal override WINDOW_STYLE NativeStyle
    {
        get
        {
            WINDOW_STYLE style = base.NativeStyle;
            style |= _format switch
            {
                DateTimePickerFormat.Long => (WINDOW_STYLE)NativeConstants.DTS_LONGDATEFORMAT,
                DateTimePickerFormat.Time => (WINDOW_STYLE)NativeConstants.DTS_TIMEFORMAT,
                _ => 0,
            };
            if (_showUpDown && _format != DateTimePickerFormat.Time)
            {
                style |= (WINDOW_STYLE)NativeConstants.DTS_UPDOWN;
            }

            if (_showCheckBox)
            {
                style |= (WINDOW_STYLE)NativeConstants.DTS_SHOWNONE;
            }

            return style;
        }
    }

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    internal override void CreateHandle()
    {
        base.CreateHandle();
        ApplyFormat();
        ApplyValue();
    }

    internal override unsafe LRESULT OnNotify(uint code, LPARAM lParam)
    {
        if (_applying || unchecked((int)code) != NativeConstants.DTN_DATETIMECHANGE)
        {
            return default;
        }

        NMDATETIMECHANGE* change = (NMDATETIMECHANGE*)lParam.Value;
        DateTime previous = _value;
        bool wasChecked = Checked;
        _checked = change->dwFlags != NMDATETIMECHANGE_FLAGS.GDT_NONE;
        if (_checked)
        {
            _value = NativeDateTime.FromSystemTime(change->st);
        }

        if (wasChecked != Checked || _value != previous)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        return default;
    }

    private unsafe void ApplyValue()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        SYSTEMTIME time = NativeDateTime.ToSystemTime(_value);
        nuint flag = (nuint)(!_showCheckBox || _checked ? NativeConstants.GDT_VALID : NativeConstants.GDT_NONE);
        _applying = true;
        try
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.DTM_SETSYSTEMTIME, flag, ref time);
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyFormat()
    {
        if (!IsHandleCreated || _format != DateTimePickerFormat.Custom)
        {
            return;
        }

        _ = NativeSend.SendString(NativeHandle, NativeConstants.DTM_SETFORMAT, 0, _customFormat);
    }

    private static void ValidateDate(DateTime value)
    {
        if (value < MinimumDate || value > MaximumDate)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be between 1753-01-01 and 9998-12-31.");
        }
    }
}
