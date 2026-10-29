using System.Runtime.InteropServices;

namespace STUB;

internal enum Lang
{
    Zh = 0,
    En = 1,
}

/// <summary>
/// 界面文案表。键名沿用参考实现（OneSharp/Resources/AppResources）的命名，
/// 用普通字典而不是 resx，是为了在 PublishAot + InvariantGlobalization 下不依赖卫星程序集。
/// 运行日志保持英文原文（与参考实现一致），只有界面、状态行和对话框走这张表。
/// </summary>
internal static class Texts
{
    private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal)
    {
        ["AppTitle"] = ["STM32 USART 烧录工具", "STM32 USART Flasher"],
        ["HeaderSubtitle"] = [
            "串口 ISP 烧录 · 读取板卡 / 擦除 / 写入 / 校验 / 启动",
            "USART ISP · read board, erase, write, verify, start",
        ],
        ["Language"] = ["语言", "Language"],

        ["PortSection"] = ["串口与选项", "Port & options"],
        ["FirmwareSection"] = ["固件与板卡", "Firmware & board"],
        ["LogSection"] = ["运行日志", "Activity log"],

        ["BaudRate"] = ["波特率", "Baud rate"],
        ["EntryMode"] = ["进入方式", "Entry mode"],
        ["EntryIsp"] = ["DTR 复位 · RTS 进 ISP", "Reset@DTR · ISP@RTS"],
        ["EntryKeep"] = ["已在 Bootloader", "Already in bootloader"],
        ["Verify"] = ["写入后校验", "Verify after write"],
        ["AutoSync"] = ["同步 0x7F", "Sync 0x7F"],
        ["Force"] = ["强制烧录", "Force"],
        ["RefreshPorts"] = ["刷新", "Refresh"],

        ["NoPorts"] = ["未发现串口，请检查 USB 转串口驱动后刷新。", "No COM ports found. Check the USB-UART driver, then refresh."],
        ["PortNone"] = ["未选择", "none"],
        ["PortHint"] = ["{0} · {1} · {2}", "{0} · {1} · {2}"],
        ["EntryHintIsp"] = ["自动复位进下载", "auto reset into ISP"],
        ["EntryHintKeep"] = ["需已在下载模式", "must already be in bootloader"],

        ["BrowseHex"] = ["浏览", "Browse"],
        ["FileHintEmpty"] = ["点「浏览」选择要烧录的 HEX 文件。", "Click Browse to pick the firmware HEX file."],
        ["FileHintMissing"] = ["找不到这个文件，请重新选择。", "File not found. Pick it again."],
        ["FileInfo"] = ["{0} · {1}", "{0} · {1}"],
        ["HexDialogTitle"] = ["选择固件 HEX", "Select firmware HEX"],

        ["BoardEmpty"] = [
            "尚未读取。接好串口后，先点「读取 PCB」确认型号，再烧录。",
            "Not read yet. Read PCB to confirm the board before programming.",
        ],
        ["BoardNoResponse"] = [
            "没有读到板卡。请确认串口、波特率和供电，或勾选「强制烧录」继续。",
            "No board response. Check port, baud rate and power, or tick Force to continue.",
        ],
        ["BoardNameLabel"] = ["型号", "Board"],
        ["BoardVersionLabel"] = ["版本", "Version"],
        ["BoardRawLabel"] = ["原始", "Raw"],

        ["ReadPcb"] = ["读取 PCB", "Read PCB"],
        ["Erase"] = ["擦除", "Erase"],
        ["StartIsp"] = ["开始烧录", "Start ISP"],
        ["Cancel"] = ["取消", "Cancel"],
        ["ClearLog"] = ["清空", "Clear"],

        ["StatusIdle"] = ["就绪", "Ready"],
        ["PhaseReading"] = ["正在读取板卡…", "Reading board…"],
        ["PhaseReadOk"] = ["已读到 {0}", "Found {0}"],
        ["PhaseNoBoard"] = ["没有读到板卡", "No board response"],
        ["PhaseErasing"] = ["正在擦除…", "Erasing…"],
        ["PhaseErased"] = ["擦除完成，Flash 已全片擦除", "Erased. Flash is blank."],
        ["PhaseFlashing"] = ["正在烧录…", "Programming…"],
        ["PhaseDone"] = ["烧录完成", "Complete"],
        ["PhaseCanceled"] = ["已取消", "Canceled"],
        ["PhaseFailed"] = ["失败：{0}", "Failed: {0}"],
        ["DoneTiming"] = ["用时 {0} · 完成 {1}", "elapsed {0} · finished {1}"],

        ["StepErase"] = ["正在擦除闪存…", "Erasing flash…"],
        ["StepEraseOk"] = ["擦除完成", "Erase complete"],
        ["StepWrite"] = ["正在写入…", "Writing…"],
        ["StepWriteDone"] = ["写入完成", "Write complete"],
        ["StepStart"] = ["正在启动程序…", "Starting application…"],
        ["StepAck"] = ["已连上下载器", "Bootloader connected"],
        ["StepOpen"] = ["正在打开串口…", "Opening port…"],
        ["StepChip"] = ["已识别芯片", "Chip identified"],
        ["StepRetry"] = ["这一步失败了，正在重试…", "Step failed, retrying…"],
        ["StepReset"] = ["正在复位并进入下载…", "Resetting into ISP…"],
        ["StepResetDone"] = ["复位已释放", "Reset released"],
        ["StepSync"] = ["正在同步…", "Syncing…"],

        ["DlgAlert"] = ["无法继续", "Cannot continue"],
        ["DlgAsk"] = ["请确认", "Confirm"],
        ["SelectPort"] = ["请先选择一个串口。", "Select a COM port first."],
        ["HexMissing"] = ["找不到这个 HEX 文件，请重新选择。", "HEX file not found. Pick it again."],
        ["ConfirmNoPcb"] = ["没有读到板卡应答。\r\n仍要强制烧录吗？", "No board response.\r\nForce the update anyway?"],
        ["HexMismatch"] = ["文件 {0} 和板卡 {1} 对不上，已停止。", "HEX {0} does not match board {1}."],
        ["ConfirmRevision"] = [
            "板卡是 {0}，文件是 {1}。\r\n版本不一致，仍要继续烧录吗？",
            "Board {0}, file {1}.\r\nRevision differs. Continue?",
        ],
        ["ConfirmErase"] = [
            "将整片擦除 Flash，不会写入固件，应用也无法再启动。继续？",
            "Mass-erase flash without programming. The application will not start. Continue?",
        ],
    };

    public static Lang Current { get; private set; } = Lang.Zh;

    public static bool IsEnglish => Current == Lang.En;

    public static event Action? Changed;

    public static string T(string key)
    {
        return Table.TryGetValue(key, out var value) ? value[(int)Current] : key;
    }

    /// <summary>首次启动：先读上次的选择，没有就跟随系统 UI 语言。</summary>
    public static void Initialize()
    {
        Current = UiConfig.Load() ?? SystemDefault();
    }

    public static void Set(Lang lang)
    {
        if (Current == lang)
        {
            return;
        }

        Current = lang;
        UiConfig.Save(lang);
        Changed?.Invoke();
    }

    private static Lang SystemDefault()
    {
        try
        {
            // InvariantGlobalization 打开了，CultureInfo 拿不到系统语言，直接问 Win32。
            var langId = GetUserDefaultUILanguage();
            return (langId & 0x3FF) == 0x04 ? Lang.Zh : Lang.En;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Lang.Zh;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}

/// <summary>语言选择落在 exe 同目录，和已有的 serial-flash.log 放一起，方便整个目录拷贝。</summary>
internal static class UiConfig
{
    private const string FileName = "stub-ui.cfg";

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static Lang? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var text = File.ReadAllText(FilePath).Trim();
            if (text.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                return Lang.En;
            }

            if (text.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                return Lang.Zh;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    public static void Save(Lang lang)
    {
        try
        {
            File.WriteAllText(FilePath, lang == Lang.En ? "en" : "zh");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
