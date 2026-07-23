using Newtonsoft.Json;
using NovaPointLibrary.Commands.Authentication;
using NovaPointLibrary.Commands.Utilities;
using NovaPointLibrary.Core.Authentication;


namespace NovaPointLibrary.Core.Settings
{
    public class AppConfig
    {
        private static readonly string _NpLocalAppFolder = AppFolders.GetConfigFolder();

        public List<AppClientConfidentialProperties> ListAppClientConfidentialProperties { get; set; } = [];
        public List<AppClientPublicProperties> ListAppClientPublicProperties { get; set; } = [];

        internal AppConfig() { }

        internal static string GetLocalAppPath()
        {
            string localAppPath = Path.Combine(_NpLocalAppFolder, VersionControl.GetVersion());
            System.IO.Directory.CreateDirectory(localAppPath);

            return localAppPath;
        }

        private static string GetSettingsPath()
        {
            string settingsFile = Path.Combine(GetLocalAppPath(), "user.config");
            return settingsFile;
        }

        public static AppConfig GetSettings()
        {
            AppConfig appSettings;

            AppConfig.RemoveLegacyData();

            string settingsFile = GetSettingsPath();

            if (File.Exists(settingsFile))
            {
                try
                {
                    string json = File.ReadAllText(settingsFile);
                    appSettings = JsonConvert.DeserializeObject<AppConfig>(json) ?? throw new InvalidOperationException("Failed to deserialize JSON.");
                }
                catch
                {
                    appSettings = new();
                }

            }
            else
            {
                appSettings = new();
            }

            return appSettings;
        }

        public IAppClientProperties GetOriginalSettings(IAppClientProperties clientProperties)
        {
            if (clientProperties is AppClientConfidentialProperties confidentialProperties)
            {
                return ListAppClientConfidentialProperties.Find(p => p.Id == confidentialProperties.Id)
                    ?? throw new InvalidOperationException($"Confidential app (Id '{confidentialProperties.Id}', ClientId '{confidentialProperties.ClientId}') does not exist in settings.");
            }

            else if (clientProperties is AppClientPublicProperties publicProperties)
            {
                return ListAppClientPublicProperties.Find(p => p.Id == publicProperties.Id)
                    ?? throw new InvalidOperationException($"Public app (Id '{publicProperties.Id}', ClientId '{publicProperties.ClientId}') does not exist in settings.");
            }
            throw new ArgumentException("App properties is neither public nor confidential.", nameof(clientProperties));
        }

        public async Task RemoveApp(IAppClientProperties clientProperties)
        {
            if (clientProperties is AppClientConfidentialProperties confidentialProperties)
            {
                ListAppClientConfidentialProperties.RemoveAll(p => p.Id == confidentialProperties.Id);
            }
            else if (clientProperties is AppClientPublicProperties publicProperties)
            {
                ListAppClientPublicProperties.RemoveAll(p => p.Id == publicProperties.Id);

                // Clear the token cache for the removed app, unless another saved app still uses the same ClientId.
                bool clientIdStillInUse = ListAppClientPublicProperties.Any(p => p.ClientId == publicProperties.ClientId);
                if (!clientIdStillInUse)
                {
                    await TokenCacheHelper.RemoveCache(new[] { publicProperties.ClientId });
                }
            }
            SaveSettings();
        }

        public async Task SaveSettings(IAppClientProperties clientProperties)
        {
            clientProperties.ValidateProperties();

            if (clientProperties is AppClientConfidentialProperties confidentialProperties)
            {
                int index = ListAppClientConfidentialProperties.FindIndex(p => p.Id == confidentialProperties.Id);
                if (index != -1) { ListAppClientConfidentialProperties[index] = confidentialProperties.Clone(); }
                else { ListAppClientConfidentialProperties.Add(confidentialProperties); }
            }

            else if (clientProperties is AppClientPublicProperties publicProperties)
            {
                int index = ListAppClientPublicProperties.FindIndex(p => p.Id == publicProperties.Id);
                if (index != -1) { ListAppClientPublicProperties[index] = publicProperties.Clone(); }
                else { ListAppClientPublicProperties.Add(publicProperties); }

                if (!publicProperties.CachingToken)
                {
                    await TokenCacheHelper.RemoveCache(new[] { publicProperties.ClientId });
                }
            }

            SaveSettings();
        }

        private void SaveSettings()
        {
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(GetSettingsPath(), json);
        }

        public static async Task RemoveTokenCache()
        {
            var clientIds = GetSettings().ListAppClientPublicProperties.Select(p => p.ClientId);
            await TokenCacheHelper.RemoveCache(clientIds);
        }

        private static void RemoveLegacyData()
        {
            string localAppPathFolderData = GetLocalAppPath();

            var msalcache = Path.Combine(AppFolders.GetConfigFolder(), $"msal1");

            string[] localAppPathFolders = System.IO.Directory.GetDirectories(_NpLocalAppFolder);
            foreach (var folderPath in localAppPathFolders)
            {
                if (!String.Equals(localAppPathFolderData, folderPath) && System.IO.Directory.Exists(folderPath))
                {
                    if (String.Equals(msalcache, folderPath)) { continue; }
                    System.IO.Directory.Delete(folderPath, recursive: true);
                }
            }
        }
    }
}
