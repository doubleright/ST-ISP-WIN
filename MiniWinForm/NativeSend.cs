using Windows.Win32;
using Windows.Win32.Foundation;

namespace MiniWinForm;

internal static unsafe class NativeSend
{
    internal static nint Send(HWND handle, uint message, nuint wParam, nint lParam)
    {
        return PInvoke.SendMessage(handle, message, new WPARAM(wParam), new LPARAM(lParam)).Value;
    }

    internal static nint Send<T>(HWND handle, uint message, nuint wParam, ref T payload) where T : unmanaged
    {
        fixed (T* pointer = &payload)
        {
            return Send(handle, message, wParam, (nint)pointer);
        }
    }

    internal static nint SendString(HWND handle, uint message, nuint wParam, string text)
    {
        if (text.Length == 0)
        {
            char* empty = stackalloc char[1];
            return Send(handle, message, wParam, (nint)empty);
        }

        fixed (char* textPointer = text)
        {
            return Send(handle, message, wParam, (nint)textPointer);
        }
    }
}
