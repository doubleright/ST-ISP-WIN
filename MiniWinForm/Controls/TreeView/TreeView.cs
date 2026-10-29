using System.Collections;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MiniWinForm;

public sealed class TreeView : Control
{
    private readonly Dictionary<nint, TreeNode> _nodesByItem = [];
    private TreeNode? _selectedNode;

    public TreeView()
        : base(tabStop: true, width: 121, height: 97)
    {
        Nodes = new TreeNodeCollection(this, null);
    }

    public TreeNodeCollection Nodes { get; }

    public TreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            Application.VerifyUiThread();
            if (value is not null && value.TreeView != this)
            {
                throw new ArgumentException("The node does not belong to this tree.", nameof(value));
            }

            if (_selectedNode == value)
            {
                return;
            }

            if (IsHandleCreated && (value is null || value.Item != 0))
            {
                _ = NativeSend.Send(NativeHandle, NativeConstants.TVM_SELECTITEM, NativeConstants.TVGN_CARET, value?.Item ?? 0);
                if (_selectedNode != value)
                {
                    _selectedNode = value;
                    AfterSelect?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            _selectedNode = value;
            AfterSelect?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? AfterSelect;

    internal override string NativeClassName => "SysTreeView32";

    internal override WINDOW_STYLE NativeStyle =>
        base.NativeStyle |
        WINDOW_STYLE.WS_VSCROLL |
        (WINDOW_STYLE)NativeConstants.TVS_HASBUTTONS |
        (WINDOW_STYLE)NativeConstants.TVS_HASLINES |
        (WINDOW_STYLE)NativeConstants.TVS_LINESATROOT |
        (WINDOW_STYLE)NativeConstants.TVS_DISABLEDRAGDROP |
        (WINDOW_STYLE)NativeConstants.TVS_SHOWSELALWAYS;

    internal override WINDOW_EX_STYLE NativeExStyle => WINDOW_EX_STYLE.WS_EX_CLIENTEDGE;

    internal override void CreateHandle()
    {
        base.CreateHandle();
        foreach (TreeNode node in Nodes)
        {
            InsertNative(node);
        }

        if (_selectedNode is { Item: not 0 })
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.TVM_SELECTITEM, NativeConstants.TVGN_CARET, _selectedNode.Item);
        }
    }

    internal override unsafe LRESULT OnNotify(uint code, LPARAM lParam)
    {
        if (unchecked((int)code) != NativeConstants.TVN_SELCHANGEDW)
        {
            return default;
        }

        NMTREEVIEWW* change = (NMTREEVIEWW*)lParam.Value;
        nint item = (nint)change->itemNew.hItem;
        TreeNode? node = item == 0 ? null : FindNode(item);
        if (_selectedNode == node)
        {
            return default;
        }

        _selectedNode = node;
        AfterSelect?.Invoke(this, EventArgs.Empty);
        return default;
    }

    internal void InsertNative(TreeNode node)
    {
        if (!IsHandleCreated || node.Item != 0 || node.Parent is { Item: 0 })
        {
            return;
        }

        node.Item = NativeTree.Insert(NativeHandle, node.Parent?.Item ?? NativeConstants.TVI_ROOT, node.Text);
        _nodesByItem[node.Item] = node;

        foreach (TreeNode child in node.Nodes)
        {
            InsertNative(child);
        }

        // 子节点插完再展开：对空节点发 TVM_EXPAND 不会留下展开状态。
        if (node.IsExpanded)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.TVM_EXPAND, NativeConstants.TVE_EXPAND, node.Item);
        }
    }

    internal void UpdateNodeText(TreeNode node)
    {
        if (IsHandleCreated && node.Item != 0)
        {
            NativeTree.SetText(NativeHandle, node.Item, node.Text);
        }
    }

    internal void ExpandNode(TreeNode node, bool expand)
    {
        if (IsHandleCreated && node.Item != 0)
        {
            _ = NativeSend.Send(
                NativeHandle,
                NativeConstants.TVM_EXPAND,
                expand ? NativeConstants.TVE_EXPAND : NativeConstants.TVE_COLLAPSE,
                node.Item);
        }
    }

    internal void RemoveNative(TreeNode node)
    {
        if (IsSelectedOrContains(node, _selectedNode))
        {
            _selectedNode = null;
        }

        if (IsHandleCreated && node.Item != 0)
        {
            _ = NativeSend.Send(NativeHandle, NativeConstants.TVM_DELETEITEM, 0, node.Item);
        }

        ForgetSubtree(node);
    }

    private TreeNode? FindNode(nint item)
    {
        return _nodesByItem.TryGetValue(item, out TreeNode? node) ? node : null;
    }

    private void ForgetSubtree(TreeNode node)
    {
        foreach (TreeNode child in node.Nodes)
        {
            ForgetSubtree(child);
        }

        if (node.Item != 0)
        {
            _nodesByItem.Remove(node.Item);
            node.Item = 0;
        }
    }

    private static bool IsSelectedOrContains(TreeNode root, TreeNode? selected)
    {
        for (TreeNode? node = selected; node is not null; node = node.Parent)
        {
            if (node == root)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class TreeNode
{
    private string _text;
    private bool _expanded;

    public TreeNode()
        : this(string.Empty)
    {
    }

    public TreeNode(string? text)
    {
        _text = text ?? string.Empty;
        Nodes = new TreeNodeCollection(null, this);
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            TreeView?.UpdateNodeText(this);
        }
    }

    public object? Tag { get; set; }

    public TreeNodeCollection Nodes { get; }

    public TreeNode? Parent { get; internal set; }

    public TreeView? TreeView { get; internal set; }

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            TreeView?.ExpandNode(this, _expanded);
        }
    }

    internal nint Item { get; set; }

    public void Expand() => IsExpanded = true;

    public void Collapse() => IsExpanded = false;

    public void Remove()
    {
        if (Parent is not null)
        {
            Parent.Nodes.Remove(this);
            return;
        }

        TreeView?.Nodes.Remove(this);
    }
}

public sealed class TreeNodeCollection : IEnumerable<TreeNode>
{
    private readonly TreeView? _tree;
    private readonly TreeNode? _owner;
    private readonly List<TreeNode> _nodes = [];

    internal TreeNodeCollection(TreeView? tree, TreeNode? owner)
    {
        _tree = tree;
        _owner = owner;
    }

    public int Count => _nodes.Count;

    public TreeNode this[int index] => _nodes[index];

    public TreeNode Add(string? text)
    {
        TreeNode node = new(text);
        Add(node);
        return node;
    }

    public int Add(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.TreeView is not null || node.Parent is not null)
        {
            throw new InvalidOperationException("The node already belongs to a tree.");
        }

        _nodes.Add(node);
        Attach(node);
        OwningTree?.InsertNative(node);
        return _nodes.Count - 1;
    }

    public void Remove(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        int index = _nodes.IndexOf(node);
        if (index < 0)
        {
            return;
        }

        RemoveAt(index);
    }

    public void RemoveAt(int index)
    {
        TreeNode node = _nodes[index];
        _nodes.RemoveAt(index);
        OwningTree?.RemoveNative(node);
        Detach(node);
    }

    public void Clear()
    {
        while (_nodes.Count > 0)
        {
            RemoveAt(_nodes.Count - 1);
        }
    }

    public IEnumerator<TreeNode> GetEnumerator() => _nodes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private TreeView? OwningTree => _tree ?? _owner?.TreeView;

    private void Attach(TreeNode node)
    {
        node.Parent = _owner;
        node.TreeView = OwningTree;
        foreach (TreeNode child in node.Nodes)
        {
            child.Parent = node;
            child.TreeView = node.TreeView;
            AttachChildren(child);
        }
    }

    private static void AttachChildren(TreeNode node)
    {
        foreach (TreeNode child in node.Nodes)
        {
            child.Parent = node;
            child.TreeView = node.TreeView;
            AttachChildren(child);
        }
    }

    private static void Detach(TreeNode node)
    {
        node.Parent = null;
        node.TreeView = null;
        node.Item = 0;
        foreach (TreeNode child in node.Nodes)
        {
            Detach(child);
        }
    }
}

internal static unsafe class NativeTree
{
    internal static nint Insert(HWND handle, nint parent, string text)
    {
        text ??= string.Empty;
        fixed (char* textPointer = text)
        {
            char* pointer = textPointer;
            char empty = '\0';
            if (text.Length == 0)
            {
                pointer = &empty;
            }

            TVINSERTSTRUCTW insert = new()
            {
                hParent = (HTREEITEM)parent,
                hInsertAfter = (HTREEITEM)NativeConstants.TVI_LAST,
            };
            TVITEMW item = new()
            {
                mask = TVITEM_MASK.TVIF_TEXT,
                pszText = pointer,
            };
            var union = insert.Anonymous;
            union.item = item;
            insert.Anonymous = union;
            nint result = NativeSend.Send(handle, NativeConstants.TVM_INSERTITEMW, 0, ref insert);
            if (result == 0)
            {
                throw new InvalidOperationException($"TVM_INSERTITEMW failed. LastError={Marshal.GetLastPInvokeError()}.");
            }

            return result;
        }
    }

    internal static void SetText(HWND handle, nint itemHandle, string text)
    {
        text ??= string.Empty;
        fixed (char* textPointer = text)
        {
            char* pointer = textPointer;
            char empty = '\0';
            if (text.Length == 0)
            {
                pointer = &empty;
            }

            TVITEMW item = new()
            {
                mask = TVITEM_MASK.TVIF_TEXT | TVITEM_MASK.TVIF_HANDLE,
                hItem = (HTREEITEM)itemHandle,
                pszText = pointer,
            };
            _ = NativeSend.Send(handle, NativeConstants.TVM_SETITEMW, 0, ref item);
        }
    }
}
