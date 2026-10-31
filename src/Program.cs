using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SshTunnelLy
{
    internal static class Program
    {
        private const string MutexName = "Local\\ssh_tunnel_ly_single_instance";
        private const string ShowEventName = "Local\\ssh_tunnel_ly_show_window";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;

        // The main window caption, used to recognise a running instance without asking the OS
        // about other processes (see ActivateRunningInstance).
        private const string WindowTitlePrefix = "ssh_tunnel_ly";

        /// <summary>已有实例时：通知它把主窗口显示出来（托盘隐藏状态下也能唤醒）。</summary>
        private static EventWaitHandle _showEvent;

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    ActivateRunningInstance();
                    return;
                }

                StartShowListener();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    ReportCrash(e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    ReportCrash(e.ExceptionObject as Exception);
                };

                bool startMinimized = false;
                foreach (string a in args)
                {
                    if (string.Equals(a, "--min", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "/min", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "-min", StringComparison.OrdinalIgnoreCase))
                        startMinimized = true;
                }

                Application.Run(new MainForm(startMinimized));
            }
        }

        private static void ReportCrash(Exception ex)
        {
            if (ex == null) return;
            try { Log.Error("未处理的异常：" + ex); } catch { }
            try
            {
                MessageBox.Show("程序遇到未处理的异常：\r\n\r\n" + ex.Message,
                    "ssh_tunnel_ly", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        private static void StartShowListener()
        {
            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                Thread t = new Thread(delegate()
                {
                    while (true)
                    {
                        try { _showEvent.WaitOne(); }
                        catch { return; }
                        try { MainForm.RequestShow(); }
                        catch { }
                    }
                });
                t.IsBackground = true;
                t.Name = "show-window-listener";
                t.Start();
            }
            catch { }
        }

        private static void ActivateRunningInstance()
        {
            // 首选：通过命名事件唤醒已有实例（即使它正隐藏到托盘）。
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out ev))
                {
                    using (ev) { ev.Set(); }
                    return;
                }
            }
            catch { }

            // 退路：按窗口标题找到已有实例的窗口并激活。
            // 这里刻意不用 System.Diagnostics.Process：只为了"看看有没有同名进程"就把
            // 进程枚举能力写进程序集元数据并不划算，直接读顶层窗口标题就够。
            try
            {
                IntPtr found = IntPtr.Zero;
                uint self = GetCurrentProcessId();
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == self) return true;

                    StringBuilder title = new StringBuilder(256);
                    if (GetWindowTextW(hWnd, title, title.Capacity) <= 0) return true;
                    if (!title.ToString().StartsWith(WindowTitlePrefix, StringComparison.OrdinalIgnoreCase)) return true;

                    found = hWnd;
                    return false;
                }, IntPtr.Zero);

                if (found != IntPtr.Zero)
                {
                    ShowWindowAsync(found, SW_RESTORE);
                    SetForegroundWindow(found);
                    return;
                }
            }
            catch { }

            MessageBox.Show("ssh_tunnel_ly 已在运行（请在任务栏通知区域查看托盘图标）。",
                "ssh_tunnel_ly", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
