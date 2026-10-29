using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public sealed class MonthCalendar : Control
{
    private static readonly DateTime MinimumDate = new(1753, 1, 1);
    private static readonly DateTime MaximumDate = new(9998, 12, 31);
    private DateTime _selectionStart = DateTime.Today;
    private bool _applying;

    public MonthCalendar()
        : base(tabStop: true, width: 227, height: 162)
    {
    }

    public DateTime SelectionStart
    {
        get => _selectionStart;
        set
        {
            Application.VerifyUiThread();
            DateTime date = value.Date;
            if (date < MinimumDate || date > MaximumDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "SelectionStart must be between 1753-01-01 and 9998-12-31.");
            }

            if (_selectionStart == date)
            {
                return;
            }

            _selectionStart = date;
            ApplySelection();
            DateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? DateChanged;

    internal override string NativeClassName => "SysMonthCal32";

    internal override void CreateHandle()
    {
        base.CreateHandle();
        ApplySelection();
    }

    internal override unsafe LRESULT OnNotify(uint code, LPARAM lParam)
    {
        int notification = unchecked((int)code);
        if (_applying || (notification != NativeConstants.MCN_SELCHANGE && notification != NativeConstants.MCN_SELECT))
        {
            return default;
        }

        NMSELCHANGE* change = (NMSELCHANGE*)lParam.Value;
        DateTime date = NativeDateTime.FromSystemTime(change->stSelStart).Date;
        if (_selectionStart == date)
        {
            return default;
        }

        _selectionStart = date;
        DateChanged?.Invoke(this, EventArgs.Empty);
        return default;
    }

    private unsafe void ApplySelection()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        SYSTEMTIME time = NativeDateTime.ToSystemTime(_selectionStart);
        _applying = true;
        try
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.MCM_SETCURSEL, 0, ref time);
        }
        finally
        {
            _applying = false;
        }
    }
}
