using System;
using System.Diagnostics;
using System.IO;

public static class DebugFileBootstrap
{
    private static TextWriterTraceListener _fileListener;

    public static string Hook(string appName = "HSED_2_0")
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(baseDir, appName, "logs");
        Directory.CreateDirectory(logDir);

        var logPath = Path.Combine(logDir, $"log_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.txt");

        var fs = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _fileListener = new TextWriterTraceListener(fs, "FileLogger");

        // Hier nur Trace verwenden
        Trace.Listeners.Clear();
        Trace.Listeners.Add(_fileListener);

        // optional zusätzlich Konsole
        Trace.Listeners.Add(new ConsoleTraceListener());

        Trace.AutoFlush = true;

        Trace.WriteLine($"[BOOT] Logging Datei: {logPath}");
        return logPath;
    }

    public static void FlushAndClose()
    {
        try { Trace.Flush(); } catch { }
        try { _fileListener?.Flush(); _fileListener?.Close(); } catch { }
    }
}
