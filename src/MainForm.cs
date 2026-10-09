using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SshTunnelLy
{
    /// <summary>取 exe 自带图标，避免使用任何运行时资源释放。</summary>
    public static class AppIcon
    {
        private static Icon _icon;

        public static Icon Get()
        {
            if (_icon == null)
            {
                try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch { }
                if (_icon == null) _icon = SystemIcons.Application;
            }
            return _icon;
        }
    }

    public class MainForm : Form
    {
        private readonly bool _startMinimized;
        private AppConfig _config;
        private readonly List<TunnelRunner> _runners = new List<TunnelRunner>();

        private ToolStrip _tool;
        private SplitContainer _split;
        private ListView _list;
        private TextBox _logBox;
        private StatusStrip _status;
        private ToolStripStatusLabel _statusTunnels;
        private ToolStripStatusLabel _statusConfig;
        private NotifyIcon _tray;
        private Timer _timer;
        private bool _exiting;
        private bool _trayTipShown;

        private ToolStripButton _btnEdit, _btnDelete, _btnStart, _btnStop, _btnStartAll, _btnStopAll;

        public MainForm(bool startMinimized)
        {
            _startMinimized = startMinimized;
            _current = this;
            BuildUi();

            string error;
            _config = ConfigStore.Load(out error);
            Log.Configure(_config.Settings.MaxLogLines, _config.Settings.LogToFile);
            Log.LineAdded += OnLogLine;
            foreach (string line in Log.Snapshot()) _logBox.AppendText(line + Environment.NewLine);

            if (error != null)
            {
                Log.Error("读取配置失败：" + error);
                MessageBox.Show("读取配置失败：" + error + "\r\n\r\n将使用默认配置。", "ssh_tunnel_ly",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Log.Info("ssh_tunnel_ly 已启动，配置文件：" + ConfigStore.ConfigPath);
            SyncRunners();
            RefreshAllRows();
        }

        private static MainForm _current;

        /// <summary>被 Program 的命名事件监听线程调用：把窗口从托盘唤回前台。</summary>
        public static void RequestShow()
        {
            MainForm f = _current;
            if (f == null || f.IsDisposed) return;
            try
            {
                if (f.IsHandleCreated) f.BeginInvoke(new Action(f.RestoreFromTray));
            }
            catch { }
        }

        // ================================================================ 界面

        private void BuildUi()
        {
            Text = "ssh_tunnel_ly  -  SSH 端口转发";
            Width = 1040;
            Height = 680;
            MinimumSize = new Size(860, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Get();
            Font = new Font("Microsoft YaHei UI", 9f);

            _tool = new ToolStrip();
            _tool.GripStyle = ToolStripGripStyle.Hidden;
            _tool.ImageScalingSize = new Size(16, 16);
            _tool.Items.Add(MakeButton("新建隧道", delegate { NewTunnel(); }));
            _btnEdit = MakeButton("编辑", delegate { EditTunnel(); });
            _tool.Items.Add(_btnEdit);
            _btnDelete = MakeButton("删除", delegate { DeleteTunnel(); });
            _tool.Items.Add(_btnDelete);
            _tool.Items.Add(new ToolStripSeparator());
            _btnStart = MakeButton("启动", delegate { ToggleSelected(true); });
            _tool.Items.Add(_btnStart);
            _btnStop = MakeButton("停止", delegate { ToggleSelected(false); });
            _tool.Items.Add(_btnStop);
            _tool.Items.Add(new ToolStripSeparator());
            _btnStartAll = MakeButton("全部启动", delegate { StartAll(); });
            _tool.Items.Add(_btnStartAll);
            _btnStopAll = MakeButton("全部停止", delegate { StopAll(); });
            _tool.Items.Add(_btnStopAll);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(MakeButton("设置", delegate { OpenSettings(); }));
            _tool.Items.Add(MakeButton("打开配置目录", delegate { OpenFolder(ConfigStore.Dir); }));

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.MultiSelect = true;
            _list.HideSelection = false;
            _list.Columns.Add("名称", 140);
            _list.Columns.Add("方向", 78);
            _list.Columns.Add("监听", 165);
            _list.Columns.Add("目标地址", 150);
            _list.Columns.Add("SSH 服务器", 180);
            _list.Columns.Add("状态", 118);
            _list.Columns.Add("备注 / 最后消息", 250);
            _list.DoubleClick += delegate { ToggleSelected(null); };
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { ToggleSelected(null); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteTunnel(); e.Handled = true; }
            };

            ContextMenuStrip listMenu = new ContextMenuStrip();
            listMenu.Items.Add("启动", null, delegate { ToggleSelected(true); });
            listMenu.Items.Add("停止", null, delegate { ToggleSelected(false); });
            listMenu.Items.Add(new ToolStripSeparator());
            listMenu.Items.Add("编辑…", null, delegate { EditTunnel(); });
            listMenu.Items.Add("复制等效 ssh 命令", null, delegate { CopySshCommand(); });
            listMenu.Items.Add(new ToolStripSeparator());
            listMenu.Items.Add("删除", null, delegate { DeleteTunnel(); });
            _list.ContextMenuStrip = listMenu;

            _logBox = new TextBox();
            _logBox.Dock = DockStyle.Fill;
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Both;
            _logBox.WordWrap = false;
            _logBox.BackColor = Color.FromArgb(24, 24, 28);
            _logBox.ForeColor = Color.Gainsboro;
            _logBox.Font = new Font("Consolas", 9f);
            _logBox.BorderStyle = BorderStyle.None;

            ContextMenuStrip logMenu = new ContextMenuStrip();
            logMenu.Items.Add("清空日志窗口", null, delegate { _logBox.Clear(); });
            logMenu.Items.Add("复制全部", null, delegate { try { Clipboard.SetText(_logBox.Text); } catch { } });
            logMenu.Items.Add("打开日志文件", null, delegate { OpenFolder(ConfigStore.LogDir); });
            logMenu.Items.Add("打开配置目录", null, delegate { OpenFolder(ConfigStore.Dir); });
            _logBox.ContextMenuStrip = logMenu;

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.SplitterWidth = 6;
            _split.Panel1MinSize = 160;
            _split.Panel2MinSize = 100;
            _split.Panel1.Controls.Add(_list);
            _split.Panel2.Controls.Add(_logBox);
            _split.Panel2.Controls.Add(MakeLogHeader());

            _status = new StatusStrip();
            _statusTunnels = new ToolStripStatusLabel("运行中 0 / 0");
            _statusConfig = new ToolStripStatusLabel("");
            _statusConfig.Spring = true;
            _statusConfig.TextAlign = ContentAlignment.MiddleLeft;
            _status.Items.Add(_statusTunnels);
            _status.Items.Add(_statusConfig);

            Controls.Add(_split);
            Controls.Add(_tool);
            Controls.Add(_status);

            _tray = new NotifyIcon();
            _tray.Icon = AppIcon.Get();
            _tray.Text = "ssh_tunnel_ly";
            _tray.Visible = true;
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示主窗口", null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("全部启动", null, delegate { StartAll(); });
            trayMenu.Items.Add("全部停止", null, delegate { StopAll(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出", null, delegate { ExitApp(); });
            _tray.ContextMenuStrip = trayMenu;
            _tray.DoubleClick += delegate { RestoreFromTray(); };

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { RefreshAllRows(); };
            _timer.Start();

            Load += delegate
            {
                _split.SplitterDistance = Math.Max(_split.Panel1MinSize, (int)(_split.Height * 0.62));
            };
            Shown += OnShown;
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && _config != null && _config.Settings.MinimizeToTray)
                    HideToTray();
            };
            FormClosing += OnFormClosing;
        }

        private Control MakeLogHeader()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = 22;
            p.BackColor = Color.FromArgb(45, 45, 52);
            Label l = new Label();
            l.Text = "  运行日志（右键可清空 / 复制 / 打开日志文件）";
            l.Dock = DockStyle.Fill;
            l.ForeColor = Color.Gainsboro;
            l.TextAlign = ContentAlignment.MiddleLeft;
            p.Controls.Add(l);
            return p;
        }

        private static ToolStripButton MakeButton(string text, EventHandler handler)
        {
            ToolStripButton b = new ToolStripButton(text);
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.AutoSize = true;
            b.Click += handler;
            return b;
        }

        // ================================================================ 配置与运行器

        private void SyncRunners()
        {
            for (int i = _runners.Count - 1; i >= 0; i--)
            {
                bool alive = false;
                foreach (TunnelConfig t in _config.Tunnels)
                    if (t.Id == _runners[i].Config.Id) { alive = true; break; }
                if (!alive)
                {
                    _runners[i].Dispose();
                    _runners.RemoveAt(i);
                }
            }
            foreach (TunnelConfig t in _config.Tunnels)
            {
                bool exists = false;
                foreach (TunnelRunner r in _runners)
                    if (r.Config.Id == t.Id) { exists = true; r.UpdateSettings(_config.Settings); break; }
                if (!exists)
                {
                    TunnelRunner runner = new TunnelRunner(t, _config.Settings);
                    runner.Changed += OnRunnerChanged;
                    _runners.Add(runner);
                }
            }
            RefreshList();
        }

        private void RefreshList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (TunnelConfig t in _config.Tunnels)
            {
                TunnelRunner runner = FindRunner(t.Id);
                ListViewItem item = new ListViewItem(t.Name);
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(t.DirectionText);
                item.SubItems.Add(t.ListenDisplay);
                item.SubItems.Add(t.DestDisplay);
                item.SubItems.Add(t.SshDisplay);
                item.SubItems.Add(runner != null ? runner.StateText : "已停止");
                item.SubItems.Add(runner != null ? runner.LastError : "");
                if (runner != null) item.Tag = runner;
                _list.Items.Add(item);
                if (runner != null) PaintState(item, runner);
            }
            _list.EndUpdate();
            UpdateButtons();
        }

        private TunnelRunner FindRunner(string id)
        {
            foreach (TunnelRunner r in _runners) if (r.Config.Id == id) return r;
            return null;
        }

        private void RefreshAllRows()
        {
            if (_list.Items.Count != _config.Tunnels.Count) { RefreshList(); return; }
            int running = 0;
            for (int i = 0; i < _config.Tunnels.Count; i++)
            {
                TunnelRunner runner = FindRunner(_config.Tunnels[i].Id);
                if (runner == null) continue;
                ListViewItem item = _list.Items[i];
                item.SubItems[5].Text = runner.StateText;
                item.SubItems[6].Text = runner.LastError;
                PaintState(item, runner);
                if (runner.State == TunnelState.Running) running++;
            }
            _statusTunnels.Text = string.Format(CultureInfo.InvariantCulture, "运行中 {0} / 共 {1} 条隧道", running, _config.Tunnels.Count);
            _statusConfig.Text = "配置：" + ConfigStore.ConfigPath;
        }

        private static void PaintState(ListViewItem item, TunnelRunner runner)
        {
            Color c;
            switch (runner.State)
            {
                case TunnelState.Running: c = Color.FromArgb(0, 130, 0); break;
                case TunnelState.Connecting:
                case TunnelState.Reconnecting: c = Color.FromArgb(200, 120, 0); break;
                case TunnelState.Error: c = Color.FromArgb(190, 0, 0); break;
                default: c = Color.DimGray; break;
            }
            item.SubItems[5].ForeColor = c;
            item.SubItems[6].ForeColor = runner.State == TunnelState.Error ? Color.FromArgb(190, 0, 0) : Color.DimGray;
        }

        private void OnRunnerChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) { BeginInvoke(new EventHandler(OnRunnerChanged), sender, e); return; }
                RefreshAllRows();
            }
            catch { }
        }

        private void OnLogLine(string line)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action<string>(OnLogLine), line); return; }
                if (_logBox.Lines.Length > _config.Settings.MaxLogLines)
                {
                    List<string> keep = new List<string>(_logBox.Lines);
                    keep.RemoveRange(0, keep.Count - _config.Settings.MaxLogLines / 2);
                    _logBox.Lines = keep.ToArray();
                }
                _logBox.AppendText(line + Environment.NewLine);
            }
            catch { }
        }

        private void SaveConfig()
        {
            string error;
            ConfigStore.Save(_config, out error);
            if (error != null)
            {
                Log.Error("保存配置失败：" + error);
                MessageBox.Show("保存配置失败：" + error, "ssh_tunnel_ly", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================================================================ 动作

        private void OnShown(object sender, EventArgs e)
        {
            if (_startMinimized) HideToTray();

            // 只自动连接在条目里勾选了"开机自启"的隧道（默认都不勾）。
            foreach (TunnelConfig t in _config.Tunnels)
            {
                if (!t.AutoStart) continue;
                TunnelRunner r = FindRunner(t.Id);
                if (r != null) StartRunner(r);
            }
        }

        private TunnelRunner SelectedRunner()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as TunnelRunner;
        }

        private List<TunnelRunner> SelectedRunners()
        {
            List<TunnelRunner> list = new List<TunnelRunner>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                TunnelRunner r = item.Tag as TunnelRunner;
                if (r != null) list.Add(r);
            }
            return list;
        }

        private void ToggleSelected(bool? start)
        {
            List<TunnelRunner> sel = SelectedRunners();
            if (sel.Count == 0) return;
            foreach (TunnelRunner r in sel)
            {
                bool shouldStart = start.HasValue ? start.Value : (r.State != TunnelState.Running && r.State != TunnelState.Connecting && r.State != TunnelState.Reconnecting);
                if (shouldStart) StartRunner(r);
                else StopRunner(r);
            }
            RefreshAllRows();
        }

        private void StartRunner(TunnelRunner runner)
        {
            if (runner.State == TunnelState.Running || runner.State == TunnelState.Connecting) return;
            bool ok = EnsureCredentials(runner.Config);
            if (!ok) return;
            runner.Start();
        }

        private void StopRunner(TunnelRunner runner)
        {
            runner.Stop();
            Log.Info("已停止隧道：" + runner.Config.Name);
        }

        private void StartAll()
        {
            int started = 0;
            foreach (TunnelConfig t in _config.Tunnels)
            {
                TunnelRunner r = FindRunner(t.Id);
                if (r == null) continue;
                if (r.State == TunnelState.Running || r.State == TunnelState.Connecting) continue;
                if (!EnsureCredentials(t)) continue;
                r.Start();
                started++;
            }
            Log.Info(string.Format("已发出 {0} 条隧道的启动指令", started));
            RefreshAllRows();
        }

        private void StopAll()
        {
            foreach (TunnelRunner r in _runners) r.Stop();
            Log.Info("已停止全部隧道");
            RefreshAllRows();
        }

        /// <summary>密码未保存时弹窗输入（仅当前运行期有效，不会写入磁盘）。</summary>
        private bool EnsureCredentials(TunnelConfig cfg)
        {
            if (cfg.Auth == "privatekey")
            {
                if (string.IsNullOrEmpty(cfg.PrivateKeyPath) || !File.Exists(cfg.PrivateKeyPath))
                {
                    MessageBox.Show("私钥文件不存在：" + cfg.PrivateKeyPath, "ssh_tunnel_ly",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                return true;
            }
            if (!string.IsNullOrEmpty(cfg.Password)) return true;

            string pw = PasswordPromptForm.Ask(this, string.Format("请输入 {0}@{1} 的登录密码：", cfg.Username, cfg.SshHost), cfg.SavePassword);
            if (pw == null) return false;
            cfg.Password = pw;
            if (cfg.SavePassword)
            {
                SaveConfig();
                Log.Info("已按当前 Windows 用户加密保存密码（DPAPI）");
            }
            return true;
        }

        private void NewTunnel()
        {
            TunnelConfig cfg = new TunnelConfig();
            cfg.Name = "隧道 " + (_config.Tunnels.Count + 1).ToString(CultureInfo.InvariantCulture);
            cfg.LocalPort = SuggestLocalPort();
            cfg.SshPort = 22;
            // SSH 主机刻意不继承上一条隧道：它是跳板机地址，必须由使用者按实际情况填写。
            cfg.SshHost = "";
            cfg.Username = _config.Tunnels.Count > 0 ? _config.Tunnels[_config.Tunnels.Count - 1].Username : "";
            if (_config.Tunnels.Count > 0) cfg.Direction = _config.Tunnels[_config.Tunnels.Count - 1].Direction;

            TunnelForm dlg = new TunnelForm(cfg, true, _config.Settings);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _config.Tunnels.Add(cfg);
            Log.Info(string.Format("新建隧道：{0}（{1}：{2}）", cfg.Name, cfg.DirectionText, cfg.RouteDisplay));
            SyncRunners();
            SaveConfig();
        }

        private int SuggestLocalPort()
        {
            for (int port = 10022; port < 65000; port++)
            {
                bool used = false;
                foreach (TunnelConfig t in _config.Tunnels) if (t.LocalPort == port) { used = true; break; }
                if (used) continue;
                try
                {
                    System.Net.Sockets.TcpListener l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                    l.Start();
                    l.Stop();
                    return port;
                }
                catch { }
            }
            return 10022;
        }

        private void EditTunnel()
        {
            TunnelRunner runner = SelectedRunner();
            if (runner == null) return;
            TunnelConfig draft = runner.Config.Clone();
            bool wasRunning = runner.State == TunnelState.Running || runner.State == TunnelState.Connecting || runner.State == TunnelState.Reconnecting;
            if (wasRunning)
            {
                if (MessageBox.Show("该隧道正在运行，编辑前需要先停止。是否继续？", "ssh_tunnel_ly",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                runner.Stop();
                wasRunning = false;
            }

            TunnelForm dlg = new TunnelForm(draft, false, _config.Settings);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            runner.Config.CopyFrom(draft);
            Log.Info("已修改隧道：" + runner.Config.Name);
            RefreshList();
            SaveConfig();
        }

        private void DeleteTunnel()
        {
            TunnelRunner runner = SelectedRunner();
            if (runner == null) return;
            if (MessageBox.Show("确定删除隧道“" + runner.Config.Name + "”？", "ssh_tunnel_ly",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            runner.Stop();
            _config.Tunnels.Remove(runner.Config);
            SyncRunners();
            SaveConfig();
            Log.Info("已删除隧道");
        }

        private void CopySshCommand()
        {
            TunnelRunner runner = SelectedRunner();
            if (runner == null) return;
            try
            {
                Clipboard.SetText(runner.Config.ToSshCommandLine());
                Log.Info("已复制等效命令：" + runner.Config.ToSshCommandLine());
            }
            catch { }
        }

        private void OpenSettings()
        {
            SettingsForm dlg = new SettingsForm(_config.Settings);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (TunnelRunner r in _runners) r.UpdateSettings(_config.Settings);
            Log.Configure(_config.Settings.MaxLogLines, _config.Settings.LogToFile);
            SaveConfig();
            Log.Info("设置已保存");
        }

        private void UpdateButtons()
        {
            int sel = _list.SelectedItems.Count;
            _btnEdit.Enabled = sel == 1;
            _btnDelete.Enabled = sel >= 1;
            _btnStart.Enabled = sel >= 1;
            _btnStop.Enabled = sel >= 1;
            _btnStartAll.Enabled = _config.Tunnels.Count > 0;
            _btnStopAll.Enabled = _config.Tunnels.Count > 0;
        }

        private void OpenFolder(string path)
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

        // ================================================================ 托盘

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            if (!_trayTipShown)
            {
                _trayTipShown = true;
                try
                {
                    _tray.BalloonTipTitle = "ssh_tunnel_ly";
                    _tray.BalloonTipText = "程序已最小化到托盘，端口转发继续在后台运行。双击图标可恢复窗口。";
                    _tray.ShowBalloonTip(3000);
                }
                catch { }
            }
        }

        private void RestoreFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_exiting && _config != null && _config.Settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            Shutdown();
        }

        private void ExitApp()
        {
            _exiting = true;
            Close();
        }

        private void Shutdown()
        {
            try { _timer.Stop(); } catch { }
            foreach (TunnelRunner r in _runners)
            {
                try { r.Dispose(); } catch { }
            }
            _runners.Clear();
            try { _tray.Visible = false; } catch { }
            Log.Info("ssh_tunnel_ly 已退出");
        }
    }

    // ==================================================================== 密码输入框

    public class PasswordPromptForm : Form
    {
        private TextBox _box;
        private CheckBox _save;

        public static string Ask(IWin32Window owner, string prompt, bool savePassword)
        {
            using (PasswordPromptForm f = new PasswordPromptForm(prompt, savePassword))
            {
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return f._box.Text;
            }
        }

        public static bool LastSaveChoice = true;

        private PasswordPromptForm(string prompt, bool savePassword)
        {
            Text = "ssh_tunnel_ly";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(400, 140);
            Font = new Font("Microsoft YaHei UI", 9f);

            Label l = new Label();
            l.Text = prompt;
            l.SetBounds(14, 16, 370, 20);
            Controls.Add(l);

            _box = new TextBox();
            _box.UseSystemPasswordChar = true;
            _box.SetBounds(14, 42, 370, 24);
            Controls.Add(_box);

            _save = new CheckBox();
            _save.Text = "保存密码（使用当前 Windows 用户加密存储）";
            _save.Checked = savePassword;
            _save.SetBounds(14, 72, 340, 22);
            Controls.Add(_save);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(220, 102, 78, 26);
            ok.Click += delegate { LastSaveChoice = _save.Checked; };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(306, 102, 78, 26);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
