using WinNASClient.Config;
using WinNASClient.Database;
using WinNASClient.HttpServer;
using WinNASClient.Services;
using WinNASClient.Utils;
using Serilog;
using Dapper;

namespace WinNASClient;

public class MainForm : Form
{
    private NotifyIcon _notifyIcon = null!;
    private ContextMenuStrip _contextMenu = null!;
    private WebServer? _webServer;
    private AppConfig _config = null!;
    private System.Threading.Timer? _trafficTimer;
    private System.Threading.Timer? _restartCheckTimer;
    private Icon? _appIcon;

    public MainForm()
    {
        LoadAppIcon();
        InitializeComponents();
        InitializeTrayIcon();
        LoadConfiguration();
    }

    private void LoadAppIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinNAS.ico");
            if (!File.Exists(iconPath))
                iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winnas.ico");
            if (!File.Exists(iconPath))
                IconGenerator.SaveIcon(iconPath);
            _appIcon = new Icon(iconPath);
            this.Icon = _appIcon;
        }
        catch
        {
            _appIcon = SystemIcons.Application;
            this.Icon = _appIcon;
        }
    }

    private void InitializeComponents()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.Size = new Size(0, 0);
        this.Opacity = 0;
        this.WindowState = FormWindowState.Minimized;
    }

    private void InitializeTrayIcon()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("打开管理页面", null, OnOpenWeb);
        _contextMenu.Items.Add("-");
        _contextMenu.Items.Add("网络信息", null, OnShowNetworkInfo);
        _contextMenu.Items.Add("系统设置", null, OnShowSettings);
        _contextMenu.Items.Add("重启服务", null, OnRestart);
        _contextMenu.Items.Add("-");
        _contextMenu.Items.Add("退出", null, OnExit);

        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Application,
            Text = "WinNAS v1.7.56 - 局域网共享系统",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += OnOpenWeb;
    }

    private async void LoadConfiguration()
    {
        _config = AppConfig.Load();
        _config.EnsureDirectories();
        _config.Save();

        var dbInitializer = new DbInitializer(_config.DbPath);
        await dbInitializer.InitializeAsync();

        var repo = new DbRepository(dbInitializer);

        var jwtSecret = await repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = 'jwt_secret'");
        if (!string.IsNullOrEmpty(jwtSecret))
            _config.JwtSecret = jwtSecret;

        var portStr = await repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = 'http_port'");
        if (int.TryParse(portStr, out int port))
            _config.HttpPort = port;

        var allowReg = await repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = 'allow_register'");
        _config.AllowRegister = allowReg?.ToLower() != "false";

        var allowGuest = await repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = 'allow_guest'");
        _config.AllowGuest = allowGuest?.ToLower() != "false";

        var dupCheck = await repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = 'duplicate_check'");
        _config.DuplicateCheck = dupCheck?.ToLower() != "false";

        var maxUpload = await repo.QueryFirstOrDefaultAsync<string>(
            "SELECT Value FROM SystemConfigs WHERE Key = 'max_upload_size'");
        if (long.TryParse(maxUpload, out long maxSize))
            _config.MaxUploadSize = maxSize;

        StartWebServer();
        StartTrafficMonitor();
        StartRestartCheck();

        _notifyIcon.ShowBalloonTip(3000, "WinNAS", $"服务已启动，访问 http://{NetworkHelper.GetLocalIpAddress()}:{_config.HttpPort}", ToolTipIcon.Info);
    }

    private async void StartWebServer()
    {
        try
        {
            _webServer = new WebServer(_config);
            await _webServer.StartAsync();
            Log.Information("Web server started on port {Port}", _config.HttpPort);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start web server");
            MessageBox.Show($"启动Web服务失败: {ex.Message}", "WinNAS 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartTrafficMonitor()
    {
        _trafficTimer = new System.Threading.Timer(async _ =>
        {
            try
            {
                if (_webServer != null)
                {
                    await _webServer.System.RecordTrafficAsync(0, 0);
                }
            }
            catch { }
        }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    private void StartRestartCheck()
    {
        _restartCheckTimer = new System.Threading.Timer(async _ =>
        {
            if (SystemService.IsRestartRequested)
            {
                SystemService.ClearRestartRequest();
                await RestartWebServerAsync();
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    private async Task RestartWebServerAsync()
    {
        try
        {
            Log.Information("Restarting web server...");

            if (_webServer != null)
            {
                await _webServer.StopAsync();
                _webServer = null;
            }

            _config = AppConfig.Load();
            _config.EnsureDirectories();
            _config.Save();

            var dbInitializer = new DbInitializer(_config.DbPath);
            await dbInitializer.InitializeAsync();

            var repo = new DbRepository(dbInitializer);

            var jwtSecret = await repo.QueryFirstOrDefaultAsync<string>(
                "SELECT Value FROM SystemConfigs WHERE Key = 'jwt_secret'");
            if (!string.IsNullOrEmpty(jwtSecret))
                _config.JwtSecret = jwtSecret;

            var portStr = await repo.QueryFirstOrDefaultAsync<string>(
                "SELECT Value FROM SystemConfigs WHERE Key = 'http_port'");
            if (int.TryParse(portStr, out int port))
                _config.HttpPort = port;

            var allowReg = await repo.QueryFirstOrDefaultAsync<string>(
                "SELECT Value FROM SystemConfigs WHERE Key = 'allow_register'");
            _config.AllowRegister = allowReg?.ToLower() != "false";

            var allowGuest = await repo.QueryFirstOrDefaultAsync<string>(
                "SELECT Value FROM SystemConfigs WHERE Key = 'allow_guest'");
            _config.AllowGuest = allowGuest?.ToLower() != "false";

            var dupCheck = await repo.QueryFirstOrDefaultAsync<string>(
                "SELECT Value FROM SystemConfigs WHERE Key = 'duplicate_check'");
            _config.DuplicateCheck = dupCheck?.ToLower() != "false";

            var maxUpload = await repo.QueryFirstOrDefaultAsync<string>(
                "SELECT Value FROM SystemConfigs WHERE Key = 'max_upload_size'");
            if (long.TryParse(maxUpload, out long maxSize))
                _config.MaxUploadSize = maxSize;

            var autoStart = await repo.QueryFirstOrDefaultAsync<string>(
                "SELECT Value FROM SystemConfigs WHERE Key = 'auto_start'");
            _config.AutoStart = autoStart?.ToLower() == "true";

            StartWebServer();

            _notifyIcon.ShowBalloonTip(3000, "WinNAS", $"服务已重启，访问 http://{NetworkHelper.GetLocalIpAddress()}:{_config.HttpPort}", ToolTipIcon.Info);

            Log.Information("Web server restarted on port {Port}", _config.HttpPort);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restart web server");
        }
    }

    private void OnOpenWeb(object? sender, EventArgs e)
    {
        var url = $"http://{NetworkHelper.GetLocalIpAddress()}:{_config.HttpPort}";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开浏览器: {ex.Message}", "WinNAS", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnShowNetworkInfo(object? sender, EventArgs e)
    {
        var networks = NetworkHelper.GetNetworkInterfaces();
        var info = string.Join("\n", networks.Select(n =>
            $"{n.Name}: {n.IPAddress}"));

        MessageBox.Show($"本机网络信息:\n\n{info}\n\n访问地址: http://{NetworkHelper.GetLocalIpAddress()}:{_config.HttpPort}",
            "WinNAS 网络信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnShowSettings(object? sender, EventArgs e)
    {
        var systemService = _webServer?.System;
        using var settingsForm = new SettingsForm(_config, _webServer, systemService);
        settingsForm.ShowDialog();
    }

    private void OnRestart(object? sender, EventArgs e)
    {
        var result = MessageBox.Show("确定要重启 WinNAS 服务吗？期间服务会短暂中断。",
            "WinNAS", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            SystemService.RequestRestart();
        }
    }

    private async void OnExit(object? sender, EventArgs e)
    {
        var result = MessageBox.Show("确定要退出 WinNAS 吗？退出后局域网共享服务将停止。",
            "WinNAS", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            try
            {
                if (_webServer != null)
                    await _webServer.StopAsync();
            }
            catch { }

            _trafficTimer?.Dispose();
            _notifyIcon.Visible = false;
            Environment.Exit(0);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        e.Cancel = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _appIcon?.Dispose();
            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();
            _trafficTimer?.Dispose();
            _restartCheckTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
