using System;
using System.IO;

namespace EasySnapApp.Utils
{
    /// <summary>
    /// Centralized runtime paths for EasySnapApp.
    /// Ensures portability across install locations (portable zip, Program Files, network share).
    /// </summary>
    public static class AppPaths
    {
        // Root folder next to the EXE (portable/runtime artifacts)
        public static string AppRoot => AppDomain.CurrentDomain.BaseDirectory;

        // Exports folder next to the EXE
        public static string ExportsRoot => Path.Combine(AppRoot, "Exports");

        // Logs folder under Exports
        public static string LogsRoot => Path.Combine(ExportsRoot, "logs");

        // Session log file next to EXE (or change to LogsRoot if you prefer)
        public static string SessionLogPath => Path.Combine(AppRoot, "session_log.txt");

        // Per-user DB folder
        public static string UserDataRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasySnapApp", "Data");

        public static string UserDbPath => Path.Combine(UserDataRoot, "EasySnap.db");

        /// <summary>
        /// Ensure all runtime folders exist.
        /// Call this once at startup before any logging/export/thumbnail code runs.
        /// </summary>
        public static void EnsureRuntimeFolders()
        {
            Directory.CreateDirectory(ExportsRoot);
            Directory.CreateDirectory(LogsRoot);
            Directory.CreateDirectory(UserDataRoot);

            // Touch session log so it's always present (do not overwrite)
            if (!File.Exists(SessionLogPath))
                File.WriteAllText(SessionLogPath, $"EasySnap started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n");
        }
    }
}
