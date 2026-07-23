using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using NovaPointLibrary.Core.Logging;
using NovaPointLibrary.Core.Settings;
using System.Text.RegularExpressions;

namespace NovaPointLibrary.Commands.Authentication
{
    internal class TokenCacheHelper
    {
        private static readonly int s_version = 1;
        
        // Matches the per-version cache subfolder names (e.g. "V1"); group 1 is the version number.
        private static readonly Regex s_cacheVersionFolderRegex = new(@"^V(\d+)$");

        private static readonly string s_msalFolder = Path.Combine(AppFolders.GetCacheFolder(), "msal");
        private static readonly string s_cacheFilePath = Path.Combine(s_msalFolder, $"V{s_version}", "msal.cache");

        private static readonly string s_cacheFileName = Path.GetFileName(s_cacheFilePath);
        private static readonly string? s_cacheDir = Path.GetDirectoryName(s_cacheFilePath);


        private static readonly string s_keyChainServiceName = "NovaPoint";
        private static readonly string s_keyChainAccountName = "MSALCache";

        private static readonly string s_linuxKeyRingSchema = "com.github.barbarur.novapoint.tokencache";
        private static readonly string s_linuxKeyRingCollection = MsalCacheHelper.LinuxKeyRingDefaultCollection;
        private static readonly string s_linuxKeyRingLabel = "MSAL token cache for NovaPoint.";
        private static readonly KeyValuePair<string, string> s_linuxKeyRingAttr1 = new KeyValuePair<string, string>("Version", $"{s_version}");
        private static readonly KeyValuePair<string, string> s_linuxKeyRingAttr2 = new KeyValuePair<string, string>("ProductGroup", "NovaPoint");

        internal static async Task<MsalCacheHelper?> GetCache(ILogger? logger = null)
        {
            var storageProperties =

                new StorageCreationPropertiesBuilder(s_cacheFileName, s_cacheDir)
                .WithLinuxKeyring(
                    s_linuxKeyRingSchema,
                    s_linuxKeyRingCollection,
                    s_linuxKeyRingLabel,
                    s_linuxKeyRingAttr1,
                    s_linuxKeyRingAttr2)
                .WithMacKeyChain(
                    s_keyChainServiceName,
                    s_keyChainAccountName)
                .Build();

            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);

            try
            {
                cacheHelper.VerifyPersistence();
            }
            catch (MsalCachePersistenceException ex)
            {
                logger?.Info(nameof(TokenCacheHelper),
                    $"WARNING: OS secret store unavailable; tokens will NOT be persisted this session. {ex.Message}");
                return null;
            }

            return cacheHelper;
        }

        internal static async Task RemoveCache(IEnumerable<Guid> clientIds, ILogger? logger = null)
        {
            try
            {
                var cacheHelper = await GetCache(logger);
                if (cacheHelper is null)
                {
                    // Persistence unavailable -> nothing was ever persisted; nothing to clear.
                    return;
                }

                foreach (var clientId in clientIds.Distinct())
                {
                    var app = PublicClientApplicationBuilder.Create(clientId.ToString()).Build();
                    cacheHelper.RegisterCache(app.UserTokenCache);

                    var accounts = await app.GetAccountsAsync();
                    foreach (var account in accounts)
                    {
                        await app.RemoveAsync(account);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Info(nameof(TokenCacheHelper),
                    $"WARNING: could not clear token cache. {ex.Message}");
            }
        }
        
        
        internal static void RemoveLegacyCaches(ILogger? logger = null)
        {
            try
            {
                if (!System.IO.Directory.Exists(s_msalFolder)) { return; }

                foreach (var folderPath in System.IO.Directory.GetDirectories(s_msalFolder))
                {
                    var match = s_cacheVersionFolderRegex.Match(Path.GetFileName(folderPath));
                    if (!match.Success) { continue; }

                    if (int.TryParse(match.Groups[1].Value, out int version) && version < s_version)
                    {
                        System.IO.Directory.Delete(folderPath, recursive: true);
                        logger?.Info(nameof(TokenCacheHelper), $"Removed stale MSAL cache '{folderPath}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Info(nameof(TokenCacheHelper),
                    $"WARNING: could not remove stale token cache(s). {ex.Message}");
            }
        }
    }
}
