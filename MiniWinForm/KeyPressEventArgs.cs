namespace MiniWinForm;

public sealed class KeyPressEventArgs : EventArgs
{
    public KeyPressEventArgs(char keyChar)
    {
        KeyChar = keyChar;
    }

    public char KeyChar { get; }

    public bool Handled { get; set; }
}
