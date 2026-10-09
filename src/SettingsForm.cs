using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SshTunnelLy
{
    /// <summary>全局设置对话框。</summary>
    public class SettingsForm : Form
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ssh_tunnel_ly";

        private readonly AppSettings _settings;

        private CheckBox _closeToTray;
        private CheckBox _minToTray;
        private CheckBox _logToFile;
        private CheckBox _autoRun;
        private TextBox _timeout;
        private TextBox _keepAlive;
        private TextBox _maxLog;
        private Label _error;

        private const int PadX = 18;
        private const int ClientW = 640;
        private const int RightEdge = ClientW - PadX;

        public SettingsForm(AppSettings settings)
        {
            _settings = settings;
            BuildUi();
            LoadFrom(settings);
        }

        private void BuildUi()
        {
            Text = "设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;   // 手动像素布局，禁止按字体自动缩放，否则底部按钮会被挤出窗口
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = AppIcon.Get();

            int y = 16;
            AddHead("启动行为", ref y);

            _autoRun = AddCheck("随 Windows 登录自动启动（写入当前用户 Run 注册表项，以 --min 静默启动）", PadX, ref y);

            y += 8;
            AddHead("窗口与托盘", ref y);

            _closeToTray = AddCheck("点关闭按钮时最小化到托盘（不退出程序，转发继续运行）", PadX, ref y);
            _minToTray = AddCheck("最小化窗口时隐藏到托盘", PadX, ref y);

            y += 8;
            AddHead("连接与日志", ref y);

            AddRowLabel("连接超时（秒）", PadX, y);
            _timeout = AddNumber(230, y, 90, ref y);

            AddRowLabel("心跳保活（秒，0 = 关闭）", PadX, y);
            _keepAlive = AddNumber(230, y, 90, ref y);

            AddRowLabel("日志窗口最大行数", PadX, y);
            _maxLog = AddNumber(230, y, 90, ref y);

            _logToFile = AddCheck("同时写入日志文件（超过 1MB 自动轮转）", PadX, ref y);

            y += 4;
            _error = new Label();
            _error.SetBounds(PadX, y, RightEdge - PadX, 20);
            _error.ForeColor = Color.FromArgb(190, 0, 0);
            Controls.Add(_error);
            y += 26;

            Button openDir = new Button();
            openDir.Text = "打开配置目录";
            openDir.SetBounds(PadX, y, 120, 28);
            openDir.Click += delegate { OpenPath(ConfigStore.Dir); };
            Controls.Add(openDir);

            Button ok = new Button();
            ok.Text = "确定";
            ok.SetBounds(RightEdge - 172, y, 80, 28);
            ok.Click += delegate(object s, EventArgs e) { ApplyAndClose(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.SetBounds(RightEdge - 84, y, 80, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ClientSize = new Size(ClientW, y + 40);
        }

        private void AddHead(string text, ref int y)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(PadX, y, 300, 20);
            l.ForeColor = Color.FromArgb(70, 90, 140);
            Controls.Add(l);
            y += 24;
        }

        private CheckBox AddCheck(string text, int x, ref int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.SetBounds(x, y, RightEdge - x, 22);
            Controls.Add(c);
            y += 26;
            return c;
        }

        private void AddRowLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y + 4, 200, 20);
            Controls.Add(l);
        }

        private TextBox AddNumber(int x, int y, int w, ref int yRef)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 24);
            t.TextAlign = HorizontalAlignment.Right;
            Controls.Add(t);
            yRef += 30;
            return t;
        }

        private void LoadFrom(AppSettings s)
        {
            _closeToTray.Checked = s.CloseToTray;
            _minToTray.Checked = s.MinimizeToTray;
            _logToFile.Checked = s.LogToFile;
            _timeout.Text = s.ConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            _keepAlive.Text = s.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
            _maxLog.Text = s.MaxLogLines.ToString(CultureInfo.InvariantCulture);
            _autoRun.Checked = IsAutoRunEnabled();
        }

        private static bool IsAutoRunEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunValueName);
                    return v != null;
                }
            }
            catch { return false; }
        }

        private static bool SetAutoRun(bool enable, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null)
                    {
                        error = "无法打开注册表项 HKCU\\" + RunKeyPath;
                        return false;
                    }
                    if (enable)
                        k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --min", RegistryValueKind.String);
                    else if (k.GetValue(RunValueName) != null)
                        k.DeleteValue(RunValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void ApplyAndClose()
        {
            int timeout = ParseInt(_timeout.Text, 15);
            int keepAlive = ParseInt(_keepAlive.Text, 30);
            int maxLog = ParseInt(_maxLog.Text, 3000);

            if (timeout < 3 || timeout > 600) { _error.Text = "连接超时需在 3-600 秒之间"; return; }
            if (keepAlive < 0 || keepAlive > 3600) { _error.Text = "心跳保活需在 0-3600 秒之间"; return; }
            if (maxLog < 200 || maxLog > 100000) { _error.Text = "日志行数需在 200-100000 之间"; return; }

            bool wantAutoRun = _autoRun.Checked;
            if (wantAutoRun != IsAutoRunEnabled())
            {
                string err;
                if (!SetAutoRun(wantAutoRun, out err))
                {
                    _error.Text = "设置开机启动失败：" + err;
                    return;
                }
            }

            _settings.CloseToTray = _closeToTray.Checked;
            _settings.MinimizeToTray = _minToTray.Checked;
            _settings.LogToFile = _logToFile.Checked;
            _settings.ConnectTimeoutSeconds = timeout;
            _settings.KeepAliveSeconds = keepAlive;
            _settings.MaxLogLines = maxLog;

            DialogResult = DialogResult.OK;
            Close();
        }

        private static int ParseInt(string s, int def)
        {
            int v;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        private void OpenPath(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开目录：" + ex.Message, "ssh_tunnel_ly", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
