﻿using CamlBuilder;
using Microsoft.Graph;
using Microsoft.Online.SharePoint.TenantAdministration;
using Microsoft.SharePoint.Client;
using NovaPoint.Commands.Site;
using NovaPointLibrary.Commands.Authentication;
using NovaPointLibrary.Commands.AzureAD;
using NovaPointLibrary.Commands.SharePoint.User;
using NovaPointLibrary.Commands.Site;
using PnP.Core.Model.SharePoint;
using PnP.Framework.Provisioning.Model;
using System.Dynamic;
using System.Linq;
using System.Text;

namespace NovaPointLibrary.Solutions.Report
{
    // TO BE DEPRECATED ONCE SiteAllReport IS IN MATURE
    public class PermissionSiteAccessSiteAll
    {
        private LogHelper _logHelper;
        private readonly Commands.Authentication.AppInfo _appInfo;

        private readonly string AdminUPN = "";
        private readonly bool RemoveAdmin = false;

        private readonly bool IncludeAdmins = false;
        private readonly bool IncludeSiteAccess = false;

        private readonly bool IncludePersonalSite = false;
        private readonly bool IncludeShareSite = true;
        private readonly bool GroupIdDefined = false;

        private readonly bool IncludeSubsites = false;

        public PermissionSiteAccessSiteAll(Action<LogInfo> uiAddLog, Commands.Authentication.AppInfo appInfo, PermissionSiteAccessSiteAllParameters parameters)
        {
            // Baic parameters required for all reports
            _logHelper = new(uiAddLog, "Reports", GetType().Name);
            _appInfo = appInfo;
            
            AdminUPN = parameters.AdminUPN;
            RemoveAdmin = parameters.RemoveAdmin;
            
            IncludeAdmins = parameters.IncludeAdmins;
            IncludeSiteAccess = parameters.IncludeSiteAccess;

            IncludeShareSite = parameters.IncludeShareSite;
            IncludePersonalSite = parameters.IncludePersonalSite;
            GroupIdDefined = parameters.GroupIdDefined;

            IncludeSubsites = parameters.IncludeSubsites;
        }
        public async Task RunAsync()
        {
            try
            {
                if (String.IsNullOrWhiteSpace(AdminUPN))
                {
                    string message = "FORM INCOMPLETED: Admin UPN cannot be empty";
                    Exception ex = new(message);
                    throw ex;
                }
                else if (!IncludeShareSite && GroupIdDefined)
                {
                    string message = $"FORM INCOMPLETED: If you want to get GroupIdDefined Sites, you want to include ShareSites";
                    Exception ex = new(message);
                    throw ex;
                }
                else
                {
                    await RunScriptAsync();
                }
            }
            catch (Exception ex)
            {
                _logHelper.ScriptFinishErrorNotice(ex);
            }
        }
        private async Task RunScriptAsync()
        {
            _logHelper.ScriptStartNotice();

            if (_appInfo.CancelToken.IsCancellationRequested) { _appInfo.CancelToken.ThrowIfCancellationRequested(); };

            string aadAccessToken = await new GetAccessToken(_logHelper, _appInfo).GraphInteractiveAsync();
            string? spoAdminAccessToken = await new GetAccessToken(_logHelper, _appInfo).SpoInteractiveAsync(_appInfo._adminUrl);
            string? spoRootPersonalSiteAccessToken = String.Empty;
            string? spoRootShareSiteAccessToken = String.Empty;
            if (IncludeAdmins || IncludeSubsites)
            {

                if (IncludePersonalSite) { spoRootPersonalSiteAccessToken = await new GetAccessToken(_logHelper, _appInfo).SpoInteractiveAsync(_appInfo._rootPersonalUrl); }
                if (IncludeShareSite) { spoRootShareSiteAccessToken = await new GetAccessToken(_logHelper, _appInfo).SpoInteractiveAsync(_appInfo._rootSharedUrl); }

            }

            List<SiteProperties> collSiteCollections = new GetSiteCollection(_logHelper, spoAdminAccessToken).CSOM_AdminAll(_appInfo._adminUrl, IncludePersonalSite, GroupIdDefined);
            collSiteCollections.RemoveAll(s => s.Title == "" || s.Template.Contains("Redirect"));
            if (!IncludePersonalSite) { collSiteCollections.RemoveAll(s => s.Template.Contains("SPSPERS")); }
            if (!IncludeShareSite) { collSiteCollections.RemoveAll(s => !s.Template.Contains("SPSPERS")); }

            double counter = 0;
            double counterStep = 1 / collSiteCollections.Count;
            foreach (SiteProperties oSiteCollection in collSiteCollections)
            {
                if (_appInfo.CancelToken.IsCancellationRequested) { _appInfo.CancelToken.ThrowIfCancellationRequested(); };
                
                counter++;
                double progress = Math.Round(counter * 100 / collSiteCollections.Count, 2);
                _logHelper.AddProgressToUI(progress);
                _logHelper.AddLogToUI($"Processing Site Collection '{oSiteCollection.Title}'");

                string currentSiteAccessToken = oSiteCollection.Url.Contains("-my.sharepoint.com") ? spoRootPersonalSiteAccessToken : spoRootShareSiteAccessToken;

                PermissionSiteAccessSiteAllRecordNEW siteCollRecord = new(oSiteCollection);

                try
                {

                    if (IncludeAdmins || IncludeSiteAccess || IncludeSubsites)
                    {
                        new SetSiteCollectionAdmin(_logHelper, spoAdminAccessToken, _appInfo._domain).Add(AdminUPN, oSiteCollection.Url);
                    }

                    if (IncludeAdmins)
                    {
                        await GetAdmins(currentSiteAccessToken, siteCollRecord, aadAccessToken);
                    }
                    
                    if (IncludeSiteAccess)
                    {
                        //Web siteWeb = new GetSite(_logHelper, _appInfo, currentSiteAccessToken).CSOMWithRoles(oSiteCollection.Url); // Adjust requirements 
                        Web siteWeb = new Commands.SharePoint.Site.GetSPOSite(_logHelper,_appInfo, currentSiteAccessToken).CSOMWithRoles(oSiteCollection.Url);
                        await GetSitePermissions(currentSiteAccessToken, aadAccessToken, siteWeb, siteCollRecord);
                    }

                    if (!IncludeAdmins && !IncludeSiteAccess)
                    {
                        AddNEWRecordToCSV(siteCollRecord);
                    }

                    if (IncludeSubsites)
                    {
                        var collSubsites = new GetSubsite(_logHelper, _appInfo, currentSiteAccessToken).CsomAllSubsitesWithRolesAndSiteDetails(oSiteCollection.Url);
                        foreach (var oSubsite in collSubsites)
                        {
                            PermissionSiteAccessSiteAllRecordNEW siteRecord = new(oSubsite);
                            if (oSubsite.HasUniqueRoleAssignments)
                            {
                                await GetSitePermissions(currentSiteAccessToken, aadAccessToken, oSubsite, siteRecord);
                            }
                            else
                            {
                                PermissionSiteAccessSiteAllPermission permissionsRecord = new("Inheriting Permissions", "", "", "");
                                AddNEWRecordToCSV(siteRecord, permissionsRecord);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                    _logHelper.AddLogToUI($"Error processing Site Collection '{oSiteCollection.Url}'");
                    _logHelper.AddLogToTxt($"Exception: {ex.Message}");
                    _logHelper.AddLogToTxt($"Trace: {ex.StackTrace}");
                    //PermissionSiteAccessSiteAllRecord record = new(oSiteCollection, ex.Message);
                    //record.AddToCSV(_logHelper);

                    AddNEWRecordToCSV(siteCollRecord, remarks: ex.Message);
                }
            }

            _logHelper.ScriptFinishSuccessfulNotice();

        }
        private async Task GetAdmins(string spoAccessToken, PermissionSiteAccessSiteAllRecordNEW siteRecord, string aadAccessToken)
        {
            if (_appInfo.CancelToken.IsCancellationRequested) { _appInfo.CancelToken.ThrowIfCancellationRequested(); };
            _logHelper.AddLogToTxt($"Getting Admins for Site Collestion '{siteRecord.Url}'");

            string accessType = "Direct Permissions";
            string permissionLevels = "Site Collection Administrator";

            IEnumerable<Microsoft.SharePoint.Client.User> collSiteCollAdmins = new GetSiteCollectionAdmin(_logHelper, spoAccessToken).Csom(siteRecord.Url);

            var users = String.Join(" ", collSiteCollAdmins.Where(sca => sca.PrincipalType.ToString() == "User").Select(sca => sca.UserPrincipalName).ToList());
            PermissionSiteAccessSiteAllPermission usersPermissionsRecord = new(accessType, "User", users, permissionLevels);
            AddNEWRecordToCSV(siteRecord, usersPermissionsRecord);

            var taskSecurityGroups = collSiteCollAdmins.Where(sca => sca.PrincipalType.ToString() == "SecurityGroup").ToList().Select(sg => GetSecurityGroupUsersAsync(aadAccessToken, sg.Title, sg.AadObjectId.NameId, siteRecord, accessType, "", permissionLevels));
            await Task.WhenAll(taskSecurityGroups);
            
        }

        private async Task GetSitePermissions(string spoAccessToken, string aadAccessToken, Web web, PermissionSiteAccessSiteAllRecordNEW siteRecord)
        {
            if (_appInfo.CancelToken.IsCancellationRequested) { _appInfo.CancelToken.ThrowIfCancellationRequested(); };
            _logHelper.AddLogToTxt($"Start getting Site Permissions for Site '{siteRecord.Url}'");

            foreach (var role in web.RoleAssignments)
            {
                _logHelper.AddLogToTxt($"Gettig Site Permissions for {role.Member.PrincipalType} '{role.Member.Title}'");

                string accessType = "Direct Permissions";
                var permissionLevels = GetPermissionLevels(role.RoleDefinitionBindings);

                if (String.IsNullOrWhiteSpace(permissionLevels))
                { 
                    continue;
                }
                else if (role.Member.PrincipalType.ToString() == "User")
                {
                    PermissionSiteAccessSiteAllPermission usersPermissionsRecord = new(accessType, "User", role.Member.LoginName, permissionLevels);
                    AddNEWRecordToCSV(siteRecord, usersPermissionsRecord);
                }
                else if (role.Member.PrincipalType.ToString() == "SharePointGroup")
                {
                    await GetSharepointGroupUsers(spoAccessToken, aadAccessToken, siteRecord, role.Member.Title, permissionLevels);
                }
                else if (role.Member.PrincipalType.ToString() == "SecurityGroup")
                {
                    await GetSecurityGroupUsersAsync(aadAccessToken, role.Member.Title, role.Member.LoginName, siteRecord, accessType, "", permissionLevels);
                }
            }
            _logHelper.AddLogToTxt($"Finish Site Permissions for Site '{siteRecord.Url}'");
        }

        private string GetPermissionLevels(RoleDefinitionBindingCollection roleDefinitionsCollection)
        {
            if (_appInfo.CancelToken.IsCancellationRequested) { _appInfo.CancelToken.ThrowIfCancellationRequested(); };
            _logHelper.AddLogToTxt($"Start getting Permission Levels");

            StringBuilder sb = new();
            foreach (var roleDefinition in roleDefinitionsCollection)
            {
                if(roleDefinition.Name == "Limited Access" || roleDefinition.Name == "Web-Only Limited Access") { continue; }
                else
                {
                    sb.Append($"{roleDefinition.Name} ");
                }
            }

            string permissionLevels = "";
            if (sb.Length > 0) { permissionLevels = sb.ToString().Remove(sb.Length - 1); }

            _logHelper.AddLogToTxt($"Finish getting Permission Levels");
            return permissionLevels;

        }

        private async Task GetSharepointGroupUsers(string spoAccessToken, string aadAccessToken, PermissionSiteAccessSiteAllRecordNEW siteRecord, string groupName, string permissionLevels)
        {
            if (_appInfo.CancelToken.IsCancellationRequested) { _appInfo.CancelToken.ThrowIfCancellationRequested(); };
            _logHelper.AddLogToTxt($"Start getting members of SharePoint Group {groupName}");

            string accessType = $"SharePoint Group '{groupName}'";

            Microsoft.SharePoint.Client.UserCollection groupMembers = new GetSPOGroupMember(_logHelper, _appInfo, spoAccessToken).CSOMAllMembers(siteRecord.Url, groupName);

            var users = String.Join(" ", groupMembers.Where(gm => gm.PrincipalType.ToString() == "User").Select(m => m.UserPrincipalName).ToList());
            PermissionSiteAccessSiteAllPermission usersPermissionsRecord = new(accessType, "User", users, permissionLevels);
            AddNEWRecordToCSV(siteRecord, usersPermissionsRecord);

            var taskSecurityGroups = groupMembers.Where(gm => gm.PrincipalType.ToString() == "SecurityGroup").ToList().Select(sca => GetSecurityGroupUsersAsync(aadAccessToken, sca.Title, sca.AadObjectId.NameId, siteRecord, accessType, "", permissionLevels));
            await Task.WhenAll(taskSecurityGroups);

            _logHelper.AddLogToTxt($"Finish getting members of SharePoint Group {groupName}");
        }

        private async Task GetSecurityGroupUsersAsync(string aadAccessToken, string groupName, string groupId, PermissionSiteAccessSiteAllRecordNEW siteRecord, string accessType, string accountType, string permissionLevels)
        {
            if (_appInfo.CancelToken.IsCancellationRequested) { _appInfo.CancelToken.ThrowIfCancellationRequested(); };
            _logHelper.AddLogToTxt($"Start getting members of Security Group {groupName} with ID {groupId}");

            if (groupId.Contains("c:0t.c|tenant|")) { groupId = groupId.Substring(groupId.IndexOf("c:0t.c|tenant|") + 14); }
            if (groupId.Contains("c:0o.c|federateddirectoryclaimprovider|")) { groupId.Substring(groupId.IndexOf("c:0o.c|federateddirectoryclaimprovider|") + 39); }
            if (groupId.Contains("_o")) { groupId = groupId.Substring(0, groupId.IndexOf("_o")); }
            
            var collOwnersMembers = await new GetAzureADGroup(_logHelper, _appInfo, aadAccessToken).GraphOwnersAndMembersAsync(groupId);

            accountType += $"Security Group {groupName}";

            //foreach (var member in collOwnersMembers ) 
            //{
            //    _logHelper.AddLogToUI($"User Type: {member.Type} {member.UserPrincipalName} from {groupName}");
            //    //_logHelper.AddLogToUI($"User UPN: {member.UserPrincipalName}");
            //}

            //var users = String.Join(" ", collSiteCollAdmins.Where(sca => sca.PrincipalType.ToString() == "User").Select(sca => sca.Title).ToList());
            var users = String.Join(" ", collOwnersMembers.Where(com => com.Type.ToString() == "user").Select(com => com.UserPrincipalName).ToList());
            PermissionSiteAccessSiteAllPermission usersPermissionsRecord = new(accessType, accountType, users, permissionLevels);
            AddNEWRecordToCSV(siteRecord, usersPermissionsRecord);

            var taskSecurityGroups = collOwnersMembers.Where(com => com.Type.ToString() == "group").ToList().Select(sg => GetSecurityGroupUsersAsync(aadAccessToken, sg.DisplayName, sg.Id, siteRecord, accessType, $"{accountType} holds ", permissionLevels));
            await Task.WhenAll(taskSecurityGroups);

            _logHelper.AddLogToTxt($"Finish getting members of Security Group {groupName}with ID {groupId}");
        }


        //private async void GetSecurityGroupUsers(string aadAccessToken, string objectId, string accessType, string accountType, PermissionSiteAccessSiteAllRecord record)
        //{
        //    var groupUsers = await new GetAzureADGroup(_logHelper, _appInfo, aadAccessToken).GraphOwnersAndMembersAsync(objectId);

        //    StringBuilder sb = new();
        //    foreach (var oUser in groupUsers)
        //    {
        //        sb.Append(oUser.UserPrincipalName);
        //    }

        //    var permissionsRecord = new PermissionSiteAccessSiteAllPermission(accessType, accountType, sb.ToString(), "Site Collection Administrator");
        //    //AddRecordToCSV(true, siteCollection, subsiteWeb, permissionsRecord);

        //}

        //private void AddSiteRecordToCSV(SiteProperties? siteCollection, PermissionSiteAccessSiteAllPermission permissionRecord = null, string remarks = "")
        //{
        //    AddRecordToCSV(siteCollection: siteCollection, permissionRecord: permissionRecord, remarks: remarks);
        //}

        //private void AddSiteRecordToCSV(Web subsiteWeb, PermissionSiteAccessSiteAllPermission permissionRecord = null, string remarks = "")
        //{
        //    AddRecordToCSV(subsiteWeb: subsiteWeb, permissionRecord: permissionRecord, remarks: remarks);
        //}

        //private void AddRecordToCSV(SiteProperties? siteCollection = null, Web? subsiteWeb = null, PermissionSiteAccessSiteAllPermission permissionRecord = null, string remarks = "")
        //{

        //    dynamic record = new ExpandoObject();
        //    record.Title = siteCollection != null ? siteCollection?.Title : subsiteWeb?.Title;
        //    record.SiteUrl = siteCollection != null ? siteCollection?.Url : subsiteWeb?.Url;
        //    record.GroupId = siteCollection != null ? siteCollection?.GroupId.ToString() : string.Empty;
        //    record.Tempalte = siteCollection != null ? siteCollection?.Template : subsiteWeb.WebTemplate;


        //    record.StorageQuotaGB = siteCollection != null ? string.Format("{0:N2}", Math.Round(siteCollection.StorageMaximumLevel / Math.Pow(1024, 3), 2)) : string.Empty;
        //    record.StorageUsedGB = siteCollection != null ? string.Format("{0:N2}", Math.Round(siteCollection.StorageUsage / Math.Pow(1024, 3), 2)) : string.Empty; // ADD CONSUMPTION FOR SUBSITES
        //    record.storageWarningLevelGB = siteCollection != null ? string.Format("{0:N2}", Math.Round(siteCollection.StorageWarningLevel / Math.Pow(1024, 3), 2)) : string.Empty;

        //    record.IsHubSite = siteCollection != null ? siteCollection?.IsHubSite.ToString() : string.Empty;
        //    record.LastContentModifiedDate = siteCollection != null ? siteCollection?.LastContentModifiedDate.ToString() : subsiteWeb?.LastItemModifiedDate.ToString();
        //    record.LockState = siteCollection != null ? siteCollection?.LockState.ToString() : string.Empty;

        //    if (IncludeAdmins && IncludeSiteAccess)
        //    {
        //        record.AccessType = permissionRecord != null ? permissionRecord._accessType : string.Empty;
        //        record.AccountType = permissionRecord != null ? permissionRecord._accounteType : string.Empty;
        //        record.User = permissionRecord != null ? permissionRecord._user : string.Empty;
        //        record.PermissionsLevel = permissionRecord != null ? permissionRecord._permissionLevel : string.Empty;
        //    }

        //    record.Remarks = remarks;

        //    _logHelper.AddRecordToCSV(record);
        //}

        private void AddNEWRecordToCSV(PermissionSiteAccessSiteAllRecordNEW site, PermissionSiteAccessSiteAllPermission? permissionRecord = null, string remarks = "")
        {

            dynamic record = new ExpandoObject();
            record.Title = site.Title;
            //record.SiteUrl = site.Url;
            //record.GroupId = site.GroupId;
            //record.Tempalte = site.Tempalte;


            //record.StorageQuotaGB = site.StorageQuotaGB;
            //record.StorageUsedGB = site.StorageUsedGB;
            //record.storageWarningLevelGB = site.StorageWarningLevelGB;

            //record.IsHubSite = site.IsHubSite;
            //record.LastContentModifiedDate = site.LastContentModifiedDate;
            //record.LockState = site.LockState;

            if (IncludeAdmins || IncludeSiteAccess)
            {
                record.AccessType = permissionRecord != null ? permissionRecord._accessType : string.Empty;
                record.AccountType = permissionRecord != null ? permissionRecord._accounteType : string.Empty;
                record.User = permissionRecord != null ? permissionRecord._user : string.Empty;
                record.PermissionsLevel = permissionRecord != null ? permissionRecord._permissionLevel : string.Empty;
            }

            record.Remarks = remarks;

            _logHelper.AddRecordToCSV(record);
        }
    }

    internal class PermissionSiteAccessSiteAllRecordNEW
    {
        internal string Title;
        internal string Url;
        internal string GroupId = string.Empty;
        internal string Tempalte;

        internal string StorageQuotaGB = string.Empty;
        internal string StorageUsedGB = string.Empty;
        internal string StorageWarningLevelGB = string.Empty;

        internal string IsHubSite = string.Empty;
        internal string LastContentModifiedDate = string.Empty;
        internal string LockState = string.Empty;

        internal string AccessType = string.Empty;
        internal string AccountType = string.Empty;
        internal string User = string.Empty;
        internal string PermissionLevel = string.Empty;

        internal string Remarks = string.Empty;

        internal PermissionSiteAccessSiteAllRecordNEW(SiteProperties siteCollection)
        {
            Title = siteCollection.Title;
            Url = siteCollection.Url;
            GroupId = siteCollection.GroupId.ToString();
            Tempalte = siteCollection.Template;

            StorageQuotaGB = string.Format("{0:N2}", Math.Round(siteCollection.StorageMaximumLevel / Math.Pow(1024, 3), 2));
            StorageUsedGB = string.Format("{0:N2}", Math.Round(siteCollection.StorageUsage / Math.Pow(1024, 3), 2));
            StorageWarningLevelGB = string.Format("{0:N2}", Math.Round(siteCollection.StorageWarningLevel / Math.Pow(1024, 3), 2));

            IsHubSite = siteCollection.IsHubSite.ToString();
            LastContentModifiedDate = siteCollection.LastContentModifiedDate.ToString();
            LockState = siteCollection.LockState.ToString();
        }

        internal PermissionSiteAccessSiteAllRecordNEW(Web subsiteWeb)
        {
            Title = subsiteWeb.Title;
            Url = subsiteWeb.Url;
            Tempalte = subsiteWeb.WebTemplate;

            LastContentModifiedDate = subsiteWeb.LastItemModifiedDate.ToString();
        }
    }







    //internal class PermissionSiteAccessSiteAllRecord
    //{
    //    internal string Title;
    //    internal string SiteUrl;
    //    internal string GroupId = string.Empty;
    //    internal string Tempalte;

    //    internal string StorageQuotaGB = string.Empty;
    //    internal string StorageUsedGB = string.Empty;
    //    internal string StorageWarningLevelGB = string.Empty;
        
    //    internal string IsHubSite = string.Empty;
    //    internal string LastContentModifiedDate = string.Empty;
    //    internal string LockState = string.Empty;

    //    internal string AccessType = string.Empty;
    //    internal string AccountType = string.Empty;
    //    internal string User = string.Empty;
    //    internal string PermissionLevel = string.Empty;

    //    internal string Remarks = string.Empty;

    //    internal PermissionSiteAccessSiteAllRecord(SiteProperties siteCollection, string remarks = "")
    //    {
    //        Title = siteCollection.Title;
    //        SiteUrl = siteCollection.Url;
    //        GroupId = siteCollection.GroupId.ToString();
    //        Tempalte = siteCollection.Template;

    //        StorageQuotaGB = string.Format("{0:N2}", Math.Round(siteCollection.StorageMaximumLevel / Math.Pow(1024, 3), 2));
    //        StorageUsedGB = string.Format("{0:N2}", Math.Round(siteCollection.StorageUsage / Math.Pow(1024, 3), 2));
    //        StorageWarningLevelGB = string.Format("{0:N2}", Math.Round(siteCollection.StorageWarningLevel / Math.Pow(1024, 3), 2));

    //        IsHubSite = siteCollection.IsHubSite.ToString();
    //        LastContentModifiedDate = siteCollection.LastContentModifiedDate.ToString();
    //        LockState = siteCollection.LockState.ToString();

    //        AddRemarks(remarks);
    //    }

    //    internal PermissionSiteAccessSiteAllRecord(Web subsiteWeb, string remarks = "")
    //    {
    //        Title = subsiteWeb.Title;
    //        SiteUrl = subsiteWeb.Url;
    //        Tempalte = subsiteWeb.WebTemplate;

    //        LastContentModifiedDate = subsiteWeb.LastItemModifiedDate.ToString();

    //        AddRemarks(remarks);
    //    }

    //    internal void AddPermissions(PermissionSiteAccessSiteAllPermission permissionRecord = null)
    //    {
    //        AccessType = permissionRecord._accessType;
    //        AccountType = permissionRecord._accounteType;
    //        User = permissionRecord._user;
    //        PermissionLevel = permissionRecord._permissionLevel;
    //    }
        
    //    private void AddRemarks(string remarks)
    //    {
    //        Remarks = remarks;
    //    }

    //    internal void AddToCSV(LogHelper logHelper)
    //    {
    //        logHelper.AddRecordToCSV(this);
    //    }
    //}

    internal class PermissionSiteAccessSiteAllPermission
    {
        internal readonly string _accessType;
        internal readonly string _accounteType;
        internal readonly string _user;
        internal readonly string _permissionLevel;

        internal PermissionSiteAccessSiteAllPermission(string accessType, string accountType, string user, string permissionLevel)
        {
            _accessType = accessType;
            _accounteType = accountType;
            _user = user;
            _permissionLevel = permissionLevel;
        }
    }

    public class PermissionSiteAccessSiteAllParameters
    {
        public string AdminUPN { get; set; } = "";
        public bool RemoveAdmin { get; set; } = false;
        public bool IncludePersonalSite { get; set; } = false;
        public bool IncludeShareSite { get; set; } = true;
        public bool GroupIdDefined { get; set; } = false;
        public bool IncludeAdmins { get; set; } = false;
        public bool IncludeSiteAccess { get; set; } = false;
        public bool IncludeSubsites { get; set; } = false;
    }
}