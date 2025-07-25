﻿using Microsoft.SharePoint.Client;
using Newtonsoft.Json;
using NovaPointLibrary.Commands.Authentication;
using NovaPointLibrary.Commands.SharePoint.Item;
using NovaPointLibrary.Commands.SharePoint.List;
using NovaPointLibrary.Commands.SharePoint.Site;
using NovaPointLibrary.Core.Logging;
using System.Linq.Expressions;

namespace NovaPointLibrary.Solutions.Report
{
    public class ShortcutODReport : ISolution
    {
        public static readonly string s_SolutionName = "OneDrive shortcut report";
        public static readonly string s_SolutionDocs = "https://github.com/Barbarur/NovaPoint/wiki/Solution-Report-ShortcutODReport";

        private ShortcutODReportParameters _param;
        private readonly LoggerSolution _logger;
        private readonly AppInfo _appInfo;

        private ShortcutODReport(LoggerSolution logger, AppInfo appInfo, ShortcutODReportParameters parameters)
        {
            _param = parameters;
            _logger = logger;
            _appInfo = appInfo;
        }

        public static async Task RunAsync(ShortcutODReportParameters parameters, Action<LogInfo> uiAddLog, CancellationTokenSource cancelTokenSource)
        {
            LoggerSolution logger = new(uiAddLog, "ShortcutODReport", parameters);

            try
            {
                AppInfo appInfo = await AppInfo.BuildAsync(logger, cancelTokenSource);

                await new ShortcutODReport(logger, appInfo, parameters).RunScriptAsync();

                logger.SolutionFinish();

            }
            catch (Exception ex)
            {
                logger.SolutionFinish(ex);
            }
        }

        private async Task RunScriptAsync()
        {
            _appInfo.IsCancelled();

            await foreach (var tenantItemRecord in new SPOTenantItemsCSOM(_logger, _appInfo, _param.TItemsParam).GetAsync())
            {
                _appInfo.IsCancelled();

                if (tenantItemRecord.Ex != null)
                {
                    ShortcutODReportRecord record = new(tenantItemRecord);
                    RecordCSV(record);
                    continue;
                }

                if (tenantItemRecord.Item == null || tenantItemRecord.List == null)
                {
                    ShortcutODReportRecord record = new(tenantItemRecord)
                    {
                        Remarks = "Item or List is null",
                    };
                    RecordCSV(record);
                    continue;
                }

                if (tenantItemRecord.List.BaseType != BaseType.DocumentLibrary) { continue; }

                if (tenantItemRecord.Item.FileSystemObjectType.ToString() == "Folder") { continue; }

                try
                {
                    var shortcutData = JsonConvert.DeserializeObject<OneDriveShortcutProperties>((string)tenantItemRecord.Item["A2ODExtendedMetadata"]);

                    ShortcutODReportRecord record = new(tenantItemRecord);
                    record.AddTargetSite(shortcutData.riwu);
                    RecordCSV(record);
                }
                catch (Exception ex)
                {
                    _logger.Error(GetType().Name, "Item", (string)tenantItemRecord.Item["FileRef"], ex);

                    ShortcutODReportRecord record = new(tenantItemRecord, ex.Message);
                    RecordCSV(record);
                }
            }
        }

        private void RecordCSV(ShortcutODReportRecord record)
        {
            _logger.RecordCSV(record);
        }
    }

    internal class ShortcutODReportRecord : ISolutionRecord
    {
        internal string SiteUrl { get; set; } = String.Empty;
        internal string ListTitle { get; set; } = String.Empty;
        internal string ListType { get; set; } = String.Empty;

        internal string ItemID { get; set; } = String.Empty;
        internal string ShortcutName { get; set; } = String.Empty;
        internal string ShortcutPath { get; set; } = String.Empty;

        internal string TargetSite { get; set; } = String.Empty;

        internal string Remarks { get; set; } = String.Empty;

        internal ShortcutODReportRecord(SPOTenantItemRecord tenantItemRecord, string remarks = "")
        {
            SiteUrl = tenantItemRecord.SiteUrl;
            if (tenantItemRecord.Ex != null) { Remarks = tenantItemRecord.Ex.Message; }
            else { Remarks = remarks; }

            if (tenantItemRecord.List != null)
            {
                ListTitle = tenantItemRecord.List.Title;
                ListType = tenantItemRecord.List.BaseType.ToString();
            }

            if (tenantItemRecord.Item != null)
            {
                ItemID = tenantItemRecord.Item.Id.ToString();
                ShortcutName = (string)tenantItemRecord.Item["FileLeafRef"];
                ShortcutPath = (string)tenantItemRecord.Item["FileRef"];
            }
        }

        internal void AddTargetSite(string targetSite)
        {
            TargetSite = targetSite;
        }
    }

    public class ShortcutODReportParameters : ISolutionParameters
    {
        private static readonly Expression<Func<ListItem, object>>[] _fileExpressions = new Expression<Func<ListItem, object>>[]
        {
            i => i["A2ODExtendedMetadata"],
            i => i["Author"],
            i => i["Created"],
            i => i["Editor"],
            i => i["ID"],
            i => i.FileSystemObjectType,
            i => i["FileLeafRef"],
            i => i["FileRef"],
        };

        internal readonly SPOAdminAccessParameters AdminAccess;
        internal readonly SPOTenantSiteUrlsParameters SiteParam;
        public SPOTenantSiteUrlsWithAccessParameters SiteAccParam
        {
            get
            {
                return new(AdminAccess, SiteParam);
            }
        }
        internal SPOListsParameters ListsParam { get; set; } = new();
        internal SPOItemsParameters ItemsParam { get; set; }
        public SPOTenantItemsParameters TItemsParam
        {
            get { return new(SiteAccParam, ListsParam, ItemsParam); }
        }

        public ShortcutODReportParameters(SPOAdminAccessParameters adminAccess, 
                                          SPOTenantSiteUrlsParameters siteParam,
                                          SPOItemsParameters itemsParameters)
        {
            AdminAccess = adminAccess;
            SiteParam = siteParam;
            ItemsParam = itemsParameters;

            SiteParam.IncludePersonalSite = true;
            SiteParam.IncludeSubsites = false;
            ListsParam.AllLists = false;
            ListsParam.IncludeLists = false;
            ListsParam.IncludeLibraries = false;
            ListsParam.ListTitle = "Documents";
            ItemsParam.FileExpressions = _fileExpressions;
        }
    }
}
