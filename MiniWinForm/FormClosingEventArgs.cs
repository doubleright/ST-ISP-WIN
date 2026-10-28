namespace MiniWinForm;

public sealed class FormClosingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}
