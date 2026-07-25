

namespace NovaPointLibrary.Commands.Directory
{
    // Display names only. Nothing here decides how a principal is resolved; that is
    // driven by the claim shape and the directory object type. Legacy and current role
    // names map to the same text so reports read the same on any tenant.
    internal static class DirectoryWellKnownPrincipal
    {
        private static readonly Dictionary<string, string> _usersDescription = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Everyone", "All internal and external users" },
            { "Everyone except external users", "All internal users" },
            { "All Company Members", "All internal users" },
            { "All Users (windows)", "All internal users" },

            { "Global Administrator", "Users with Global Admin role" },
            { "Company Administrator", "Users with Global Admin role" },
            { "SharePoint Administrator", "Users with SharePoint Admin role" },
            { "SharePoint Service Administrator", "Users with SharePoint Admin role" },

            { "ReadOnlyAccessToTenantAdminSite", "Users with read access to the Tenant Admin site" },
        };

        internal static string GetUsersDescription(string title)
        {
            if (!string.IsNullOrWhiteSpace(title) && _usersDescription.TryGetValue(title, out string? description))
            {
                return description;
            }

            return $"Unknown users on '{title}'";
        }
    }
}
