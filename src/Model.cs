using System;
using System.Collections.Generic;
using System.Globalization;

namespace SshTunnelLy
{
    /// <summary>
    /// 单条端口转发（等价于一条 ssh -L 或 ssh -R 命令）的配置。
    /// Direction = "local"  本地转发：本机监听 LocalHost:LocalPort → 服务器侧 DestHost:DestPort
    /// Direction = "remote" 远程转发：服务器监听 LocalHost:LocalPort → 本机侧 DestHost:DestPort
    /// （两种方向下 LocalHost/LocalPort 都是"监听端"，DestHost/DestPort 都是"被转发的目标服务"，
    ///   区别只在于监听端在哪台机器上、目标地址由哪一端解析。）
    /// </summary>
    public class TunnelConfig
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public bool AutoStart = false;   // 开机自启：程序启动时自动连接此隧道（默认关）

        public string Direction = "local";   // local (-L) | remote (-R)

        // 监听地址与目标地址默认留空：留空时界面/配置里就是空的，运行时按 127.0.0.1（回环）处理。
        public string LocalHost = "";
        public int LocalPort = 0;

        public string DestHost = "";
        public int DestPort = 0;

        public string SshHost = "";
        public int SshPort = 22;
        public string Username = "";

        public string Auth = "password";   // password | privatekey
        public string Password = "";       // 运行时使用；是否落盘取决于 SavePassword
        public bool SavePassword = true;
        public string PrivateKeyPath = "";
        public string PrivateKeyPassphrase = "";

        public bool AutoReconnect = true;

        public bool IsRemote { get { return Direction == "remote"; } }
        public string DirectionText { get { return IsRemote ? "远程" : "本地"; } }

        /// <summary>监听地址留空时实际使用的地址（ssh 省略绑定地址即为回环）。</summary>
        public string EffectiveLocalHost { get { return string.IsNullOrEmpty(LocalHost) ? "127.0.0.1" : LocalHost.Trim(); } }

        /// <summary>目标地址留空时实际使用的地址（本机/服务器自身的回环）。</summary>
        public string EffectiveDestHost { get { return string.IsNullOrEmpty(DestHost) ? "127.0.0.1" : DestHost.Trim(); } }

        /// <summary>监听地址是否通配（0.0.0.0 / ::）。</summary>
        public bool ListenWildcard { get { return IsUnspecified(LocalHost); } }

        /// <summary>目标地址是否通配（0.0.0.0 / ::），这是无意义的写法，校验会拒绝。</summary>
        public bool DestWildcard { get { return IsUnspecified(DestHost); } }

        /// <summary>判断是否为"未指定"（通配）地址。</summary>
        public static bool IsUnspecified(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            System.Net.IPAddress a;
            if (!System.Net.IPAddress.TryParse(host.Trim(), out a)) return false;
            return a.Equals(System.Net.IPAddress.Any) || a.Equals(System.Net.IPAddress.IPv6Any);
        }

        // 列表里显示的是运行时实际使用的地址，避免出现 ":13080" 这种残缺写法。
        public string LocalDisplay { get { return EffectiveLocalHost + ":" + LocalPort.ToString(CultureInfo.InvariantCulture); } }
        public string DestDisplay { get { return EffectiveDestHost + ":" + DestPort.ToString(CultureInfo.InvariantCulture); } }
        public string SshDisplay { get { return Username + "@" + SshHost + ":" + SshPort.ToString(CultureInfo.InvariantCulture); } }

        /// <summary>列表"监听"列：标明监听端在本机还是在服务器上。</summary>
        public string ListenDisplay { get { return (IsRemote ? "服务器 " : "本机 ") + LocalDisplay; } }

        /// <summary>一句话路径描述，用于日志。</summary>
        public string RouteDisplay
        {
            get
            {
                return IsRemote
                    ? "服务器 " + SshHost + " 上的 " + LocalDisplay + "  ->  本机 " + DestDisplay
                    : "本机 " + LocalDisplay + "  ->  " + SshDisplay + "  ->  服务器侧 " + DestDisplay;
            }
        }

        /// <summary>
        /// 等价的 ssh 命令行，方便复制到终端使用。
        /// 与界面保持一致：监听地址留空则连冒号一起省略（ssh 省略绑定地址即回环）；
        /// 目标地址留空则该字段为空（注意：ssh 不接受空目标地址，复制前需要补上）。
        /// </summary>
        public string ToSshCommandLine()
        {
            string bind = string.IsNullOrEmpty(LocalHost) ? "" : ForCommand(LocalHost.Trim()) + ":";
            string dest = string.IsNullOrEmpty(DestHost) ? "" : ForCommand(DestHost.Trim());
            string s = "ssh -N " + (IsRemote ? "-R " : "-L ") + bind + LocalPort + ":" + dest + ":" + DestPort;
            if (SshPort != 22) s += " -p " + SshPort;
            if (Auth == "privatekey" && !string.IsNullOrEmpty(PrivateKeyPath)) s += " -i \"" + PrivateKeyPath + "\"";
            s += " " + Username + "@" + ForCommand(SshHost == null ? "" : SshHost.Trim());
            return s;
        }

        /// <summary>ssh 命令行里的 IPv6 地址必须写成 [::1] 形式。</summary>
        private static string ForCommand(string host)
        {
            if (string.IsNullOrEmpty(host) || host.IndexOf(':') < 0) return host;
            System.Net.IPAddress a;
            if (!System.Net.IPAddress.TryParse(host, out a)) return host;
            return "[" + host + "]";
        }

        public TunnelConfig Clone()
        {
            return (TunnelConfig)MemberwiseClone();
        }

        /// <summary>就地覆盖字段（保持对象引用不变，便于运行器继续引用同一实例）。</summary>
        public void CopyFrom(TunnelConfig o)
        {
            Name = o.Name;
            AutoStart = o.AutoStart;
            Direction = NormalizeDirection(o.Direction);
            LocalHost = o.LocalHost;
            LocalPort = o.LocalPort;
            DestHost = o.DestHost;
            DestPort = o.DestPort;
            SshHost = o.SshHost;
            SshPort = o.SshPort;
            Username = o.Username;
            Auth = o.Auth;
            Password = o.Password;
            SavePassword = o.SavePassword;
            PrivateKeyPath = o.PrivateKeyPath;
            PrivateKeyPassphrase = o.PrivateKeyPassphrase;
            AutoReconnect = o.AutoReconnect;
        }

        /// <summary>把配置里可能出现的各种写法归一成 local / remote。</summary>
        public static string NormalizeDirection(string v)
        {
            if (string.IsNullOrEmpty(v)) return "local";
            switch (v.Trim().ToLowerInvariant())
            {
                case "remote":
                case "r":
                case "-r":
                case "远程":
                    return "remote";
                default:
                    return "local";
            }
        }

        public List<string> Validate()
        {
            Direction = NormalizeDirection(Direction);
            List<string> errs = new List<string>();
            if (string.IsNullOrEmpty(Name)) Name = "隧道";

            if (LocalPort < 1 || LocalPort > 65535)
                errs.Add((IsRemote ? "服务器监听端口" : "本地监听端口") + "必须是 1-65535");
            if (DestPort < 1 || DestPort > 65535) errs.Add("目标端口必须是 1-65535");

            // 监听地址：允许通配（0.0.0.0 / ::），留空按 127.0.0.1 处理
            string e = CheckAddress(IsRemote ? "服务器监听地址" : "本地监听地址", LocalHost, true, "");
            if (e != null) errs.Add(e);

            // 目标地址：留空按 127.0.0.1 处理；但不能是通配地址（本机解析通配地址会失败）
            e = CheckAddress("目标地址", DestHost, true, "不能填通配地址（0.0.0.0 / ::），请填 127.0.0.1、具体 IP 或主机名");
            if (e != null) errs.Add(e);

            e = CheckAddress("SSH 主机", SshHost, false, "不能填通配地址（0.0.0.0 / ::）");
            if (e != null) errs.Add(e);

            if (SshPort < 1 || SshPort > 65535) errs.Add("SSH 端口必须是 1-65535");
            if (string.IsNullOrEmpty(Username)) errs.Add("用户名不能为空");
            else if (Username.Trim().IndexOfAny(new char[] { ' ', '@', ':' }) >= 0)
                errs.Add("用户名不能包含空格、@ 或 :");
            if (Auth == "privatekey" && string.IsNullOrEmpty(PrivateKeyPath)) errs.Add("私钥认证需要选择私钥文件");
            return errs;
        }

        /// <summary>
        /// 地址形状校验（只做语法检查，不做 DNS 解析，避免界面卡顿）。
        /// blankAllowed 表示留空是否合法；wildcardError 为通配地址的错误后缀，留空表示允许通配。
        /// 返回错误文本，null 表示通过。
        /// </summary>
        public static string CheckAddress(string label, string host, bool blankAllowed, string wildcardError)
        {
            string h = host == null ? "" : host.Trim();
            if (h.Length == 0)
            {
                if (blankAllowed) return null;
                return label + "不能为空";
            }
            if (h.Length > 253) return label + "太长（超过 253 个字符）";

            if (h.IndexOf(':') >= 0)
            {
                // 含冒号：必须是合法的 IPv6 字面量
                System.Net.IPAddress ip6;
                if (!System.Net.IPAddress.TryParse(h, out ip6)) return label + "不是合法的 IPv6 地址";
                if (IsUnspecified(h) && !string.IsNullOrEmpty(wildcardError)) return label + wildcardError;
                return null;
            }

            bool onlyDigitsAndDots = true;
            foreach (char c in h)
            {
                bool digitOrDot = (c >= '0' && c <= '9') || c == '.';
                if (!digitOrDot) onlyDigitsAndDots = false;
                bool allowed = digitOrDot || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '-' || c == '_';
                if (!allowed) return label + "只能包含字母、数字、点、连字符和下划线";
            }
            if (h.StartsWith(".") || h.EndsWith(".") || h.StartsWith("-") || h.EndsWith("-"))
                return label + "格式不正确（不能以点或连字符开头/结尾）";

            System.Net.IPAddress ip4;
            bool isIp = System.Net.IPAddress.TryParse(h, out ip4);
            if (onlyDigitsAndDots)
            {
                if (!isIp) return label + "不是合法的 IPv4 地址";
                if (IsUnspecified(h) && !string.IsNullOrEmpty(wildcardError)) return label + wildcardError;
            }
            return null;
        }

        public JsonMap ToJson()
        {
            JsonMap m = new JsonMap();
            m["id"] = Id;
            m["name"] = Name;
            m["autoStart"] = AutoStart;
            m["direction"] = NormalizeDirection(Direction);
            m["localHost"] = LocalHost;
            m["localPort"] = (long)LocalPort;
            m["destHost"] = DestHost;
            m["destPort"] = (long)DestPort;
            m["sshHost"] = SshHost;
            m["sshPort"] = (long)SshPort;
            m["username"] = Username;
            m["auth"] = Auth;
            m["savePassword"] = SavePassword;
            m["autoReconnect"] = AutoReconnect;
            if (!string.IsNullOrEmpty(PrivateKeyPath)) m["privateKeyPath"] = PrivateKeyPath;
            if (Auth == "privatekey")
            {
                if (SavePassword && !string.IsNullOrEmpty(PrivateKeyPassphrase))
                    m["privateKeyPassphraseEnc"] = ConfigStore.Protect(PrivateKeyPassphrase);
                m["privateKeyPassphrase"] = "";
            }
            else
            {
                if (SavePassword && !string.IsNullOrEmpty(Password))
                    m["passwordEnc"] = ConfigStore.Protect(Password);
                // 不再写入明文密码字段，避免误泄露
            }
            return m;
        }

        public static TunnelConfig FromJson(IDictionary<string, object> m)
        {
            TunnelConfig c = new TunnelConfig();
            c.Id = Json.Str(m, "id", Guid.NewGuid().ToString("N"));
            c.Name = Json.Str(m, "name", "隧道");
            c.AutoStart = Json.Bool(m, "autoStart", false);
            c.Direction = NormalizeDirection(Json.Str(m, "direction", "local"));
            c.LocalHost = Json.Str(m, "localHost", "");
            c.LocalPort = Json.Int(m, "localPort", 0);
            c.DestHost = Json.Str(m, "destHost", "");
            c.DestPort = Json.Int(m, "destPort", 0);
            c.SshHost = Json.Str(m, "sshHost", "");
            c.SshPort = Json.Int(m, "sshPort", 22);
            c.Username = Json.Str(m, "username", "");
            c.Auth = Json.Str(m, "auth", "password");
            c.SavePassword = Json.Bool(m, "savePassword", true);
            c.AutoReconnect = Json.Bool(m, "autoReconnect", true);
            c.PrivateKeyPath = Json.Str(m, "privateKeyPath", "");

            string enc = Json.Str(m, "passwordEnc", "");
            if (!string.IsNullOrEmpty(enc)) c.Password = ConfigStore.Unprotect(enc);
            else c.Password = Json.Str(m, "password", "");   // 兼容手工编写/脚本生成的明文配置

            string kenc = Json.Str(m, "privateKeyPassphraseEnc", "");
            if (!string.IsNullOrEmpty(kenc)) c.PrivateKeyPassphrase = ConfigStore.Unprotect(kenc);
            else c.PrivateKeyPassphrase = Json.Str(m, "privateKeyPassphrase", "");

            if (c.SshPort <= 0) c.SshPort = 22;
            return c;
        }
    }

    public class AppSettings
    {
        public bool CloseToTray = true;
        public bool LogToFile = true;
        public bool MinimizeToTray = true;
        public int ConnectTimeoutSeconds = 15;
        public int KeepAliveSeconds = 30;
        public int MaxLogLines = 3000;

        public JsonMap ToJson()
        {
            JsonMap m = new JsonMap();
            m["closeToTray"] = CloseToTray;
            m["logToFile"] = LogToFile;
            m["minimizeToTray"] = MinimizeToTray;
            m["connectTimeoutSeconds"] = (long)ConnectTimeoutSeconds;
            m["keepAliveSeconds"] = (long)KeepAliveSeconds;
            m["maxLogLines"] = (long)MaxLogLines;
            return m;
        }

        public static AppSettings FromJson(IDictionary<string, object> m)
        {
            AppSettings s = new AppSettings();
            if (m == null) return s;
            s.CloseToTray = Json.Bool(m, "closeToTray", true);
            s.LogToFile = Json.Bool(m, "logToFile", true);
            s.MinimizeToTray = Json.Bool(m, "minimizeToTray", true);
            s.ConnectTimeoutSeconds = Json.Int(m, "connectTimeoutSeconds", 15);
            s.KeepAliveSeconds = Json.Int(m, "keepAliveSeconds", 30);
            s.MaxLogLines = Json.Int(m, "maxLogLines", 3000);
            if (s.ConnectTimeoutSeconds < 3) s.ConnectTimeoutSeconds = 3;
            if (s.KeepAliveSeconds < 0) s.KeepAliveSeconds = 0;
            return s;
        }
    }

    public class AppConfig
    {
        public int Version = 1;
        public AppSettings Settings = new AppSettings();
        public List<TunnelConfig> Tunnels = new List<TunnelConfig>();
    }
}
