using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace SshTunnelLy
{
    /// <summary>新增 / 编辑隧道对话框（本地转发 -L 与远程转发 -R 共用同一套字段）。</summary>
    public class TunnelForm : Form
    {
        private readonly TunnelConfig _cfg;
        private readonly bool _isNew;
        private readonly AppSettings _settings;

        private TextBox _name;
        private CheckBox _autoStart;
        private CheckBox _autoReconnect;

        private RadioButton _dirLocal, _dirRemote;
        private Label _lblDirHint;

        private Label _grpListen, _lblListenAddr, _hintListen;
        private TextBox _localHost, _localPort;

        private Label _grpDest, _lblDestAddr, _hintDest;
        private TextBox _destHost, _destPort;

        private TextBox _sshHost, _sshPort;
        private TextBox _user;
        private ComboBox _auth;

        private Label _lblPassword;
        private Label _lblPasswordHint;
        private TextBox _password;
        private CheckBox _savePassword;

        private Label _lblKey, _lblKeyHint, _lblKeyPass, _lblKeyPassHint;
        private TextBox _keyPath;
        private Button _browse;
        private TextBox _keyPass;

        private Label _previewLabel;
        private TextBox _preview;
        private Label _error;
        private Button _ok, _cancel;

        // 布局基准：私钥区按认证方式整块收起/展开
        private int _tailY;
        private int _keyBlockHeight;
        private int _passBlockHeight;
        private int _keyOffset;

        // 说明文字（两个方向各一份；建界面时按最长的那份预留高度，避免文字被裁掉）
        private const string HintDirLocal = "本地转发：本机监听端口，把连到该端口的访问经 SSH 服务器送到目标服务。";
        private const string HintDirRemote = "远程转发：由 SSH 服务器监听端口，把连到该端口的访问经隧道送回本机的目标服务。";
        private const string HintListenLocal = "留空 = 本机回环 127.0.0.1（只有本机能访问）；填 0.0.0.0 则监听本机所有 IPv4 地址，允许局域网内其它机器访问";
        private const string HintListenRemote = "留空 = 服务器回环 127.0.0.1（只有服务器本机能访问）；填 0.0.0.0 需服务端 sshd 设置 GatewayPorts yes，否则 sshd 仍只绑定回环";
        private const string HintDestLocal = "留空 = SSH 服务器回环 127.0.0.1（服务器自身）；不能填 0.0.0.0";
        private const string HintDestRemote = "留空 = 本机回环 127.0.0.1（运行本程序的这台机器自身）；不能填 0.0.0.0";

        public TunnelForm(TunnelConfig cfg, bool isNew, AppSettings settings)
        {
            _cfg = cfg;
            _isNew = isNew;
            _settings = settings;
            BuildUi();
            LoadFrom(cfg);
            UpdateDirectionRows();
            UpdateAuthRows();
            UpdatePreview();
        }

        // 画布坐标：右侧留白 18，标签列宽 112，字段列从 138 开始
        private const int PadX = 18;
        private const int LabelW = 112;
        private const int FieldX = 138;
        private const int ClientW = 700;
        private const int RightEdge = ClientW - PadX;
        private const int RowH = 32;

        private int FieldW { get { return RightEdge - FieldX; } }

        private void BuildUi()
        {
            Text = _isNew ? "新建隧道" : "编辑隧道";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;   // 手动像素布局，禁止按字体自动缩放，否则底部按钮会被挤出窗口
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = AppIcon.Get();

            int y = 16;

            AddLabel("名称", y);
            _name = AddText(FieldX, y, FieldW);
            y += RowH;

            _autoStart = AddCheck("开机自启（程序启动时连接本条）", FieldX, y, 220);
            _autoReconnect = AddCheck("断线后自动重连", FieldX + 220, y, 150);
            y += 30;

            // ---------------------------------------------------------- 转发方向
            AddGroup("转发方向", ref y);
            _dirLocal = AddRadio("本地转发（本机监听）", FieldX, y, 180);
            _dirRemote = AddRadio("远程转发（服务器监听）", FieldX + 190, y, 200);
            _dirLocal.Checked = true;
            y += 30;
            _lblDirHint = AddHint("", PadX, y, MaxHintHeight(HintDirLocal, HintDirRemote, PadX));
            y += _lblDirHint.Height + 2;

            // ---------------------------------------------------------- 监听端
            _grpListen = AddGroup("本地监听", ref y);
            _lblListenAddr = AddLabel("监听地址", y);
            _localHost = AddHostText(FieldX, y, 290);
            AddLabel2("端口", 440, y, 40);
            _localPort = AddPortText(484, y, 96);
            y += RowH;
            _hintListen = AddHint("", PadX, y, MaxHintHeight(HintListenLocal, HintListenRemote, PadX));
            y += _hintListen.Height + 2;

            // ---------------------------------------------------------- 目标地址
            _grpDest = AddGroup("目标地址", ref y);
            _lblDestAddr = AddLabel("目标地址", y);
            _destHost = AddHostText(FieldX, y, 290);
            AddLabel2("端口", 440, y, 40);
            _destPort = AddPortText(484, y, 96);
            y += RowH;
            _hintDest = AddHint("", PadX, y, MaxHintHeight(HintDestLocal, HintDestRemote, PadX));
            y += _hintDest.Height + 2;

            // ---------------------------------------------------------- SSH 服务器
            AddGroup("SSH 服务器（跳板机）", ref y);
            AddLabel("主机", y);
            _sshHost = AddHostText(FieldX, y, 290);
            AddLabel2("端口", 440, y, 40);
            _sshPort = AddPortText(484, y, 96);
            y += RowH;

            AddLabel("用户名", y);
            _user = AddText(FieldX, y, 290);
            AddLabel2("认证方式", 440, y, 76);
            _auth = new ComboBox();
            _auth.DropDownStyle = ComboBoxStyle.DropDownList;
            _auth.Items.Add("密码");
            _auth.Items.Add("私钥文件");
            _auth.SetBounds(520, y, 150, 24);
            _auth.SelectedIndexChanged += delegate { UpdateAuthRows(); UpdatePreview(); };
            Controls.Add(_auth);
            y += RowH;

            // ---------------------------------------------------------- 密码
            int passBlockStart = y;
            _lblPassword = AddLabel("密码", y);
            _password = AddText(FieldX, y, 290);
            _password.UseSystemPasswordChar = true;
            _lblPasswordHint = AddHint("留空则每次启动时弹窗询问", 440, y);
            _savePassword = AddCheck("保存密码（DPAPI 加密，仅当前 Windows 用户可解密）", FieldX, y + 30, 520);
            y += RowH + 30;
            _passBlockHeight = y - passBlockStart;

            // ---------------------------------------------------------- 私钥
            int keyBlockStart = y;
            _lblKey = AddLabel("私钥文件", y);
            _keyPath = AddText(FieldX, y, 400);
            _browse = new Button();
            _browse.Text = "浏览…";
            _browse.SetBounds(548, y - 1, 90, 25);
            _browse.Click += delegate { BrowseKey(); };
            Controls.Add(_browse);
            _lblKeyHint = AddHint("支持 OpenSSH（id_rsa、id_ed25519…）与 PuTTY .ppk 私钥；只用私钥认证，不会回退到密码", FieldX, y + 30);
            y += RowH + 30;

            _lblKeyPass = AddLabel("私钥口令", y);
            _keyPass = AddText(FieldX, y, 290);
            _keyPass.UseSystemPasswordChar = true;
            _lblKeyPassHint = AddHint("私钥没有口令则留空", 440, y);
            y += RowH;
            _keyBlockHeight = y - keyBlockStart;

            // ---------------------------------------------------------- 等效命令 + 按钮
            _tailY = y;

            _previewLabel = new Label();
            _previewLabel.Text = "等效命令（-N 表示仅作端口转发、不在远端执行命令；只读，可选中后复制到终端使用）";
            _previewLabel.SetBounds(PadX, y + 4, RightEdge - PadX, 20);
            _previewLabel.ForeColor = Color.DimGray;
            Controls.Add(_previewLabel);

            _preview = new TextBox();
            _preview.Multiline = true;
            _preview.ReadOnly = true;
            _preview.WordWrap = false;
            _preview.ScrollBars = ScrollBars.Horizontal;
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.BackColor = Color.FromArgb(245, 245, 248);
            _preview.ForeColor = Color.FromArgb(50, 50, 70);
            _preview.Font = new Font("Consolas", 9f);
            _preview.SetBounds(PadX, y + 26, RightEdge - PadX, 44);
            Controls.Add(_preview);

            _error = new Label();
            _error.ForeColor = Color.FromArgb(190, 0, 0);
            Controls.Add(_error);

            _ok = new Button();
            _ok.Text = "确定";
            _ok.Click += delegate { ApplyAndClose(); };
            Controls.Add(_ok);

            _cancel = new Button();
            _cancel.Text = "取消";
            _cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(_cancel);

            AcceptButton = _ok;
            CancelButton = _cancel;

            MoveTail(0);

            // 事件在控件全部就绪后再挂，避免初始化时访问尚未创建的控件
            _dirLocal.CheckedChanged += delegate { UpdateDirectionRows(); UpdatePreview(); };
            _dirRemote.CheckedChanged += delegate { UpdateDirectionRows(); UpdatePreview(); };
        }

        // ================================================================ 布局辅助

        private Label AddGroup(string title, ref int y)
        {
            Label l = new Label();
            l.Text = "—  " + title;
            l.SetBounds(PadX, y, RightEdge - PadX, 18);
            l.ForeColor = Color.FromArgb(70, 90, 140);
            Controls.Add(l);
            y += 22;
            return l;
        }

        private Label AddLabel(string text, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(PadX, y + 4, LabelW, 20);
            l.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(l);
            return l;
        }

        private void AddLabel2(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y + 4, w, 20);
            l.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(l);
        }

        private Label AddHint(string text, int x, int y)
        {
            return AddHint(text, x, y, HintHeight(text, x));
        }

        /// <summary>创建说明文字标签。高度由调用方给出，务必够放下换行后的全文，否则文字会被裁掉。</summary>
        private Label AddHint(string text, int x, int y, int height)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y + 4, RightEdge - x, height);
            l.ForeColor = Color.DimGray;
            Controls.Add(l);
            return l;
        }

        /// <summary>说明文字在给定宽度下换行显示需要的高度（多留 4px，避免最后一行被切）。</summary>
        private int HintHeight(string text, int x)
        {
            if (string.IsNullOrEmpty(text)) return 18;
            int w = RightEdge - x;
            Size sz = TextRenderer.MeasureText(text, Font, new Size(w, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return Math.Max(18, sz.Height + 4);
        }

        /// <summary>同一处提示在两种转发方向下的文案不同，按最长的那份预留高度，切换方向时就不会被裁掉。</summary>
        private int MaxHintHeight(string a, string b, int x)
        {
            return Math.Max(HintHeight(a, x), HintHeight(b, x));
        }

        private TextBox AddText(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 24);
            t.TextChanged += delegate { UpdatePreview(); };
            Controls.Add(t);
            return t;
        }

        /// <summary>端口输入框：只允许数字，最多 5 位。</summary>
        private TextBox AddPortText(int x, int y, int w)
        {
            TextBox t = AddText(x, y, w);
            t.MaxLength = 5;
            t.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (!char.IsControl(e.KeyChar) && (e.KeyChar < '0' || e.KeyChar > '9')) e.Handled = true;
            };
            return t;
        }

        /// <summary>地址输入框：不允许输入空格（粘贴进来的空格由校验负责提示）。</summary>
        private TextBox AddHostText(int x, int y, int w)
        {
            TextBox t = AddText(x, y, w);
            t.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == ' ') e.Handled = true;
            };
            return t;
        }

        private CheckBox AddCheck(string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.SetBounds(x, y, w, 22);
            Controls.Add(c);
            return c;
        }

        private RadioButton AddRadio(string text, int x, int y, int w)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.SetBounds(x, y, w, 22);
            Controls.Add(r);
            return r;
        }

        /// <summary>把"等效命令 / 错误提示 / 按钮"这一整段上下移动，并同步窗口高度。</summary>
        private void MoveTail(int delta)
        {
            int y = _tailY + delta;
            _previewLabel.Top = y + 4;
            _preview.Top = y + 26;
            _error.SetBounds(PadX, y + 26 + 44 + 10, RightEdge - PadX, 34);
            int btnY = y + 26 + 44 + 10 + 38;
            _ok.SetBounds(RightEdge - 172, btnY, 80, 28);
            _cancel.SetBounds(RightEdge - 84, btnY, 80, 28);
            ClientSize = new Size(ClientW, btnY + 40);
        }

        // ================================================================ 数据

        /// <summary>端口显示：未设置（0）时留空，由使用者手填，避免出现"0"这种无意义的值。</summary>
        private static string Num(int v)
        {
            return v > 0 ? v.ToString(CultureInfo.InvariantCulture) : "";
        }

        private void LoadFrom(TunnelConfig c)
        {
            _name.Text = c.Name;
            _autoStart.Checked = c.AutoStart;
            _autoReconnect.Checked = c.AutoReconnect;
            _dirLocal.Checked = !c.IsRemote;
            _dirRemote.Checked = c.IsRemote;
            _localHost.Text = c.LocalHost;
            _localPort.Text = Num(c.LocalPort);
            _destHost.Text = c.DestHost;
            _destPort.Text = Num(c.DestPort);
            _sshHost.Text = c.SshHost;
            _sshPort.Text = Num(c.SshPort);
            _user.Text = c.Username;
            _auth.SelectedIndex = c.Auth == "privatekey" ? 1 : 0;
            _password.Text = c.Password ?? "";
            _savePassword.Checked = c.SavePassword;
            _keyPath.Text = c.PrivateKeyPath ?? "";
            _keyPass.Text = c.PrivateKeyPassphrase ?? "";
        }

        /// <summary>方向变化时改标题与说明文字（字段含义不变，只是监听端换了一台机器）。</summary>
        private void UpdateDirectionRows()
        {
            if (_dirRemote.Checked)
            {
                _grpListen.Text = "—  远程监听（在 SSH 服务器上开放出去的地址）";
                _lblListenAddr.Text = "远端地址";
                _hintListen.Text = HintListenRemote;
                _grpDest.Text = "—  目标地址（本机上被转发的服务）";
                _lblDestAddr.Text = "目标地址";
                _hintDest.Text = HintDestRemote;
                _lblDirHint.Text = HintDirRemote;
            }
            else
            {
                _grpListen.Text = "—  本地监听（在本机上开放给其它程序连接的地址）";
                _lblListenAddr.Text = "监听地址";
                _hintListen.Text = HintListenLocal;
                _grpDest.Text = "—  目标地址（SSH 服务器上被转发的服务）";
                _lblDestAddr.Text = "目标地址";
                _hintDest.Text = HintDestLocal;
                _lblDirHint.Text = HintDirLocal;
            }
        }

        private void UpdateAuthRows()
        {
            bool byKey = _auth.SelectedIndex == 1;
            _lblKey.Visible = _keyPath.Visible = _browse.Visible = _lblKeyHint.Visible = byKey;
            _lblKeyPass.Visible = _keyPass.Visible = _lblKeyPassHint.Visible = byKey;
            _lblPassword.Visible = _password.Visible = _savePassword.Visible = _lblPasswordHint.Visible = !byKey;

            // 私钥认证时把整个私钥区块上移，补上被隐藏的密码区块留下的空白；
            // 密码认证时私钥区块不可见，直接由 MoveTail 收回它的高度。
            ShiftKeyBlock((byKey ? -_passBlockHeight : 0) - _keyOffset);
            MoveTail(byKey ? -_passBlockHeight : -_keyBlockHeight);
        }

        /// <summary>按偏移量移动私钥区块（相对当前位置，可反复调用）。</summary>
        private void ShiftKeyBlock(int dy)
        {
            if (dy == 0) return;
            Control[] block = new Control[] { _lblKey, _keyPath, _browse, _lblKeyHint, _lblKeyPass, _keyPass, _lblKeyPassHint };
            foreach (Control c in block)
            {
                if (c != null) c.Top += dy;
            }
            _keyOffset += dy;
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            _preview.Text = Collect().ToSshCommandLine();
        }

        private TunnelConfig Collect()
        {
            TunnelConfig c = _cfg.Clone();
            c.Name = _name.Text.Trim();
            c.AutoStart = _autoStart.Checked;
            c.AutoReconnect = _autoReconnect.Checked;
            c.Direction = _dirRemote.Checked ? "remote" : "local";
            c.LocalHost = _localHost.Text.Trim();
            c.LocalPort = ParsePort(_localPort.Text, 0);
            c.DestHost = _destHost.Text.Trim();
            c.DestPort = ParsePort(_destPort.Text, 0);
            c.SshHost = _sshHost.Text.Trim();
            c.SshPort = ParsePort(_sshPort.Text, 22);
            c.Username = _user.Text.Trim();
            c.Auth = _auth.SelectedIndex == 1 ? "privatekey" : "password";
            if (c.Auth == "privatekey")
            {
                // 私钥认证不使用密码：清掉可能残留的密码（例如从密码认证切换过来、
                // 密码框虽已隐藏但内容还在），既避免它落盘，也避免运行时被当成
                // 备选认证方式而出现"私钥选错了却仍然连上"的误导现象。
                c.Password = "";
                c.SavePassword = false;
            }
            else
            {
                c.Password = _password.Text;
                c.SavePassword = _savePassword.Checked;
            }
            c.PrivateKeyPath = _keyPath.Text.Trim();
            c.PrivateKeyPassphrase = _keyPass.Text;
            return c;
        }

        private static int ParsePort(string s, int def)
        {
            int v;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        private bool ApplyAndClose()
        {
            TunnelConfig c = Collect();

            List<string> errs = c.Validate();
            if (errs.Count > 0)
            {
                _error.Text = string.Join("；", errs.ToArray());
                return false;
            }
            if (c.Auth == "privatekey" && !File.Exists(c.PrivateKeyPath))
            {
                _error.Text = "私钥文件不存在：" + c.PrivateKeyPath;
                return false;
            }
            // 未勾选“保存密码”时，密码仍保留在内存中的配置对象里供本次运行使用，只是不落盘。
            _cfg.CopyFrom(c);
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        /// <summary>弹出标准的文件选择框（资源管理器样式）让使用者指定私钥文件。</summary>
        private void BrowseKey()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "选择私钥文件（OpenSSH id_rsa/id_ed25519 或 PuTTY .ppk）";
                d.Filter = "SSH 私钥文件|id_rsa*;id_ed25519*;id_ecdsa*;id_dsa*;*.pem;*.key;*.ppk"
                         + "|PuTTY 私钥 (*.ppk)|*.ppk"
                         + "|PEM 私钥 (*.pem;*.key)|*.pem;*.key"
                         + "|所有文件 (*.*)|*.*";
                d.CheckFileExists = true;
                d.CheckPathExists = true;
                d.RestoreDirectory = true;
                d.DereferenceLinks = true;
                d.InitialDirectory = DefaultKeyDirectory();
                // 已经填过路径时，直接从那个文件所在的目录打开
                string cur = (_keyPath.Text ?? "").Trim().Trim('"');
                if (cur.Length > 0)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(cur);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
                        if (File.Exists(cur)) d.FileName = Path.GetFileName(cur);
                    }
                    catch { }
                }
                if (d.ShowDialog(this) == DialogResult.OK) _keyPath.Text = d.FileName;
            }
        }

        /// <summary>私钥对话框的默认起始目录：优先 %USERPROFILE%\.ssh，其次"我的文档"。</summary>
        private static string DefaultKeyDirectory()
        {
            try
            {
                string ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
                if (Directory.Exists(ssh)) return ssh;
            }
            catch { }
            try { return Environment.GetFolderPath(Environment.SpecialFolder.Personal); }
            catch { return ""; }
        }
    }
}
