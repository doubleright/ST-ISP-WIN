using System.Collections;

namespace MiniWinForm;

public sealed class ControlCollection : IEnumerable<Control>
{
    private readonly Control _owner;
    private readonly List<Control> _items = [];

    internal ControlCollection(Control owner)
    {
        _owner = owner;
    }

    public int Count => _items.Count;

    public Control this[int index] => _items[index];

    public void Add(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control.Parent is not null)
        {
            throw new InvalidOperationException("The control already has a parent.");
        }

        _items.Add(control);
        bool oldEnabled = control.Enabled;
        control.Parent = _owner;
        _owner.OnControlAdded(control);
        _owner.PerformLayout();

        if (oldEnabled != control.Enabled)
        {
            control.NotifyParentEnabledChanged(EventArgs.Empty);
        }

        if (_owner.IsHandleCreated)
        {
            control.CreateHandle();
        }
    }

    public void Remove(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        int index = _items.IndexOf(control);
        if (index < 0)
        {
            return;
        }

        _items.RemoveAt(index);
        if (control.IsHandleCreated)
        {
            NativeControl.Destroy(control.NativeHandle);
        }

        control.Parent = null;
        _owner.OnControlRemoved(control);
        _owner.PerformLayout();
    }

    public IEnumerator<Control> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}