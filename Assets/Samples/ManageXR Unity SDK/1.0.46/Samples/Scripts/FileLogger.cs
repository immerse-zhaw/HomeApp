using System;
using System.IO;
using UnityEngine;

namespace MXR.SDK.Samples
{
    /// <summary>
    /// Simple file logger that writes logs to a text file
    /// </summary>
    public static class FileLogger
    {
        private static string logFilePath;
        private static bool initialized = false;

        public static string LogFilePath => logFilePath;

        private static void Initialize()
        {
            if (initialized) return;

            string logDirectory = Application.persistentDataPath;
            logFilePath = Path.Combine(logDirectory, "app_debug.log");
            
            // Create initial log entry
            try
            {
                string header = $"\n\n===== NEW SESSION: {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n";
                File.AppendAllText(logFilePath, header);
                Debug.Log($"[FileLogger] Log file created at: {logFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] Failed to initialize log file: {e.Message}");
            }

            initialized = true;
        }

        public static void Log(string message)
        {
            Initialize();

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}\n";

            try
            {
                File.AppendAllText(logFilePath, logEntry);
                Debug.Log(message); // Also log to Unity console
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] Failed to write log: {e.Message}");
            }
        }

        public static void LogError(string message)
        {
            Initialize();

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] ERROR: {message}\n";

            try
            {
                File.AppendAllText(logFilePath, logEntry);
                Debug.LogError(message); // Also log to Unity console
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] Failed to write error log: {e.Message}");
            }
        }

        public static void LogWarning(string message)
        {
            Initialize();

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] WARNING: {message}\n";

            try
            {
                File.AppendAllText(logFilePath, logEntry);
                Debug.LogWarning(message); // Also log to Unity console
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] Failed to write warning log: {e.Message}");
            }
        }

        public static string GetLogPath()
        {
            Initialize();
            return logFilePath;
        }
    }
}
