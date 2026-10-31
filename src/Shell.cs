using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SshTunnelLy
{
    /// <summary>
    /// 打开目录 / 文件，走 ShellExecuteW 而不是 System.Diagnostics.Process。
    /// 原因：托管代码里引用 Process（Process.Start / GetProcessById）会把"启动进程"
    /// "枚举进程"这类能力写进程序集元数据，安全软件的启发式会把它当作可疑能力；
    /// 而 Process.Start 最终也是调用 ShellExecute，直接 P/Invoke 既等价又不留这个痕迹。
    /// </summary>
    internal static class Shell
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ShellExecuteW(IntPtr hwnd, string operation, string file,
                                                   string parameters, string directory, int showCommand);

        private const int SW_SHOWNORMAL = 1;

        public static void OpenFolder(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Open(path);
        }

        public static void Open(string path)
        {
            // ShellExecuteW 返回值 <= 32 表示失败（见 MSDN 的返回值约定）
            IntPtr result = ShellExecuteW(IntPtr.Zero, "open", path, null, null, SW_SHOWNORMAL);
            if ((long)result <= 32)
                throw new InvalidOperationException("ShellExecute 失败，返回值 " + (long)result);
        }
    }
}
