

namespace NovaPointLibrary.Commands.Directory
{
    internal class DirectoryGroupUserEmails
    {
        internal Guid GroupID { get; set; }
        internal string GroupName { get; set; }
        internal bool IsOwners { get; set; }
        internal string AccountType { get; set; }
        internal string Users { get; set; }
        internal string Remarks { get; set; }

        internal DirectoryGroupUserEmails(Guid groupID, string groupName, bool isOwner, string user, string remarks = "")
            : this(groupID, groupName, isOwner, $"Directory Group '{groupName}' ({groupID})", user, remarks)
        { }

        private DirectoryGroupUserEmails(Guid groupID, string groupName, bool isOwner, string accountType, string user, string remarks)
        {
            GroupID = groupID;
            GroupName = groupName;
            IsOwners = isOwner;
            AccountType = accountType;
            Users = user;
            Remarks = remarks;
        }

        // Entra ID directory role. Members are tenant-wide and identical on every site,
        // so the role is reported instead of expanded.
        internal static DirectoryGroupUserEmails GetDirectoryRole(Guid roleId, string roleName, bool isOwner, string users)
        {
            return new(roleId, roleName, isOwner, $"Directory Role '{roleName}' ({roleId})", users, "");
        }

        // Claim with no directory object behind it, e.g. 'Everyone except external users'.
        // AccountType stays the bare title; solutions filter on it by substring.
        internal static DirectoryGroupUserEmails GetClaimPrincipal(string title, string users)
        {
            return new(Guid.Empty, title, false, title, users, "");
        }

    }
}
