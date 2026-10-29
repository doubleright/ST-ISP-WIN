using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public enum FontStyle
{
    Regular = 0,
    Bold = 1,
}

/// <summary>
/// 极简字体对象：只关心字族、磅值和粗体，句柄按需创建后在实例上缓存。
/// 刻意不做 WinForms 那套 FontFamily 缓存/释放，界面上的字体实例通常是几个静态字段，进程内一直用到退出。
/// </summary>
public sealed unsafe class Font
{
    /// <summary>LOGFONTW 的 lfFaceName 偏移：5 个 LONG + 8 个 BYTE = 28 字节，之后是 WCHAR[32]。</summary>
    private const int FaceNameOffset = 28;

    private HFONT _handle;

    public Font(string family, float size, FontStyle style = FontStyle.Regular)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("Font family must not be empty.", nameof(family));
        }

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Font size must be positive.");
        }

        Family = family;
        Size = size;
        Style = style;
    }

    public string Family { get; }

    public float Size { get; }

    public FontStyle Style { get; }

    internal HFONT Handle
    {
        get
        {
            if (_handle == default)
            {
                _handle = CreateHandle();
            }

            return _handle;
        }
    }

    public override string ToString() => $"{Family} {Size:0.##}pt {Style}";

    private HFONT CreateHandle()
    {
        NONCLIENTMETRICSW metrics = new()
        {
            cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICSW>(),
        };

        if (!PInvoke.SystemParametersInfo(
            (SYSTEM_PARAMETERS_INFO_ACTION)NativeConstants.SPI_GETNONCLIENTMETRICS,
            metrics.cbSize,
            &metrics,
            0))
        {
            throw new InvalidOperationException($"SystemParametersInfo(SPI_GETNONCLIENTMETRICS) failed. LastError={Marshal.GetLastPInvokeError()}.");
        }

        // 以系统消息字体为底稿，只改字高/字重/字族，字符集等设置保持系统默认（中文会自动回落到雅黑）。
        LOGFONTW logFont = metrics.lfMessageFont;
        logFont.lfHeight = -HeightInPixels(Size);
        logFont.lfWeight = Style == FontStyle.Bold ? 700 : 400;
        WriteFaceName(ref logFont, Family);

        HFONT handle = PInvoke.CreateFontIndirect(&logFont);
        if (handle == default)
        {
            throw new InvalidOperationException($"CreateFontIndirect failed for '{this}'. LastError={Marshal.GetLastPInvokeError()}.");
        }

        return handle;
    }

    private static int HeightInPixels(float points)
    {
        uint dpi = PInvoke.GetDpiForSystem();
        if (dpi == 0)
        {
            dpi = 96;
        }

        return (int)Math.Round(points * dpi / 72.0);
    }

    private static void WriteFaceName(ref LOGFONTW logFont, string family)
    {
        // 结构里 lfFaceName 是定长内联数组，CsWin32 生成的类型不接受直接赋值，这里按固定偏移写。
        fixed (LOGFONTW* font = &logFont)
        {
            fixed (char* name = family)
            {
                char* faceName = (char*)((byte*)font + FaceNameOffset);
                int length = Math.Min(family.Length, 31);
                for (int index = 0; index < length; index++)
                {
                    faceName[index] = name[index];
                }

                faceName[length] = '\0';
            }
        }
    }
}
