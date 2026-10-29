using System.Drawing;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public enum GroupBoxStyle
{
    /// <summary>系统主题的蚀刻边框，标题嵌在边框上（默认）。</summary>
    Standard,

    /// <summary>卡片：底色自绘、1px 细边框、标题独占一条顶栏、顶栏下有分隔线。</summary>
    Card,
}

public sealed class GroupBox : Control
{
    private GroupBoxStyle _style;
    private Color _borderColor = Color.Empty;

    public GroupBox()
        : base(tabStop: false, width: 200, height: 100)
    {
    }

    public GroupBoxStyle Style
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

    /// <summary>仅 Card 样式使用；留空时按底色自动压暗 10%。</summary>
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

    internal override string NativeClassName => "BUTTON";

    internal override WINDOW_STYLE NativeStyle =>
        WINDOW_STYLE.WS_CHILD |
        WINDOW_STYLE.WS_CLIPSIBLINGS |
        WINDOW_STYLE.WS_CLIPCHILDREN |
        (WINDOW_STYLE)NativeConstants.BS_OWNERDRAW;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CONTROLPARENT;

    internal override bool DrawItem(in DRAWITEMSTRUCT drawItem)
    {
        NativeDraw.DrawGroupBox(drawItem, Text, this);
        return true;
    }
}
