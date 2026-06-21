using System;
using System.IO;

namespace rdpManager.Helpers
{
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "rdpManager.log");
        private static readonly object LockObj = new object();

        public static event Action<string>? OnLogWritten;

        public static void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        public static void LogWarning(string message)
        {
            WriteLog("WARN", message);
        }

        public static void LogError(string message, Exception? ex = null)
        {
            string fullMessage = message;
            if (ex != null)
            {
                fullMessage += $"\nException: {ex.Message}\nStackTrace:\n{ex.StackTrace}";
            }
            WriteLog("ERROR", fullMessage);
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                
                lock (LockObj)
                {
                    string dir = Path.GetDirectoryName(LogFilePath)!;
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.AppendAllText(LogFilePath, logLine);
                }

                // 触发事件通知订阅者（如 UI 界面）
                OnLogWritten?.Invoke(logLine);
            }
            catch
            {
                // 确保日志系统本身绝不抛出任何未捕获异常导致应用程序崩溃
            }
        }
    }
}
