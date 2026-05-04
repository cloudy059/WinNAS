using WinNASClient.Config;
using WinNASClient.Database;
using WinNASClient.Services;
using WinNASClient.Utils;

namespace WinNASClient;

public class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly HttpServer.WebServer? _webServer;
    private readonly SystemService? _systemService;
    private NumericUpDown _portNumeric = null!;
    private CheckBox _allowRegisterCheck = null!;
    private CheckBox _allowGuestCheck = null!;
    private CheckBox _duplicateCheck = null!;
    private CheckBox _autoStartCheck = null!;
    private TextBox _maxUploadTextBox = null!;
    private Button _saveButton = null!;
    private Button _cancelButton = null!;
    private Button _resetButton = null!;
    private ComboBox _networkCombo = null!;

    public SettingsForm(AppConfig config, HttpServer.WebServer? webServer, SystemService? systemService = null)
    {
        _config = config;
        _webServer = webServer;
        _systemService = systemService;
        InitializeSettingsForm();
    }

    private void InitializeSettingsForm()
    {
        this.Text = "WinNAS 系统设置";
        this.Size = new Size(420, 260);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15),
            ColumnCount = 2,
            RowCount = 7
        };

        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;

        mainPanel.Controls.Add(new Label { Text = "HTTP端口:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _portNumeric = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = _config.HttpPort, Dock = DockStyle.Fill };
        mainPanel.Controls.Add(_portNumeric, 1, row++);

        mainPanel.Controls.Add(new Label { Text = "绑定网卡:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _networkCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        var networks = NetworkHelper.GetNetworkInterfaces();
        foreach (var net in networks)
            _networkCombo.Items.Add($"{net.Name} ({net.IPAddress})");
        if (_networkCombo.Items.Count > 0) _networkCombo.SelectedIndex = 0;
        mainPanel.Controls.Add(_networkCombo, 1, row++);

        var switchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true
        };
        switchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        switchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _allowRegisterCheck = new CheckBox { Text = "允许注册", Checked = _config.AllowRegister, Dock = DockStyle.Fill, AutoSize = true };
        _allowGuestCheck = new CheckBox { Text = "访客模式", Checked = _config.AllowGuest, Dock = DockStyle.Fill, AutoSize = true };
        _duplicateCheck = new CheckBox { Text = "重复文件检查", Checked = _config.DuplicateCheck, Dock = DockStyle.Fill, AutoSize = true };
        _autoStartCheck = new CheckBox { Text = "开机自启", Checked = _config.AutoStart, Dock = DockStyle.Fill, AutoSize = true };

        switchPanel.Controls.Add(_allowRegisterCheck, 0, 0);
        switchPanel.Controls.Add(_allowGuestCheck, 1, 0);
        switchPanel.Controls.Add(_duplicateCheck, 0, 1);
        switchPanel.Controls.Add(_autoStartCheck, 1, 1);

        mainPanel.Controls.Add(new Label { Text = "开关设置:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        mainPanel.Controls.Add(switchPanel, 1, row++);

        mainPanel.Controls.Add(new Label { Text = "最大上传(MB):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        _maxUploadTextBox = new TextBox { Text = (_config.MaxUploadSize / 1024 / 1024).ToString(), Dock = DockStyle.Fill };
        mainPanel.Controls.Add(_maxUploadTextBox, 1, row++);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 45,
            Padding = new Padding(8)
        };

        _cancelButton = new Button { Text = "取消", Size = new Size(75, 30) };
        _cancelButton.Click += (s, e) => this.Close();
        buttonPanel.Controls.Add(_cancelButton);

        _saveButton = new Button { Text = "保存", Size = new Size(75, 30) };
        _saveButton.Click += OnSave;
        buttonPanel.Controls.Add(_saveButton);

        _resetButton = new Button { Text = "初始化", Size = new Size(75, 30), BackColor = Color.FromArgb(245, 108, 108), ForeColor = Color.White };
        _resetButton.Click += OnReset;
        buttonPanel.Controls.Add(_resetButton);

        this.Controls.Add(mainPanel);
        this.Controls.Add(buttonPanel);
    }

    private async void OnReset(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "此操作将清除所有数据并恢复默认设置，管理员密码将重置为 admin。\n此操作不可逆！\n\n确认执行初始化？",
            "系统初始化确认",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        var inputResult = MessageBox.Show(
            "再次确认：这将删除所有用户、文件记录、分享、团队、聊天记录等数据！\n\n点击\"是\"执行初始化，点击\"否\"取消。",
            "二次确认",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Stop);

        if (inputResult != DialogResult.Yes) return;

        if (_systemService != null)
        {
            try
            {
                await _systemService.ResetSystemAsync();
                MessageBox.Show("系统已初始化，管理员密码已重置为 admin。请重新登录。", "WinNAS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}", "WinNAS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            MessageBox.Show("系统服务未就绪，无法执行初始化。", "WinNAS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void OnSave(object? sender, EventArgs e)
    {
        _config.HttpPort = (int)_portNumeric.Value;
        _config.AllowRegister = _allowRegisterCheck.Checked;
        _config.AllowGuest = _allowGuestCheck.Checked;
        _config.DuplicateCheck = _duplicateCheck.Checked;
        _config.AutoStart = _autoStartCheck.Checked;

        if (long.TryParse(_maxUploadTextBox.Text, out long maxMB))
            _config.MaxUploadSize = maxMB * 1024 * 1024;

        _config.Save();

        if (_systemService != null)
        {
            try
            {
                await _systemService.SetConfigAsync("http_port", _config.HttpPort.ToString());
                await _systemService.SetConfigAsync("allow_register", _config.AllowRegister ? "true" : "false");
                await _systemService.SetConfigAsync("allow_guest", _config.AllowGuest ? "true" : "false");
                await _systemService.SetConfigAsync("duplicate_check", _config.DuplicateCheck ? "true" : "false");
                await _systemService.SetConfigAsync("max_upload_size", _config.MaxUploadSize.ToString());
                await _systemService.SetAutoStartAsync(_autoStartCheck.Checked);
            }
            catch { }
        }
        else
        {
            if (_autoStartCheck.Checked)
            {
                try
                {
                    var appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(appPath))
                    {
                        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                        key?.SetValue("WinNAS", $"\"{appPath}\"");
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    key?.DeleteValue("WinNAS", false);
                }
                catch { }
            }
        }

        MessageBox.Show("设置已保存，部分设置需要重启后生效。", "WinNAS", MessageBoxButtons.OK, MessageBoxIcon.Information);
        this.Close();
    }
}
