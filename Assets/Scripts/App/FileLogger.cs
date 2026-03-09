using System;
using System.IO;
using UnityEngine;

namespace App
{
    /// <summary>
    /// Non-blocking file logger. Uses a persistent StreamWriter with AutoFlush=false
    /// so writes are buffered in memory and flushed on app quit, avoiding main-thread
    /// I/O stalls from the old File.AppendAllText approach.
    /// </summary>
    public static class FileLogger
    {
        private static string logFilePath;
        private static StreamWriter writer;
        private static readonly object lockObj = new object();
        private static bool initialized = false;

        public static string LogFilePath => logFilePath;

        private static void Initialize()
        {
            if (initialized) return;
            initialized = true; // set early to prevent re-entry

            try
            {
                string logDirectory = Application.persistentDataPath;
                logFilePath = Path.Combine(logDirectory, "app_debug.log");
                writer = new StreamWriter(logFilePath, append: true) { AutoFlush = false };
                writer.WriteLine($"\n===== NEW SESSION: {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");

                // Flush & close cleanly when the app exits
                Application.quitting += Flush;
            }
            catch (Exception e)
            {
                // Only essential error: log file could not be opened at all
                Debug.LogError($"[FileLogger] Init failed: {e.Message}");
            }
        }

        public static void Log(string message)
        {
            Initialize();
            try
            {
                lock (lockObj)
                    writer?.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
            catch { /* silent – never spam the console from inside the logger */ }
        }

        public static void LogError(string message)
        {
            Initialize();
            try
            {
                lock (lockObj)
                    writer?.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {message}");
            }
            catch { }
        }

        public static void LogWarning(string message)
        {
            Initialize();
            try
            {
                lock (lockObj)
                    writer?.WriteLine($"[{DateTime.Now:HH:mm:ss}] WARNING: {message}");
            }
            catch { }
        }

        /// <summary>Call periodically (e.g. every few seconds) or on quit to commit buffered entries.</summary>
        public static void Flush()
        {
            try
            {
                lock (lockObj)
                    writer?.Flush();
            }
            catch { }
        }

        public static string GetLogPath()
        {
            Initialize();
            return logFilePath;
        }
    }
}
