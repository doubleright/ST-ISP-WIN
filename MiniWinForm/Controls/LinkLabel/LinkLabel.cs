using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public sealed class LinkLabel : Control
{
    public LinkLabel()
        : base(tabStop: true, width: 100, height: 23)
    {
    }

    public event EventHandler<LinkLabelLinkClickedEventArgs>? LinkClicked;

    internal override string NativeClassName => "SysLink";

    internal override WINDOW_STYLE NativeStyle => base.NativeStyle | (WINDOW_STYLE)NativeConstants.LWS_TRANSPARENT;

    internal override unsafe LRESULT OnNotify(uint code, LPARAM lParam)
    {
        int notification = unchecked((int)code);
        if (notification != NativeConstants.NM_CLICK && notification != NativeConstants.NM_RETURN)
        {
            return default;
        }

        NMLINK* link = (NMLINK*)lParam.Value;
        LinkClicked?.Invoke(this, new LinkLabelLinkClickedEventArgs(new string((char*)&link->item.szUrl)));
        return default;
    }
}

public sealed class LinkLabelLinkClickedEventArgs : EventArgs
{
    public LinkLabelLinkClickedEventArgs(string url)
    {
        Url = url ?? string.Empty;
    }

    public string Url { get; }
}
