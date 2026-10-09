using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SshTunnelLy
{
    public enum TunnelState
    {
        Stopped,
        Connecting,
        Running,
        Reconnecting,
        Error
    }

    /// <summary>
    /// 一条端口转发隧道：SSH 客户端 + 一个转发端口（本地 -L 或远程 -R）。
    /// 等价命令：ssh -N -L localHost:localPort:destHost:destPort user@sshHost -p sshPort
    ///           ssh -N -R localHost:localPort:destHost:destPort user@sshHost -p sshPort
    /// 每条隧道独立线程/独立 SSH 连接，因此可以同时运行任意多条映射。
    /// </summary>
    public class TunnelRunner : IDisposable
    {
        private readonly object _gate = new object();
        private readonly TunnelConfig _config;
        private AppSettings _settings;

        private Thread _thread;
        private volatile bool _want;
        private SshClient _client;
        private List<ForwardedPort> _ports = new List<ForwardedPort>();

        private TunnelState _state = TunnelState.Stopped;
        private string _lastError = "";
        private int _reconnects;
        private DateTime _connectedAt = DateTime.MinValue;
        private DateTime _startedAt = DateTime.MinValue;

        public event EventHandler Changed;

        public TunnelRunner(TunnelConfig config, AppSettings settings)
        {
            _config = config;
            _settings = settings;
        }

        public TunnelConfig Config { get { return _config; } }

        public void UpdateSettings(AppSettings settings)
        {
            lock (_gate) { _settings = settings; }
        }

        public TunnelState State { get { lock (_gate) return _state; } }
        public string LastError { get { lock (_gate) return _lastError; } }
        public int ReconnectCount { get { lock (_gate) return _reconnects; } }
        public DateTime ConnectedAt { get { lock (_gate) return _connectedAt; } }
        public bool IsActive { get { return _want; } }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case TunnelState.Running:
                        {
                            DateTime t = ConnectedAt;
                            if (t == DateTime.MinValue) return "运行中";
                            TimeSpan d = DateTime.Now - t;
                            return string.Format(CultureInfo.InvariantCulture, "运行中 {0:D2}:{1:D2}:{2:D2}", (int)d.TotalHours, d.Minutes, d.Seconds);
                        }
                    case TunnelState.Connecting: return "连接中…";
                    case TunnelState.Reconnecting: return string.Format(CultureInfo.InvariantCulture, "重连中…({0})", ReconnectCount);
                    case TunnelState.Error: return "错误";
                    default: return "已停止";
                }
            }
        }

        private void SetState(TunnelState state, string error)
        {
            EventHandler h;
            lock (_gate)
            {
                _state = state;
                if (error != null) _lastError = error;
                if (state == TunnelState.Running) _connectedAt = DateTime.Now;
                if (state == TunnelState.Stopped || state == TunnelState.Connecting) { }
                h = Changed;
            }
            if (h != null) { try { h(this, EventArgs.Empty); } catch { } }
        }

        // ---------------------------------------------------------------- 启停

        public void Start()
        {
            lock (_gate)
            {
                if (_want) return;
                _want = true;
                _startedAt = DateTime.Now;
                _reconnects = 0;
                if (_thread == null || !_thread.IsAlive)
                {
                    _thread = new Thread(RunLoop);
                    _thread.IsBackground = true;
                    _thread.Name = "tunnel-" + _config.Name;
                    _thread.Start();
                }
            }
            SetState(TunnelState.Connecting, "");
        }

        public void Stop()
        {
            lock (_gate) { _want = false; }
            Teardown();
            SetState(TunnelState.Stopped, "");
        }

        public void Dispose()
        {
            try { Stop(); } catch { }
        }

        private bool Wanted { get { return _want; } }

        // ---------------------------------------------------------------- 主循环

        private void RunLoop()
        {
            while (Wanted)
            {
                TunnelConfig cfg = _config.Clone();
                AppSettings settings;
                lock (_gate) { settings = _settings; }

                bool normalDisconnect = false;
                try
                {
                    if (cfg.Auth == "password" && string.IsNullOrEmpty(cfg.Password))
                        throw new InvalidOperationException("未设置登录密码");

                    // 远程转发时监听端在服务器上，本机无法预先探测端口占用。
                    if (!cfg.IsRemote)
                    {
                        string busy = CheckLocalPortFree(cfg);
                        if (busy != null) throw new InvalidOperationException(busy);
                    }

                    SetState(TunnelState.Connecting, "");
                    Log.Info(string.Format("正在连接 {0}（{1}）", cfg.SshDisplay, cfg.RouteDisplay));

                    SshClient client = BuildClient(cfg, settings);
                    lock (_gate) { _client = client; }
                    client.Connect();

                    if (!Wanted) { normalDisconnect = true; break; }

                    List<ForwardedPort> candidates = BuildPorts(cfg);
                    List<ForwardedPort> started = new List<ForwardedPort>();
                    string firstError = null;
                    foreach (ForwardedPort p in candidates)
                    {
                        client.AddForwardedPort(p);
                        try
                        {
                            p.Start();
                            started.Add(p);
                        }
                        catch (Exception ex)
                        {
                            if (firstError == null) firstError = DescribePortFailure(cfg, p, ex);
                            try { p.Dispose(); } catch { }
                        }
                    }
                    if (started.Count == 0)
                        throw new InvalidOperationException(firstError ?? "没有可用的监听地址");
                    if (started.Count < candidates.Count)
                        Log.Warn(string.Format("{0}：{1} 个监听地址中有 {2} 个启动失败（{3}）",
                            cfg.Name, candidates.Count, candidates.Count - started.Count, firstError));
                    lock (_gate) { _ports = started; }

                    Log.Info(string.Format("隧道已启动（{0}）：{1}（服务端 {2}）{3}",
                        cfg.DirectionText, cfg.RouteDisplay, DescribeNegotiation(client), ListenNote(cfg, started)));

                    lock (_gate) { _reconnects = 0; }
                    SetState(TunnelState.Running, "");

                    while (Wanted && client.IsConnected) Thread.Sleep(400);

                    if (Wanted)
                    {
                        Log.Warn(string.Format("{0} 的 SSH 连接已断开", cfg.Name));
                        SetState(TunnelState.Connecting, "SSH 连接已断开");
                    }
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    string msg = Describe(ex);
                    SetState(TunnelState.Error, msg);
                    Log.Error(string.Format("{0}：{1}", cfg.Name, msg));
                }
                finally
                {
                    Teardown();
                }

                if (normalDisconnect || !Wanted) break;

                if (!cfg.AutoReconnect)
                {
                    lock (_gate) { _want = false; }
                    SetState(TunnelState.Stopped, null);
                    break;
                }

                int round;
                lock (_gate) { _reconnects++; round = _reconnects; }
                int delay = Math.Min(30, 2 + (round - 1) * 3);
                SetState(TunnelState.Reconnecting, null);
                Log.Warn(string.Format("{0}：{1} 秒后尝试第 {2} 次重连", cfg.Name, delay, round));
                for (int i = 0; i < delay * 4 && Wanted; i++) Thread.Sleep(250);
            }

            Teardown();
            SetState(TunnelState.Stopped, null);
            lock (_gate) { _thread = null; }
        }

        private void Teardown()
        {
            SshClient client;
            List<ForwardedPort> ports;
            lock (_gate)
            {
                ports = _ports; _ports = new List<ForwardedPort>();
                client = _client; _client = null;
            }
            if (ports != null)
            {
                foreach (ForwardedPort port in ports)
                {
                    try { if (port.IsStarted) port.Stop(); } catch { }
                    try { port.Dispose(); } catch { }
                }
            }
            if (client != null)
            {
                try { if (client.IsConnected) client.Disconnect(); } catch { }
                try { client.Dispose(); } catch { }
            }
        }

        // ---------------------------------------------------------------- 转发端口

        /// <summary>
        /// 构造需要启动的转发端口。
        /// SSH.NET 的字符串绑定地址在 Start() 时会走 DNS 解析，传通配地址（0.0.0.0 / ::）会直接抛
        /// ArgumentException（"未指定，不能作为目标地址使用"），因此：
        ///   -L 监听通配地址 → 在本机每个同族地址上各绑一个端口（效果等于监听全部地址）；
        ///   -R 监听通配地址 → 用 IPAddress.Any / IPv6Any 重载，实际绑定由服务器决定（GatewayPorts）。
        /// </summary>
        private static List<ForwardedPort> BuildPorts(TunnelConfig cfg)
        {
            List<ForwardedPort> list = new List<ForwardedPort>();
            string dest = cfg.EffectiveDestHost;
            uint lport = (uint)cfg.LocalPort;
            uint dport = (uint)cfg.DestPort;

            if (!cfg.ListenWildcard)
            {
                list.Add(cfg.IsRemote
                    ? (ForwardedPort)new ForwardedPortRemote(cfg.EffectiveLocalHost, lport, dest, dport)
                    : (ForwardedPort)new ForwardedPortLocal(cfg.EffectiveLocalHost, lport, dest, dport));
                return list;
            }

            if (cfg.IsRemote)
            {
                list.Add(new ForwardedPortRemote(IPAddress.Any, lport, ResolveFirst(dest), dport));
                return list;
            }

            bool ipv6 = cfg.EffectiveLocalHost.IndexOf(':') >= 0;
            List<string> addrs = ipv6 ? LocalIPv6Addresses() : LocalIPv4Addresses();
            if (addrs.Count == 0) addrs.Add(ipv6 ? "::1" : "127.0.0.1");
            foreach (string a in addrs) list.Add(new ForwardedPortLocal(a, lport, dest, dport));
            return list;
        }

        private static string DescribePortFailure(TunnelConfig cfg, ForwardedPort port, Exception ex)
        {
            string where = PortEndpoint(port);
            if (string.IsNullOrEmpty(where)) where = cfg.LocalDisplay;
            if (cfg.IsRemote)
                return string.Format("服务器拒绝监听 {0}：{1}（端口可能已被占用；1-1024 需要服务端 root；" +
                    "服务端 GatewayPorts 未开启时只允许绑定回环地址）", where, ex.Message);
            return string.Format("无法监听 {0}：{1}（端口可能已被占用，或被系统保留）", where, ex.Message);
        }

        /// <summary>通配监听时，把实际展开出来的地址写进日志。</summary>
        private static string ListenNote(TunnelConfig cfg, List<ForwardedPort> started)
        {
            if (!cfg.ListenWildcard) return "";
            if (cfg.IsRemote)
                return string.Format("　提示：已请求服务器在 {0}:{1} 监听；若服务端未设置 GatewayPorts yes，" +
                    "sshd 只会绑定回环地址（仅服务器本机可访问）", cfg.EffectiveLocalHost, cfg.LocalPort);
            StringBuilder sb = new StringBuilder();
            foreach (ForwardedPort p in started)
            {
                if (sb.Length > 0) sb.Append("、");
                sb.Append(PortEndpoint(p));
            }
            return "　通配监听已展开到：" + sb;
        }

        /// <summary>取端口对象实际绑定的地址（BoundHost/BoundPort 定义在派生类上）。</summary>
        private static string PortEndpoint(ForwardedPort port)
        {
            try
            {
                ForwardedPortLocal l = port as ForwardedPortLocal;
                if (l != null && !string.IsNullOrEmpty(l.BoundHost))
                    return l.BoundHost + ":" + l.BoundPort.ToString(CultureInfo.InvariantCulture);
                ForwardedPortRemote r = port as ForwardedPortRemote;
                if (r != null && !string.IsNullOrEmpty(r.BoundHost))
                    return r.BoundHost + ":" + r.BoundPort.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
            return null;
        }

        /// <summary>把主机名/IP 解析成 IPAddress（优先 IPv4，和 ForwardedPortRemote 的 IPAddress 重载配套）。</summary>
        private static IPAddress ResolveFirst(string host)
        {
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return ip;
            IPAddress[] all = Dns.GetHostAddresses(host);
            if (all == null || all.Length == 0) throw new InvalidOperationException("无法解析地址：" + host);
            foreach (IPAddress a in all)
                if (a.AddressFamily == AddressFamily.InterNetwork) return a;
            return all[0];
        }

        /// <summary>本机所有 IPv4 地址（含回环）：用于把 0.0.0.0 展开成逐个地址监听。</summary>
        private static List<string> LocalIPv4Addresses()
        {
            List<string> list = new List<string>();
            list.Add("127.0.0.1");
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = ua.Address.ToString();
                        if (!list.Contains(s)) list.Add(s);
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>本机所有 IPv6 地址（含回环，跳过需要 scope id 的链路本地地址）：用于把 :: 展开。</summary>
        private static List<string> LocalIPv6Addresses()
        {
            List<string> list = new List<string>();
            list.Add("::1");
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetworkV6) continue;
                        if (ua.Address.IsIPv6LinkLocal) continue;
                        string s = ua.Address.ToString();
                        if (!list.Contains(s)) list.Add(s);
                    }
                }
            }
            catch { }
            return list;
        }

        // ---------------------------------------------------------------- 构造客户端

        private SshClient BuildClient(TunnelConfig cfg, AppSettings settings)
        {
            List<AuthenticationMethod> methods = new List<AuthenticationMethod>();

            if (cfg.Auth == "privatekey")
            {
                PrivateKeyFile key = string.IsNullOrEmpty(cfg.PrivateKeyPassphrase)
                    ? new PrivateKeyFile(cfg.PrivateKeyPath)
                    : new PrivateKeyFile(cfg.PrivateKeyPath, cfg.PrivateKeyPassphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(cfg.Username, key));
                // 只用私钥认证：绝不把密码作为备选方法一起交给 SSH.NET。
                // 否则一旦私钥不对（未授权/选错文件），库会静默回退到密码并连接成功，
                // 让人误以为"错误的私钥也能连上"。
            }
            else
            {
                methods.Add(new PasswordAuthenticationMethod(cfg.Username, cfg.Password));
                KeyboardInteractiveAuthenticationMethod ki = new KeyboardInteractiveAuthenticationMethod(cfg.Username);
                string password = cfg.Password;
                ki.AuthenticationPrompt += delegate(object sender, AuthenticationPromptEventArgs e)
                {
                    foreach (AuthenticationPrompt prompt in e.Prompts)
                    {
                        if (prompt.Request.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            prompt.Request.IndexOf("密码", StringComparison.Ordinal) >= 0)
                            prompt.Response = password;
                    }
                };
                methods.Add(ki);
            }

            ConnectionInfo info = new ConnectionInfo(cfg.SshHost == null ? "" : cfg.SshHost.Trim(), cfg.SshPort, cfg.Username, methods.ToArray());
            info.Timeout = TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds);
            info.RetryAttempts = 1;

            SshClient client = new SshClient(info);
            client.KeepAliveInterval = TimeSpan.FromSeconds(Math.Max(1, settings.KeepAliveSeconds));
            return client;
        }

        private static string DescribeNegotiation(SshClient client)
        {
            try
            {
                ConnectionInfo ci = client.ConnectionInfo;
                return string.Format("{0} / {1} / {2}",
                    ci.CurrentKeyExchangeAlgorithm, ci.CurrentHostKeyAlgorithm, ci.CurrentServerEncryption);
            }
            catch { return "?"; }
        }

        // ---------------------------------------------------------------- 辅助

        /// <summary>启动前检查本地端口是否被占用，给出更友好的错误提示。</summary>
        private static string CheckLocalPortFree(TunnelConfig cfg)
        {
            IPAddress addr;
            if (!IPAddress.TryParse(cfg.EffectiveLocalHost, out addr)) addr = IPAddress.Any;
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(addr, cfg.LocalPort);
                probe.Start();
                return null;
            }
            catch (SocketException)
            {
                return string.Format("本地端口 {0} 已被占用，请更换本地端口", cfg.LocalDisplay);
            }
            catch (Exception ex)
            {
                return "无法监听本地地址 " + cfg.LocalDisplay + "：" + ex.Message;
            }
            finally
            {
                if (probe != null) { try { probe.Stop(); } catch { } }
            }
        }

        public static string Describe(Exception ex)
        {
            if (ex == null) return "未知错误";
            if (ex is SshAuthenticationException) return "认证失败：" + ex.Message + "（请检查用户名/密码/私钥）";
            if (ex is SshConnectionException) return "SSH 连接失败：" + ex.Message;
            if (ex is SshOperationTimeoutException) return "连接超时：请检查主机地址、端口与网络";
            if (ex is SocketException)
            {
                SocketException se = (SocketException)ex;
                return string.Format("网络错误：{0}（{1}）", se.Message, se.SocketErrorCode);
            }
            if (ex is System.IO.FileNotFoundException) return "文件未找到：" + ex.Message;
            if (ex is Renci.SshNet.Common.SshException) return "SSH 错误：" + ex.Message;
            return ex.Message;
        }
    }
}
