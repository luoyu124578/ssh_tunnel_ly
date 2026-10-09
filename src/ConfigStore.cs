using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SshTunnelLy
{
    /// <summary>
    /// 配置与日志的存放位置：遵循微软规范放在 %APPDATA%\ssh_tunnel_ly\，
    /// 程序目录（同级目录）不会产生任何文件。
    /// </summary>
    public static class ConfigStore
    {
        public const string AppFolderName = "ssh_tunnel_ly";

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName); }
        }

        public static string ConfigPath { get { return Path.Combine(Dir, "config.json"); } }
        public static string LogDir { get { return Path.Combine(Dir, "logs"); } }
        public static string LogPath { get { return Path.Combine(LogDir, "ssh_tunnel_ly.log"); } }

        public static void EnsureDir()
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        }

        // ---------------- 密码保护（当前用户 DPAPI，其他用户/其他机器无法解密） ----------------

        private const string DpapiPrefix = "dpapi:";

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return DpapiPrefix + Convert.ToBase64String(blob);
            }
            catch
            {
                return "";
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return stored;
            try
            {
                byte[] blob = Convert.FromBase64String(stored.Substring(DpapiPrefix.Length));
                byte[] plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        // ---------------- 读写配置 ----------------

        public static AppConfig Load(out string error)
        {
            error = null;
            AppConfig cfg = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return cfg;
                string text = File.ReadAllText(ConfigPath, Encoding.UTF8);
                if (string.IsNullOrEmpty(text.Trim())) return cfg;
                JsonMap root = Json.Parse(text) as JsonMap;
                if (root == null) { error = "配置文件根节点不是对象"; return cfg; }

                cfg.Version = Json.Int(root, "version", 1);
                object sObj;
                if (root.TryGetValue("settings", out sObj)) cfg.Settings = AppSettings.FromJson(sObj as IDictionary<string, object>);

                object tObj;
                if (root.TryGetValue("tunnels", out tObj))
                {
                    JsonList list = tObj as JsonList;
                    if (list != null)
                    {
                        foreach (object o in list)
                        {
                            IDictionary<string, object> m = o as IDictionary<string, object>;
                            if (m != null) cfg.Tunnels.Add(TunnelConfig.FromJson(m));
                        }
                    }
                }
                return cfg;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return cfg;
            }
        }

        public static void Save(AppConfig cfg, out string error)
        {
            error = null;
            try
            {
                EnsureDir();
                JsonMap root = new JsonMap();
                root["version"] = (long)cfg.Version;
                root["settings"] = cfg.Settings.ToJson();
                JsonList list = new JsonList();
                foreach (TunnelConfig t in cfg.Tunnels) list.Add(t.ToJson());
                root["tunnels"] = list;

                string text = Json.Write(root, true);
                string tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(ConfigPath))
                {
                    string bak = ConfigPath + ".bak";
                    try { File.Copy(ConfigPath, bak, true); }
                    catch { }
                    File.Delete(ConfigPath);
                }
                File.Move(tmp, ConfigPath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
    }
}
