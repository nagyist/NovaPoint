﻿using NovaPointLibrary.Commands.SharePoint.Permision;
using NovaPointLibrary.Commands.SharePoint.Site;
using NovaPointLibrary.Commands.SharePoint.SiteGroup;
using NovaPointLibrary.Core.Logging;
using static NovaPointLibrary.Commands.SharePoint.Permision.SPOSharingLinksREST;


namespace NovaPointLibrary.Solutions.Automation
{
    public class RemoveSharingLinksAuto
    {
        public static readonly string s_SolutionName = "Remove Sharing Links";
        public static readonly string s_SolutionDocs = "https://github.com/Barbarur/NovaPoint/wiki/Solution-Automation-RemoveSharingLinksAuto";

        private RemoveSharingLinksAutoParameters _param;
        private readonly LoggerSolution _logger;
        private readonly Commands.Authentication.AppInfo _appInfo;

        private RemoveSharingLinksAuto(LoggerSolution logger, Commands.Authentication.AppInfo appInfo, RemoveSharingLinksAutoParameters parameters)
        {
            _param = parameters;
            _logger = logger;
            _appInfo = appInfo;
        }

        public static async Task RunAsync(RemoveSharingLinksAutoParameters parameters, Action<LogInfo> uiAddLog, CancellationTokenSource cancelTokenSource)
        {
            LoggerSolution logger = new(uiAddLog, "RemoveSharingLinksAuto", parameters);

            try
            {
                Commands.Authentication.AppInfo appInfo = await Commands.Authentication.AppInfo.BuildAsync(logger, cancelTokenSource);

                await new RemoveSharingLinksAuto(logger, appInfo, parameters).RunScriptAsync();

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

            await foreach (var siteRecord in new SPOTenantSiteUrlsWithAccessCSOM(_logger, _appInfo, _param.SiteAccParam).GetAsync())
            {
                _appInfo.IsCancelled();
                
                if (siteRecord.Ex != null)
                {
                    SPOSharingLinksRecord record = new(siteRecord.SiteUrl);
                    record.Remarks = siteRecord.Ex.Message;
                    RecordCSV(record);
                    continue;
                }

                try
                {
                    await ProcessSite(siteRecord);
                }
                catch (Exception ex)
                {
                    _logger.Error(GetType().Name, "Site", siteRecord.SiteUrl, ex);
                    SPOSharingLinksRecord record = new(siteRecord.SiteUrl);
                    record.Remarks = ex.Message;
                    RecordCSV(record);
                }

            }
        }

        private async Task ProcessSite(SPOTenantSiteUrlsRecord siteRecord)
        {
            _appInfo.IsCancelled();

            var collGroups = await new SPOSiteGroupCSOM(_logger, _appInfo).GetSharingLinksAsync(siteRecord.SiteUrl);

            SPOSharingLinksREST restSharingLinks = new(_logger, _appInfo);
            ProgressTracker progress = new(siteRecord.Progress, collGroups.Count());
            foreach (var oGroup in collGroups)
            {
                var record = await restSharingLinks.GetFromGroupAsync(siteRecord.SiteUrl, oGroup);

                try
                {
                    if (_param.RemoveAll || record.SharingLinkCreatedBy.Equals(_param.Createdby, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!_param.ReportMode)
                        {
                            await new SPOSiteGroupCSOM(_logger, _appInfo).RemoveAsync(siteRecord.SiteUrl, oGroup);
                        }
                    }
                    else
                    {
                        continue;
                    }

                    record.Remarks = "Sharing Link deleted";
                    RecordCSV(record);
                }
                catch (Exception ex)
                {
                    record.Remarks = ex.Message;
                    RecordCSV(record);
                }

                progress.ProgressUpdateReport();
            }

        }

        private void RecordCSV(SPOSharingLinksRecord record)
        {
            _logger.RecordCSV(record);
        }
    }

    public class RemoveSharingLinksAutoParameters : ISolutionParameters
    {
        public bool ReportMode { get; set; }
        public bool RemoveAll { get; set; }
        public string Createdby { get; set; }
        internal SPOAdminAccessParameters AdminAccess;
        internal SPOTenantSiteUrlsParameters SiteParam;
        public SPOTenantSiteUrlsWithAccessParameters SiteAccParam
        {
            get
            {
                return new(AdminAccess, SiteParam);
            }
        }
        public RemoveSharingLinksAutoParameters(bool reportMode, bool removeAll, string createdby, SPOAdminAccessParameters adminAccess, SPOTenantSiteUrlsParameters siteParam)
        {
            ReportMode = reportMode;
            RemoveAll = removeAll;
            Createdby = createdby;
            AdminAccess = adminAccess;
            SiteParam = siteParam;
        }
    }
}
