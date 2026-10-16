using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace LCIOS
{
    // Mirrors the Unity log into Documents/LethalCompany-iOS.log (previous run: LethalCompany-iOS-prev.log).
    // Pulled off the phone with Files / Filza. Never throws: logging must not be the thing that crashes.
    public static class Log
    {
        public const string FileName = "LethalCompany-iOS.log";
        public const string PrevFileName = "LethalCompany-iOS-prev.log";
        const string CleanExitMarker = "=== clean exit ===";

        static StreamWriter s_Writer;
        static readonly object s_Lock = new object();
        public static string FilePath { get; private set; }

        public static void Init(string dir)
        {
            try
            {
                string path = Path.Combine(dir, FileName);
                string prev = Path.Combine(dir, PrevFileName);
                if (File.Exists(path))
                {
                    if (File.Exists(prev)) File.Delete(prev);
                    File.Move(path, prev);
                }
                FilePath = path;
                s_Writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                s_Writer.AutoFlush = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LCIOS] cannot open log file: " + e.Message);
            }
            Application.logMessageReceivedThreaded += OnLog;
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Write("E", "UNHANDLED EXCEPTION (terminating=" + e.IsTerminating + "): " + e.ExceptionObject);
            Application.quitting += () => Write("I", CleanExitMarker);
        }

        public static void Info(string msg) => Write("I", msg);
        public static void Warn(string msg) => Write("W", msg);
        public static void Error(string msg) => Write("E", msg);

        static void OnLog(string condition, string trace, LogType type)
        {
            if (type == LogType.Log) Write("I", condition);
            else if (type == LogType.Warning) Write("W", condition);
            else Write("E", condition + (string.IsNullOrEmpty(trace) ? "" : "\n" + trace));
        }

        static void Write(string level, string msg)
        {
            try
            {
                lock (s_Lock)
                {
                    if (s_Writer == null) return;
                    s_Writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}");
                }
            }
            catch (Exception) { }
        }
    }
}
