namespace STUB;

/// <summary>
/// 上次使用的烧录设置。落在 exe 同目录的 stub-settings.cfg（key=value 纯文本），
/// 跟 stub-ui.cfg 放一起，整个目录拷贝就能带走。文件坏了/没权限都不抛，退回默认值。
/// </summary>
internal sealed class AppSettings
{
    private const string FileName = "stub-settings.cfg";

    public string Hex { get; set; } = string.Empty;

    public string Port { get; set; } = string.Empty;

    public int Baud { get; set; } = 115200;

    public bool KeepInBootloader { get; set; }

    public bool Verify { get; set; } = true;

    public bool Sync { get; set; } = true;

    public bool Force { get; set; }

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (!File.Exists(FilePath))
            {
                return settings;
            }

            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var split = line.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                var key = line[..split].Trim();
                var value = line[(split + 1)..].Trim();
                switch (key.ToLowerInvariant())
                {
                    case "hex":
                        settings.Hex = value;
                        break;
                    case "port":
                        settings.Port = value;
                        break;
                    case "baud":
                        settings.Baud = ParseBaud(value, settings.Baud);
                        break;
                    case "entry":
                        settings.KeepInBootloader = value.Equals("keep", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "verify":
                        settings.Verify = ParseBool(value, settings.Verify);
                        break;
                    case "sync":
                        settings.Sync = ParseBool(value, settings.Sync);
                        break;
                    case "force":
                        settings.Force = ParseBool(value, settings.Force);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            File.WriteAllLines(FilePath,
            [
                "# STM32 USART Flasher - last used settings",
                "hex=" + Hex,
                "port=" + Port,
                "baud=" + Baud,
                "entry=" + (KeepInBootloader ? "keep" : "isp"),
                "verify=" + (Verify ? "1" : "0"),
                "sync=" + (Sync ? "1" : "0"),
                "force=" + (Force ? "1" : "0"),
            ]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>只认 9600 / 115200，其它值当作没写过，避免手改文件改出无效波特率。</summary>
    private static int ParseBaud(string value, int fallback)
    {
        return value switch
        {
            "9600" => 9600,
            "115200" => 115200,
            _ => fallback,
        };
    }

    private static bool ParseBool(string value, bool fallback)
    {
        return value switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" => false,
            _ => fallback,
        };
    }
}
