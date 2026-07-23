using NovaPointLibrary.Commands.Authentication;

namespace NovaPointLibrary.Core.Settings
{
    public static class AppFolders
    {
        private const string AppName = "NovaPoint";

        // Windows: %LOCALAPPDATA%\NovaPoint\config
        // macOS/Linux: ~/.local/share/NovaPoint/config
        public static string GetConfigFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppName, "config");
        }

        // Windows: %LOCALAPPDATA%\NovaPoint\cache
        // macOS/Linux: $XDG_CACHE_HOME/NovaPoint  (falls back to ~/.cache/NovaPoint)
        public static string GetCacheFolder()
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "cache");
            }
            else
            {
                string baseCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                return Path.Combine(baseCache, AppName);
            }
        }

        // Windows/macOS: ~/Documents/NovaPoint
        // Linux: ~/Documents/NovaPoint  (MyDocuments returns $HOME on Unix — we append Documents manually)
        public static string GetOutputFolder()
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs) ||
                docs == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            {
                docs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Documents");
            }
            return Path.Combine(docs, AppName);
        }

        // To be triggered from UI
        public static void CleanUpLegacyFolders()
        {
            TokenCacheHelper.RemoveLegacyCaches();
            
            RemoveLegacyData();
        }
        
        private static void RemoveLegacyData()
        {
            string localAppPathFolderData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppName);

            if (!System.IO.Directory.Exists(localAppPathFolderData)) { return; }

            string cacheFolder = GetCacheFolder();
            string configFolder = GetConfigFolder();

            foreach (var folderPath in System.IO.Directory.GetDirectories(localAppPathFolderData))
            {
                // OrdinalIgnoreCase so a casing difference between the on-disk folder name and the
                // expected path doesn't cause the cache/config folder to be treated as legacy and deleted.
                if (String.Equals(folderPath, cacheFolder, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (String.Equals(folderPath, configFolder, StringComparison.OrdinalIgnoreCase)) { continue; }

                if (System.IO.Directory.Exists(folderPath))
                {
                    System.IO.Directory.Delete(folderPath, recursive: true);
                }
            }
        }
    }
}
