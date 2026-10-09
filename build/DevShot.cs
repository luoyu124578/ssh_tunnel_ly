using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SshTunnelLy
{
    /// <summary>
    /// 开发用截图工具（不参与发布）：把对话框渲染成 PNG，用于检查控件布局与文字是否被截断。
    /// 编译方式见 build\make_shots.ps1：与 src 下源码一起编译（不含 Program.cs），单独的可执行文件。
    /// </summary>
    internal static class DevShot
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Shot(new TunnelForm(LocalCfg(), true, new AppSettings()), Path.Combine(outDir, "shot_tunnel_local.png"));
            Shot(new TunnelForm(RemoteCfg(), true, new AppSettings()), Path.Combine(outDir, "shot_tunnel_remote.png"));
            Shot(new TunnelForm(KeyCfg(), false, new AppSettings()), Path.Combine(outDir, "shot_tunnel_key.png"));
            Shot(new SettingsForm(new AppSettings()), Path.Combine(outDir, "shot_settings.png"));
            InspectNewTunnel(outDir);
            CheckAuthSwitch();
            Console.WriteLine("DONE");
        }

        /// <summary>
        /// 反射验证：把认证方式切成"私钥文件"以后，Collect() 必须丢弃残留的密码。
        /// 否则运行时会把密码当作备选认证方式交给 SSH.NET，出现"私钥选错了也照样连上"的假象。
        /// </summary>
        private static void CheckAuthSwitch()
        {
            TunnelConfig cfg = new TunnelConfig();
            cfg.SshHost = "ssh.example.com";
            cfg.SshPort = 22;
            cfg.Username = "user";
            cfg.Auth = "password";
            cfg.Password = "remembered-password";
            cfg.SavePassword = true;

            TunnelForm f = new TunnelForm(cfg, false, new AppSettings());
            f.Show();
            Application.DoEvents();

            ((ComboBox)Field(f, "_auth")).SelectedIndex = 1;              // 1 = 私钥文件
            Application.DoEvents();
            ((TextBox)Field(f, "_password")).Text = "typed-password";
            ((CheckBox)Field(f, "_savePassword")).Checked = true;

            MethodInfo mi = typeof(TunnelForm).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance);
            TunnelConfig r = (TunnelConfig)mi.Invoke(f, null);

            bool ok = r.Auth == "privatekey" && r.Password == "" && !r.SavePassword;
            Console.WriteLine("AUTH SWITCH CHECK: auth=" + r.Auth + ", password='" + r.Password + "', savePassword=" + r.SavePassword);
            Console.WriteLine(ok ? "  -> OK：私钥认证时不保留密码（不会回退到密码认证）"
                                 : "  -> FAIL：私钥认证时仍然带着密码，运行时可能回退到密码认证");

            f.Close();
            f.Dispose();
            Application.DoEvents();
        }

        private static object Field(object o, string name)
        {
            FieldInfo fi = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return fi == null ? null : fi.GetValue(o);
        }

        /// <summary>
        /// 反射调用 MainForm.NewTunnel（私有），打开真正的"新建隧道"对话框，
        /// 读出各输入框的初始值（默认值必须是空的，不能带上上一台跳板机的 IP），并渲染成 PNG。
        /// </summary>
        private static void InspectNewTunnel(string outDir)
        {
            MainForm mf = new MainForm(false);
            mf.StartPosition = FormStartPosition.CenterScreen;
            mf.Show();
            Application.DoEvents();

            Timer t = new Timer();
            t.Interval = 700;
            t.Tick += delegate
            {
                t.Stop();
                foreach (Form f in Application.OpenForms)
                {
                    TunnelForm tf = f as TunnelForm;
                    if (tf == null) continue;
                    Application.DoEvents();
                    Console.WriteLine("NEW TUNNEL DEFAULTS (read from the live dialog):");
                    Console.WriteLine("  name      = '" + Text(tf, "_name") + "'");
                    Console.WriteLine("  localHost = '" + Text(tf, "_localHost") + "'");
                    Console.WriteLine("  localPort = '" + Text(tf, "_localPort") + "'");
                    Console.WriteLine("  destHost  = '" + Text(tf, "_destHost") + "'");
                    Console.WriteLine("  destPort  = '" + Text(tf, "_destPort") + "'");
                    Console.WriteLine("  sshHost   = '" + Text(tf, "_sshHost") + "'");
                    Console.WriteLine("  sshPort   = '" + Text(tf, "_sshPort") + "'");
                    Console.WriteLine("  user      = '" + Text(tf, "_user") + "'");
                    Shot(tf, Path.Combine(outDir, "shot_tunnel_new.png"));
                    tf.Close();
                }
            };
            t.Start();

            MethodInfo mi = typeof(MainForm).GetMethod("NewTunnel", BindingFlags.NonPublic | BindingFlags.Instance);
            mi.Invoke(mf, null);

            mf.Close();
            mf.Dispose();
        }

        private static string Text(TunnelForm f, string fieldName)
        {
            FieldInfo fi = typeof(TunnelForm).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi == null) return "(no such field)";
            object v = fi.GetValue(f);
            TextBox tb = v as TextBox;
            return tb != null ? tb.Text : (v == null ? "(null)" : v.ToString());
        }

        private static TunnelConfig Base()
        {
            TunnelConfig c = new TunnelConfig();
            c.SshHost = "ssh.example.com";
            c.SshPort = 22;
            c.Username = "user";
            c.Auth = "password";
            c.Password = "example-password";
            c.SavePassword = true;
            c.AutoReconnect = true;
            return c;
        }

        private static TunnelConfig LocalCfg()
        {
            TunnelConfig c = Base();
            c.Name = "DSH GUI (3080)";
            c.Direction = "local";
            c.LocalHost = "127.0.0.1";
            c.LocalPort = 13080;
            c.DestHost = "127.0.0.1";
            c.DestPort = 3080;
            c.AutoStart = true;
            return c;
        }

        private static TunnelConfig RemoteCfg()
        {
            TunnelConfig c = Base();
            c.Name = "远程转发：服务器 19080 → 本机 8080";
            c.Direction = "remote";
            c.LocalHost = "0.0.0.0";
            c.LocalPort = 19080;
            c.DestHost = "127.0.0.1";
            c.DestPort = 8080;
            c.AutoStart = false;
            return c;
        }

        private static TunnelConfig KeyCfg()
        {
            TunnelConfig c = Base();
            c.Name = "私钥认证示例";
            c.Direction = "local";
            c.LocalHost = "127.0.0.1";
            c.LocalPort = 13306;
            c.DestHost = "10.0.0.8";
            c.DestPort = 3306;
            c.Username = "deploy";
            c.Auth = "privatekey";
            c.PrivateKeyPath = @"C:\Users\you\.ssh\id_ed25519";
            c.PrivateKeyPassphrase = "";
            return c;
        }

        private static void Shot(Form f, string path)
        {
            f.StartPosition = FormStartPosition.CenterScreen;
            if (!f.Visible) f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(250);
            Application.DoEvents();
            ReportClipped(f);
            // DrawToBitmap on a top-level Form paints from the WINDOW origin (caption included),
            // so the bitmap must cover the full form size, not just the client area.
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine(f.Text + " -> " + path + " (win " + f.Width + "x" + f.Height + ", client " + f.ClientSize.Width + "x" + f.ClientSize.Height + ")");
            f.Close();
            f.Dispose();
            Application.DoEvents();
        }

        /// <summary>
        /// 自动检查所有"固定尺寸 + 文字"的控件是否装得下自己的文字：标签按换行后的高度算，
        /// 复选框/单选/按钮按单行宽度算。有被裁掉的就打印出来（文字截断是这类手工布局最常见的毛病）。
        /// </summary>
        private static void ReportClipped(Form f)
        {
            int bad = 0;
            foreach (Control c in All(f))
            {
                if (c.AutoSize || c.Width <= 0 || c.Height <= 0) continue;
                string t = c.Text;
                if (string.IsNullOrEmpty(t)) continue;
                bool wrappable = c is Label;
                Size single = TextRenderer.MeasureText(t, c.Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (wrappable)
                {
                    Size need = TextRenderer.MeasureText(t, c.Font, new Size(c.Width, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    if (need.Height > c.Height + 1)
                    {
                        bad++;
                        Console.WriteLine("  CLIPPED-V [" + f.Text + "] '" + t + "' 需要 " + need.Height + "px 高，实际 " + c.Height + "px（宽 " + c.Width + "）");
                    }
                    else if (need.Height <= single.Height + 1 && single.Width > c.Width + 1)
                    {
                        // 换行后仍然只有一行高 = 这段文字没有可断行的地方，宽度超了就是真的被切掉
                        bad++;
                        Console.WriteLine("  CLIPPED-H [" + f.Text + "] '" + t + "' 需要 " + single.Width + "px 宽，实际 " + c.Width + "px（不换行）");
                    }
                }
                else if (single.Width > c.Width + 1)
                {
                    bad++;
                    Console.WriteLine("  CLIPPED-H [" + f.Text + "] '" + t + "' 需要 " + single.Width + "px 宽，实际 " + c.Width + "px");
                }
            }
            Console.WriteLine("CLIP CHECK [" + f.Text + "]: " + (bad == 0 ? "OK，无文字截断" : bad + " 处被截断"));
        }

        private static System.Collections.Generic.List<Control> All(Control root)
        {
            System.Collections.Generic.List<Control> list = new System.Collections.Generic.List<Control>();
            foreach (Control c in root.Controls)
            {
                list.Add(c);
                list.AddRange(All(c));
            }
            return list;
        }
    }
}
