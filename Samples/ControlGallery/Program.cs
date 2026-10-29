using System.Runtime.InteropServices;
using MiniWinForm;

namespace ControlGallery;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();

        // --uitest <文件>：数值控件的方向/步进交互抽查（--udtest 为旧名，等价）。
        if (args.Length >= 2 && (args[0] == "--uitest" || args[0] == "--udtest"))
        {
            SelfCheck.ProbeInteraction(args[1]);
            return;
        }

        // --cbtest <文件>：下拉选择是否立刻生效。
        if (args.Length >= 2 && args[0] == "--cbtest")
        {
            SelfCheck.ProbeComboBox(args[1]);
            return;
        }

        var form = new GalleryForm();

        // --shots <目录>：逐页截图 + 控件体检后自动退出，用于回归验证。
        if (args.Length >= 2 && args[0] == "--shots")
        {
            SelfCheck.Attach(form, form.TabHost, args[1]);
        }

        Application.Run(form);
    }

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForSystem();
}
