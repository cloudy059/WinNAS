using System.Text.Json;

namespace WinNASClient.Config;

public class AppConfig
{
    public string DataPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinNAS");
    public string DbPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public int HttpPort { get; set; } = 8080;
    public string JwtSecret { get; set; } = "";
    public int JwtExpireHours { get; set; } = 24;
    public bool AllowRegister { get; set; } = true;
    public bool AllowGuest { get; set; } = true;
    public long MaxUploadSize { get; set; } = 1073741824;
    public bool DuplicateCheck { get; set; } = true;
    public bool AutoStart { get; set; } = false;

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WinNAS", "config.json");

    public static AppConfig Load()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
        return new AppConfig();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public void EnsureDirectories()
    {
        if (!Directory.Exists(DataPath))
            Directory.CreateDirectory(DataPath);

        DbPath = Path.Combine(DataPath, "winnas.db");

        var exeDir = GetExeDirectory();
        var localWwwroot = Path.Combine(exeDir, "wwwroot");
        if (Directory.Exists(localWwwroot) && Directory.GetFiles(localWwwroot, "*", SearchOption.AllDirectories).Length > 0)
        {
            WebRootPath = localWwwroot;
        }
        else
        {
            WebRootPath = Path.Combine(DataPath, "wwwroot");
            if (!Directory.Exists(WebRootPath))
                Directory.CreateDirectory(WebRootPath);

            if (Directory.GetFiles(WebRootPath, "*", SearchOption.AllDirectories).Length == 0)
            {
                try
                {
                    if (Directory.Exists(localWwwroot) && Directory.GetFiles(localWwwroot, "*", SearchOption.AllDirectories).Length > 0)
                    {
                        CopyDirectory(localWwwroot, WebRootPath);
                    }
                }
                catch { }
            }
        }
    }

    private static string GetExeDirectory()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
                return Path.GetDirectoryName(exePath)!;
        }
        catch { }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }
}
