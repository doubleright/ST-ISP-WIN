using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public sealed class RichTextBox : Control
{
    private static FreeLibrarySafeHandle? _library;
    private string? _rtf;
    private bool _readOnly;
    private bool _wordWrap = true;
    private ScrollBars _scrollBars = ScrollBars.Both;

    public RichTextBox()
        : base(tabStop: true, width: 100, height: 96)
    {
    }

    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            Application.VerifyUiThread();
            _readOnly = value;
            if (IsHandleCreated)
            {
                _ = NativeSend.Send(NativeHandle, NativeConstants.EM_SETREADONLY, _readOnly ? 1u : 0u, 0);
            }
        }
    }

    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            Application.VerifyUiThread();
            if (_wordWrap == value)
            {
                return;
            }

            _wordWrap = value;
            RecreateHandle();
        }
    }

    public ScrollBars ScrollBars
    {
        get => _scrollBars;
        set
        {
            Application.VerifyUiThread();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_scrollBars == value)
            {
                return;
            }

            _scrollBars = value;
            RecreateHandle();
        }
    }

    public string Rtf
    {
        get
        {
            Application.VerifyUiThread();
            if (!IsHandleCreated)
            {
                return _rtf ?? string.Empty;
            }

            _rtf = ReadStream(NativeConstants.SF_RTF);
            return _rtf;
        }
        set
        {
            Application.VerifyUiThread();
            _rtf = value ?? string.Empty;
            if (!IsHandleCreated)
            {
                return;
            }

            if (_rtf.Length == 0)
            {
                Text = string.Empty;
                return;
            }

            WriteStream(NativeConstants.SF_RTF, _rtf);
        }
    }

    internal override string NativeClassName => "RICHEDIT50W";

    internal override WINDOW_STYLE NativeStyle
    {
        get
        {
            WINDOW_STYLE style = base.NativeStyle |
                (WINDOW_STYLE)NativeConstants.ES_MULTILINE |
                (WINDOW_STYLE)NativeConstants.ES_AUTOVSCROLL |
                (WINDOW_STYLE)NativeConstants.ES_WANTRETURN;
            if (!_wordWrap)
            {
                style |= (WINDOW_STYLE)NativeConstants.ES_AUTOHSCROLL;
            }

            if ((_scrollBars & ScrollBars.Horizontal) == ScrollBars.Horizontal && !_wordWrap)
            {
                style |= WINDOW_STYLE.WS_HSCROLL;
            }

            if ((_scrollBars & ScrollBars.Vertical) == ScrollBars.Vertical)
            {
                style |= WINDOW_STYLE.WS_VSCROLL;
            }

            return style;
        }
    }

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    internal override void CreateHandle()
    {
        EnsureLibrary();
        base.CreateHandle();
        _ = NativeSend.Send(NativeHandle, NativeConstants.EM_EXLIMITTEXT, 0, int.MaxValue);
        if (_readOnly)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.EM_SETREADONLY, 1, 0);
        }

        if (!string.IsNullOrEmpty(_rtf))
        {
            WriteStream(NativeConstants.SF_RTF, _rtf);
        }
    }

    private static void EnsureLibrary()
    {
        if (_library is not null)
        {
            return;
        }

        FreeLibrarySafeHandle library = PInvoke.LoadLibrary("msftedit.dll");
        if (library.IsInvalid)
        {
            library.Dispose();
            throw new InvalidOperationException($"LoadLibrary failed for msftedit.dll. LastError={Marshal.GetLastPInvokeError()}.");
        }

        _library = library;
    }

    private unsafe void WriteStream(uint format, string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        StreamBuffer state = new() { Bytes = bytes };
        GCHandle handle = GCHandle.Alloc(state);
        try
        {
            NativeEditStream stream = new()
            {
                Cookie = (nuint)GCHandle.ToIntPtr(handle),
                Callback = (nint)(delegate* unmanaged[Stdcall]<nuint, byte*, int, int*, uint>)&StreamCallback,
            };
            nint written = NativeSend.Send(NativeHandle, NativeConstants.EM_STREAMIN, format, ref stream);
            if (written <= 0)
            {
                throw new InvalidOperationException($"EM_STREAMIN failed. Error={stream.Error}.");
            }
        }
        finally
        {
            handle.Free();
        }
    }

    private unsafe string ReadStream(uint format)
    {
        StreamBuffer state = new() { Collecting = true };
        GCHandle handle = GCHandle.Alloc(state);
        try
        {
            NativeEditStream stream = new()
            {
                Cookie = (nuint)GCHandle.ToIntPtr(handle),
                Callback = (nint)(delegate* unmanaged[Stdcall]<nuint, byte*, int, int*, uint>)&StreamCallback,
            };
            if (NativeSend.Send(NativeHandle, NativeConstants.EM_STREAMOUT, format, ref stream) < 0)
            {
                throw new InvalidOperationException($"EM_STREAMOUT failed. Error={stream.Error}.");
            }
            return Encoding.Latin1.GetString(state.Bytes);
        }
        finally
        {
            handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe uint StreamCallback(nuint cookie, byte* buffer, int length, int* transferred)
    {
        *transferred = 0;
        try
        {
            StreamBuffer state = (StreamBuffer)GCHandle.FromIntPtr((nint)cookie).Target!;
            if (state.Collecting)
            {
                int offset = state.Bytes.Length;
                byte[] grown = new byte[offset + length];
                state.Bytes.AsSpan().CopyTo(grown);
                new ReadOnlySpan<byte>(buffer, length).CopyTo(grown.AsSpan(offset));
                state.Bytes = grown;
                *transferred = length;
                return 0;
            }

            int available = state.Bytes.Length - state.Position;
            int count = Math.Min(length, Math.Max(available, 0));
            if (count > 0)
            {
                state.Bytes.AsSpan(state.Position, count).CopyTo(new Span<byte>(buffer, count));
                state.Position += count;
            }

            *transferred = count;
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct NativeEditStream
    {
        public nuint Cookie;
        public uint Error;
        public nint Callback;
    }

    private sealed class StreamBuffer
    {
        public byte[] Bytes = [];
        public int Position;
        public bool Collecting;
    }
}
