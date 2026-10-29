using System.Drawing;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public enum ButtonStyle
{
    /// <summary>系统主题按钮（默认）。</summary>
    Standard,

    /// <summary>扁平样式：用 BackColor 填底、ForeColor 写字、1px 边框，按下时底色压暗。</summary>
    Flat,
}

public sealed class Button : Control
{
    private DialogResult _dialogResult;
    private ButtonStyle _style;
    private Color _borderColor = Color.Empty;

    public Button()
        : base(tabStop: true, width: 75, height: 23)
    {
    }

    public ButtonStyle Style
    {
        get => _style;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_style == value)
            {
                return;
            }

            _style = value;
            if (IsHandleCreated)
            {
                NativeControl.Invalidate(NativeHandle);
            }
        }
    }

    /// <summary>仅 Flat 样式使用；留空时按底色自动压暗 14%。</summary>
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            Application.VerifyUiThread();
            if (_borderColor == value)
            {
                return;
            }

            _borderColor = value;
            if (IsHandleCreated)
            {
                NativeControl.Invalidate(NativeHandle);
            }
        }
    }

    public DialogResult DialogResult
    {
        get => _dialogResult;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _dialogResult = value;
        }
    }

    public event EventHandler? Click;

    internal override string NativeClassName => "BUTTON";

    internal override WINDOW_STYLE NativeStyle => base.NativeStyle | (WINDOW_STYLE)NativeConstants.BS_OWNERDRAW;

    internal override void OnCommand(int notificationCode)
    {
        if (notificationCode == NativeConstants.BN_CLICKED)
        {
            Click?.Invoke(this, EventArgs.Empty);
            if (DialogResult != DialogResult.None && FindForm() is { Modal: true } form)
            {
                form.DialogResult = DialogResult;
                form.Close();
            }
        }
    }

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawButton(drawItem, Text, NativeHandle, this);
        return true;
    }
}