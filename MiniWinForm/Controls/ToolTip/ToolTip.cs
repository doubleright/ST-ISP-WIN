using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public sealed class ToolTip : IDisposable
{
    private readonly Dictionary<Control, Tip> _tips = [];
    private HWND _window;
    private bool _active = true;

    public bool Active
    {
        get => _active;
        set
        {
            Application.VerifyUiThread();
            _active = value;
            if (_window != default)
            {
                _ = NativeSend.Send(_window, NativeConstants.TTM_ACTIVATE, _active ? 1u : 0u, 0);
            }
        }
    }

    public void SetToolTip(Control control, string? caption)
    {
        Application.VerifyUiThread();
        ArgumentNullException.ThrowIfNull(control);
        if (string.IsNullOrEmpty(caption))
        {
            Remove(control);
            return;
        }

        if (_tips.TryGetValue(control, out Tip? existing))
        {
            existing.Text = caption;
            if (control.IsHandleCreated)
            {
                DeleteNative(existing);
                AddNative(control, existing);
            }

            return;
        }

        Tip tip = new() { Text = caption };
        _tips.Add(control, tip);
        control.HandleCreated += OnHandleCreated;
        control.HandleDestroyed += OnHandleDestroyed;
        if (control.IsHandleCreated)
        {
            AddNative(control, tip);
        }
    }

    public void RemoveAll()
    {
        Application.VerifyUiThread();
        foreach (Control control in _tips.Keys.ToArray())
        {
            Remove(control);
        }
    }

    public void Dispose()
    {
        Application.VerifyUiThread();
        RemoveAll();
        if (_window != default)
        {
            _ = PInvoke.DestroyWindow(_window);
            _window = default;
        }
    }

    private void Remove(Control control)
    {
        if (!_tips.Remove(control, out Tip? tip))
        {
            return;
        }

        control.HandleCreated -= OnHandleCreated;
        control.HandleDestroyed -= OnHandleDestroyed;
        DeleteNative(tip);
    }

    private void OnHandleCreated(object? sender, EventArgs e)
    {
        if (sender is Control control && _tips.TryGetValue(control, out Tip? tip))
        {
            AddNative(control, tip);
        }
    }

    private void OnHandleDestroyed(object? sender, EventArgs e)
    {
        if (sender is Control control && _tips.TryGetValue(control, out Tip? tip))
        {
            DeleteNative(tip);
        }
    }

    private unsafe void EnsureWindow()
    {
        if (_window != default)
        {
            return;
        }

        fixed (char* className = "tooltips_class32")
        {
            _window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOPMOST,
                new PCWSTR(className),
                default,
                WINDOW_STYLE.WS_POPUP | (WINDOW_STYLE)NativeConstants.TTS_ALWAYSTIP | (WINDOW_STYLE)NativeConstants.TTS_NOPREFIX,
                unchecked((int)0x80000000),
                unchecked((int)0x80000000),
                unchecked((int)0x80000000),
                unchecked((int)0x80000000),
                default,
                default,
                Application.Instance != default ? Application.Instance : NativeApplication.GetModuleHandle(),
                null);
        }

        if (_window == default)
        {
            throw new InvalidOperationException($"CreateWindowEx failed for tooltip. LastError={Marshal.GetLastPInvokeError()}.");
        }

        _ = NativeSend.Send(_window, NativeConstants.TTM_ACTIVATE, _active ? 1u : 0u, 0);
        _ = NativeSend.Send(_window, NativeConstants.TTM_SETMAXTIPWIDTH, 0, 400);
    }

    private unsafe void AddNative(Control control, Tip tip)
    {
        EnsureWindow();
        DeleteNative(tip);
        tip.Tool = (nint)control.NativeHandle.Value;
        tip.Parent = control.Parent is { IsHandleCreated: true } parent ? parent.NativeHandle : control.NativeHandle;
        string text = tip.Text;
        fixed (char* textPointer = text)
        {
            NativeToolInfo info = CreateInfo(tip, (nint)textPointer);
            info.cbSize = (uint)Marshal.SizeOf<NativeToolInfo>();
            if (NativeSend.Send(_window, NativeConstants.TTM_ADDTOOLW, 0, ref info) == 0)
            {
                info.cbSize = (uint)Marshal.OffsetOf<NativeToolInfo>(nameof(NativeToolInfo.Reserved));
                if (NativeSend.Send(_window, NativeConstants.TTM_ADDTOOLW, 0, ref info) == 0)
                {
                    throw new InvalidOperationException($"TTM_ADDTOOLW failed. LastError={Marshal.GetLastPInvokeError()}.");
                }
            }

            tip.InfoSize = info.cbSize;
        }
    }

    private unsafe void DeleteNative(Tip tip)
    {
        if (_window == default || tip.Tool == 0)
        {
            return;
        }

        NativeToolInfo info = CreateInfo(tip, 0);
        info.cbSize = tip.InfoSize == 0 ? (uint)Marshal.SizeOf<NativeToolInfo>() : tip.InfoSize;
        _ = NativeSend.Send(_window, NativeConstants.TTM_DELTOOLW, 0, ref info);
        tip.Tool = 0;
    }

    private static NativeToolInfo CreateInfo(Tip tip, nint text)
    {
        return new NativeToolInfo
        {
            flags = NativeConstants.TTF_IDISHWND | NativeConstants.TTF_SUBCLASS,
            hwnd = tip.Parent,
            id = (nuint)tip.Tool,
            text = text,
        };
    }

    private sealed class Tip
    {
        public string Text = string.Empty;
        public nint Tool;
        public HWND Parent;
        public uint InfoSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeToolInfo
    {
        public uint cbSize;
        public uint flags;
        public HWND hwnd;
        public nuint id;
        public RECT rect;
        public nint instance;
        public nint text;
        public nint lParam;
        public nint Reserved;
    }
}
