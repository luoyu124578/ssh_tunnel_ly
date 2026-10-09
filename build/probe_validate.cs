// Dev-only probe: exercises TunnelConfig.Validate() / CheckAddress() without starting the GUI.
using System;
using System.Collections.Generic;
using SshTunnelLy;

internal static class ProbeValidate
{
    private static int _fail;

    private static void Case(string label, string dir, string local, int lport, string dest, int dport, string user, bool expectOk)
    {
        TunnelConfig c = new TunnelConfig();
        c.Direction = dir;
        c.LocalHost = local;
        c.LocalPort = lport;
        c.DestHost = dest;
        c.DestPort = dport;
        c.SshHost = "ssh.example.com";
        c.SshPort = 22;
        c.Username = user;
        c.Auth = "password";
        c.Password = "x";

        List<string> errs = c.Validate();
        bool ok = errs.Count == 0;
        string verdict = ok == expectOk ? "PASS" : "FAIL";
        if (ok != expectOk) _fail++;
        Console.WriteLine(string.Format("[{0}] {1}  期望={2} 实际={3}  错误={4}",
            verdict, label, expectOk ? "通过" : "报错", ok ? "通过" : "报错",
            errs.Count == 0 ? "(无)" : string.Join(" | ", errs.ToArray())));
        Console.WriteLine("        等效命令: " + c.ToSshCommandLine());
    }

    private static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 留空：监听与目标都允许为空
        Case("-L 监听留空 + 目标留空", "local", "", 13080, "", 3080, "root", true);
        // 通配监听：允许
        Case("-L 监听 0.0.0.0", "local", "0.0.0.0", 13081, "127.0.0.1", 3080, "root", true);
        Case("-R 监听 0.0.0.0", "remote", "0.0.0.0", 19080, "127.0.0.1", 18999, "root", true);
        // 目标地址通配：拒绝
        Case("-L 目标 0.0.0.0", "local", "127.0.0.1", 13082, "0.0.0.0", 3080, "root", false);
        Case("-R 目标 ::", "remote", "127.0.0.1", 19082, "::", 3080, "root", false);
        // 地址格式错误
        Case("监听地址有多余空格（会被 Trim）", "local", "127.0.0.1 ", 13083, "127.0.0.1", 3080, "root", true);
        Case("目标地址以下划线开头（允许）", "local", "127.0.0.1", 13084, "_bad", 3080, "root", true);
        Case("监听地址以点结尾", "local", "127.0.0.1.", 13085, "127.0.0.1", 3080, "root", false);
        Case("合法域名", "local", "127.0.0.1", 13086, "example.com", 80, "root", true);
        Case("合法 IPv6 目标", "local", "127.0.0.1", 13087, "::1", 3080, "root", true);
        // 端口
        Case("本地端口 0", "local", "127.0.0.1", 0, "127.0.0.1", 3080, "root", false);
        Case("目标端口 70000", "local", "127.0.0.1", 13088, "127.0.0.1", 70000, "root", false);
        Case("SSH 端口 22 合法", "local", "127.0.0.1", 13089, "127.0.0.1", 3080, "root", true);
        // 用户名
        Case("用户名含空格", "local", "127.0.0.1", 13090, "127.0.0.1", 3080, "ro ot", false);
        Case("用户名为空", "local", "127.0.0.1", 13091, "127.0.0.1", 3080, "", false);
        Case("用户名含 @", "local", "127.0.0.1", 13092, "127.0.0.1", 3080, "ro@ot", false);
        Case("目标地址含空格（内部，非法）", "local", "127.0.0.1", 13093, "127.0.0.1 x", 3080, "root", false);
        Case("目标地址含分号", "local", "127.0.0.1", 13094, "a;b", 3080, "root", false);
        // 主机留空（新默认值）
        TunnelConfig empty = new TunnelConfig();
        Console.WriteLine();
        Console.WriteLine("新建隧道默认值: LocalHost=\"" + empty.LocalHost + "\"  DestHost=\"" + empty.DestHost +
                          "\"  SshHost=\"" + empty.SshHost + "\"");
        Console.WriteLine("默认值的等效命令: " + empty.ToSshCommandLine());
        List<string> e = empty.Validate();
        Console.WriteLine("空配置校验结果（应报错）: " + (e.Count == 0 ? "(无错误)" : string.Join(" | ", e.ToArray())));

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL VALIDATION CASES PASS" : (_fail + " CASE(S) FAILED"));
    }
}
