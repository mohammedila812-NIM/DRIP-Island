using System;
using System.IO;

namespace IslandWin;

public static class Log
{
    static readonly object Sync = new();
    static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandWin", "logs");

    static Log()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            CleanupOldLogs();
        }
        catch { }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

    static void Write(string level, string message, Exception ex)
    {
        try
        {
            string date = DateTime.Now.ToString("yyyyMMdd");
            string file = Path.Combine(LogDir, $"island-{date}.log");
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string line = $"{time} [{level}] {message}";
            if (ex != null)
                line += $"{Environment.NewLine}  {ex}";

            lock (Sync)
            {
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }
        catch { }
    }

    static void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDir)) return;
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.GetFiles(LogDir, "island-*.log"))
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTime < cutoff)
                {
                    try { fi.Delete(); } catch { }
                }
            }
        }
        catch { }
    }
}
