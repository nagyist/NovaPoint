using NovaPointLibrary.Core.Settings;

namespace NovaPointLibrary.Core.Logging;

public static class LogCrash
{
    private static readonly string s_crashFolder = Path.Combine(AppFolders.GetOutputFolder(), "CrashReport");
    
    public static string WriteCrashLog(Exception ex, string crashName)
    {
        string logFile = Path.Combine(s_crashFolder, $"{DateTime.Now:yyMMddHHmmss}{crashName}.Log");

        try
        {
            Directory.CreateDirectory(s_crashFolder);
            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort logging only; a failure here should not throw again.
        }

        return logFile;
    }
    
}