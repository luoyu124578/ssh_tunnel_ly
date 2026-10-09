using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SshTunnelLy
{
    /// <summary>内存环形缓冲 + 可选文件日志（文件位于 %APPDATA%\ssh_tunnel_ly\logs\）。</summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static readonly Queue<string> Lines = new Queue<string>();
        private static int _maxLines = 3000;
        private static bool _fileEnabled;
        private const long MaxFileBytes = 1024 * 1024;

        public static event Action<string> LineAdded;

        public static void Configure(int maxLines, bool fileEnabled)
        {
            lock (Gate)
            {
                _maxLines = maxLines < 100 ? 100 : maxLines;
                _fileEnabled = fileEnabled;
                while (Lines.Count > _maxLines) Lines.Dequeue();
            }
        }

        public static string[] Snapshot()
        {
            lock (Gate) return Lines.ToArray();
        }

        public static void Info(string message) { Add("信息", message); }
        public static void Warn(string message) { Add("警告", message); }
        public static void Error(string message) { Add("错误", message); }

        public static void Add(string level, string message)
        {
            string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}", DateTime.Now, level, message);
            Action<string> handler;
            lock (Gate)
            {
                Lines.Enqueue(line);
                while (Lines.Count > _maxLines) Lines.Dequeue();
                handler = LineAdded;
            }
            if (_fileEnabled) WriteFile(line);
            if (handler != null)
            {
                try { handler(line); }
                catch { }
            }
        }

        // 多条隧道由各自的线程并发写日志，必须串行化，否则 File.AppendAllText 会因文件被占用而静默丢行。
        private static readonly object FileGate = new object();

        private static void WriteFile(string line)
        {
            lock (FileGate)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        string dir = ConfigStore.LogDir;
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        string path = ConfigStore.LogPath;
                        FileInfo fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > MaxFileBytes)
                        {
                            string old = path + ".1";
                            try { if (File.Exists(old)) File.Delete(old); File.Move(path, old); }
                            catch { }
                        }
                        File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(20);
                    }
                }
            }
        }
    }
}
