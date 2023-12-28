﻿using Microsoft.SharePoint.Client;
using NovaPointLibrary.Commands.SharePoint.Site;
using NovaPointLibrary.Solutions.Report;
using NovaPointLibrary.Solutions;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NovaPointLibrary.Commands.Authentication;
using NovaPointLibrary.Commands.SharePoint.Item;
using PnP.Framework.Utilities;

namespace NovaPointLibrary.Commands.SharePoint.RecycleBin
{
    internal class SPOTenantRecycleBinItem
    {
        private readonly NPLogger _logger;
        private readonly AppInfo _appInfo;
        private SPORecycleBinItemParameters _param = new();

        bool HasViewThreshold = false;

        private bool _clear = false;
        private bool _restore = false;

        public SPOTenantRecycleBinItem(NPLogger logger, AppInfo appInfo, SPORecycleBinItemParameters parameters)
        {
            _logger = logger;
            _appInfo = appInfo;
            _param = parameters;
        }

        internal async Task ReportAsync()
        {
            await IterateSitesAsync();
        }

        internal async Task ClearAsync()
        {
            _clear = true;
            await IterateSitesAsync();
        }

        internal async Task RestoreAsync()
        {
            _restore = true;
            await IterateSitesAsync();
        }

        private async Task IterateSitesAsync()
        {
            _appInfo.IsCancelled();

            await foreach (var siteResults in new SPOTenantSiteUrlsCSOM(_logger, _appInfo, _param).GetAsync())
            {

                if (!String.IsNullOrWhiteSpace(siteResults.Remarks))
                {
                    AddRecord(siteResults.SiteUrl, remarks: siteResults.Remarks);
                    continue;
                }

                try
                {
                    await ProcessRecycleBinItemsAsync(siteResults.SiteUrl, siteResults.Progress);
                }
                catch (Exception ex)
                {
                    _logger.ReportError("Site", siteResults.SiteUrl, ex);
                    AddRecord(siteResults.SiteUrl, remarks: ex.Message);
                }
            }
        }

        private async Task ProcessRecycleBinItemsAsync(string siteUrl, ProgressTracker parentProgress)
        {
            _appInfo.IsCancelled();
            _logger.LogUI(GetType().Name, $"Start processing recycle bin items for '{siteUrl}'");

            HasViewThreshold = false;

            ProgressTracker progress = new(parentProgress, 5000);
            int itemCounter = 0;
            int itemExpectedCount = 5000;
            var spoRecycleBinItem = new SPORecycleBinItemCSOM(_logger, _appInfo);
            await foreach (RecycleBinItem oRecycleBinItem in spoRecycleBinItem.GetAsync(siteUrl, _param))
            {
                _appInfo.IsCancelled();

                string remarks = string.Empty;
                if(_clear)
                {
                    remarks = await DeleteRecycleBinItemAsync(siteUrl, oRecycleBinItem);
                }
                if (_restore)
                {
                    remarks = await RestoreRecycleBinItemAsync(siteUrl, oRecycleBinItem);
                }

                AddRecord(siteUrl, oRecycleBinItem, remarks);

                progress.ProgressUpdateReport();
                itemCounter++;
                if (itemCounter == itemExpectedCount)
                {
                    progress.IncreaseTotalCount(6000);
                    itemExpectedCount += 5000;
                }
            }

            _logger.LogTxt(GetType().Name, $"Finish processing recycle bin items for '{siteUrl}'");
        }

        private async Task<string> DeleteRecycleBinItemAsync(string siteUrl, RecycleBinItem oRecycleBinItem)
        {
            _appInfo.IsCancelled();

            string rematks = string.Empty;

            if (!HasViewThreshold)
            {
                try
                {
                    await new SPORecycleBinItemCSOM(_logger, _appInfo).RemoveAsync(siteUrl, oRecycleBinItem);
                    rematks = "Item removed from Recycle bin";
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("The attempted operation is prohibited because it exceeds the list view threshold."))
                    {
                        HasViewThreshold = true;
                    }
                    else
                    {
                        _logger.ReportError("Recycle bin item", oRecycleBinItem.Title, ex);
                        rematks = ex.Message;
                    }
                }
            }
            if (HasViewThreshold)
            {
                try
                {
                    await new SPORecycleBinItemREST(_logger, _appInfo).RemoveAsync(siteUrl, oRecycleBinItem);
                    rematks = "Item removed from Recycle bin";
                }
                catch (Exception ex)
                {
                    _logger.ReportError("Recycle bin item", oRecycleBinItem.Title, ex);
                    rematks = ex.Message;
                }
            }

            return rematks;
        }

        private async Task<string> RestoreRecycleBinItemAsync(string siteUrl, RecycleBinItem oRecycleBinItem)
        {
            _appInfo.IsCancelled();

            string rematks = string.Empty;

            if (!HasViewThreshold)
            {
                try
                {
                    rematks = await RestoreRenameAsync(new SPORecycleBinItemCSOM(_logger, _appInfo).RestoreAsync, siteUrl, oRecycleBinItem, _param.RenameFile);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("The attempted operation is prohibited because it exceeds the list view threshold."))
                    {
                        HasViewThreshold = true;
                    }
                    else
                    {
                        _logger.ReportError("Recycle bin item", oRecycleBinItem.Title, ex);
                        rematks = ex.Message;
                    }
                }
            }
            if (HasViewThreshold)
            {
                try
                {
                    rematks = await RestoreRenameAsync(new SPORecycleBinItemREST(_logger, _appInfo).RestoreAsync, siteUrl, oRecycleBinItem, _param.RenameFile);
                }
                catch (Exception ex)
                {
                    _logger.ReportError("Recycle bin item", oRecycleBinItem.Title, ex);
                    rematks = ex.Message;
                }
            }

            return rematks;
        }

        private async Task<string> RestoreRenameAsync(Func<string, RecycleBinItem, Task> restore, string siteUrl, RecycleBinItem oRecycleBinItem, bool renameFile)
        {
            _appInfo.IsCancelled();
            _logger.LogTxt(GetType().Name, $"Processing restoration of item '{oRecycleBinItem.Title}' with id '{oRecycleBinItem.Id}', renaming file '{renameFile}'");

            string IsRestored = $"'{oRecycleBinItem.ItemType}' restored from Recycle bin correctly";

            try
            {
                await restore(siteUrl, oRecycleBinItem);
                return IsRestored;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("rename the existing file and try again.") && renameFile)
                {
                    if (oRecycleBinItem.ItemType != RecycleBinItemType.File && oRecycleBinItem.ItemType != RecycleBinItemType.Folder)
                    {
                        return $"{ex.Message} This is not neither a Folder or a Item. It cannot be renamed for restoration";
                    }

                    string remarks = string.Empty;

                    try
                    {
                        remarks = await FileRenameAsync(siteUrl, oRecycleBinItem);
                        await restore(siteUrl, oRecycleBinItem);
                        remarks += " " + IsRestored;
                        return remarks;
                    }
                    catch (Exception e)
                    {
                        Exception newEx = new($"{ex.Message} {remarks} {e.Message}");
                        throw newEx;
                    }
                }
                else
                {
                    throw new Exception(ex.Message);
                }
            }
        }

        internal async Task<string> FileRenameAsync(string siteUrl, RecycleBinItem oRecycleBinItem)
        {
            _appInfo.IsCancelled();
            string methodName = $"{GetType().Name}";
            _logger.LogTxt(methodName, $"Renaming original item {oRecycleBinItem.Title} in {siteUrl}");

            string unavailableName = oRecycleBinItem.Title;
            string newName;
            do
            {
                string itemNameOnly = Path.GetFileNameWithoutExtension(unavailableName);
                if (itemNameOnly[^3] == '('
                    && int.TryParse(itemNameOnly[^2].ToString(), out int unit)
                    && itemNameOnly[^1] == ')'
                    && unit >= 9)
                {
                    throw new Exception($"Too many {oRecycleBinItem.ItemType} with the same name on that location. We couldn't rename file from original location.");
                }

                newName = GetNewName(unavailableName);

                if (!await IsNameAvailable(siteUrl, oRecycleBinItem, newName))
                {
                    unavailableName = newName;
                    newName = string.Empty;
                }
            }
            while (string.IsNullOrWhiteSpace(newName));

            await RenameTargetLocationFile(siteUrl, oRecycleBinItem, newName);

            return $"File {oRecycleBinItem.Title} in the same location has been renamed as {newName}.";
        }


        internal string GetNewName(string itemName)
        {
            _appInfo.IsCancelled();
            string methodName = $"{GetType().Name}";
            _logger.LogTxt(GetType().Name, $"Getting new name for file {itemName}");

            string itemNameOnly = Path.GetFileNameWithoutExtension(itemName);
            var extension = Path.GetExtension(itemName);

            bool isDuplicatedName = false;
            int unit = 1;
            if (itemNameOnly[^3] == '(' && int.TryParse(itemNameOnly[^2].ToString(), out unit) && itemNameOnly[^1] == ')')
            {
                isDuplicatedName = true;
            }

            string newName;
            if (isDuplicatedName)
            {
                unit++;
                string baseName = itemNameOnly.Substring(0, itemNameOnly.Length - 3);
                newName = baseName + $"({unit})";
            }
            else
            {
                newName = itemNameOnly + "(1)";
            }

            return newName + extension;
        }

        internal async Task<bool> IsNameAvailable(string siteUrl, RecycleBinItem oRecycleBinItem, string itemNewTitle)
        {
            _appInfo.IsCancelled();
            _logger.LogTxt(GetType().Name, $"Check if file with name '{itemNewTitle}' exists in original location");

            string itemRelativeUrl = UrlUtility.Combine(oRecycleBinItem.DirName, itemNewTitle);

            bool exists = true;

            if (oRecycleBinItem.ItemType == RecycleBinItemType.File)
            {
                var oFile = await new SPOFileCSOM(_logger, _appInfo).GetFileAsync(siteUrl, itemRelativeUrl);
                if (oFile.Exists) { exists = false; }
            }
            else if (oRecycleBinItem.ItemType == RecycleBinItemType.Folder)
            {
                var oFolder = await new SPOFolderCSOM(_logger, _appInfo).GetFolderAsync(siteUrl, itemRelativeUrl);
                if (oFolder.Exists) { exists = false; }
            }

            return exists;

        }

        internal async Task RenameTargetLocationFile(string siteUrl, RecycleBinItem oRecycleBinItem, string newName)
        {
            _appInfo.IsCancelled();
            _logger.LogTxt(GetType().Name, $"Check if file with name '{newName}' exists in original location");

            string itemRelativeUrl = UrlUtility.Combine(oRecycleBinItem.DirName, oRecycleBinItem.Title);

            try
            {
                SPOListItemCSOM item = new(_logger, _appInfo);
                if (oRecycleBinItem.ItemType == RecycleBinItemType.File)
                {
                    await new SPOFileCSOM(_logger, _appInfo).RenameFileAsync(siteUrl, itemRelativeUrl, newName);
                }
                else if (oRecycleBinItem.ItemType == RecycleBinItemType.Folder)
                {
                    await new SPOFolderCSOM(_logger, _appInfo).RenameFolderAsync(siteUrl, itemRelativeUrl, newName);
                }
            }
            catch (Exception ex)
            {
                Exception newEx = new($"Error while renaming file '{oRecycleBinItem.Title}' as '{newName}' at '{oRecycleBinItem.DirName}'. {ex.Message}");
                throw newEx;
            }
        }

        private void AddRecord(string siteUrl,
                               RecycleBinItem? oRecycleBinItem = null,
                               string remarks = "")
        {
            dynamic recordItem = new ExpandoObject();
            recordItem.SiteUrl = siteUrl;

            recordItem.ItemId = oRecycleBinItem != null ? oRecycleBinItem.Id.ToString() : string.Empty;
            recordItem.ItemTitle = oRecycleBinItem != null ? oRecycleBinItem.Title : String.Empty;
            recordItem.ItemType = oRecycleBinItem != null ? oRecycleBinItem.ItemType.ToString() : String.Empty;
            recordItem.ItemState = oRecycleBinItem != null ? oRecycleBinItem.ItemState.ToString() : String.Empty;

            recordItem.DateDeleted = oRecycleBinItem != null ? oRecycleBinItem.DeletedDate.ToString() : String.Empty;
            recordItem.DeletedByName = oRecycleBinItem != null ? oRecycleBinItem.DeletedByName : String.Empty;
            recordItem.DeletedByEmail = oRecycleBinItem != null ? oRecycleBinItem.DeletedByEmail : String.Empty;

            recordItem.CreatedByName = oRecycleBinItem != null ? oRecycleBinItem.AuthorName : String.Empty;
            recordItem.CreatedByEmail = oRecycleBinItem != null ? oRecycleBinItem.AuthorEmail : String.Empty;
            recordItem.OriginalLocation = oRecycleBinItem != null ? oRecycleBinItem.DirName : String.Empty;

            recordItem.SizeMB = oRecycleBinItem != null ? Math.Round(oRecycleBinItem.Size / Math.Pow(1024, 2), 2).ToString() : String.Empty;
            
            recordItem.Remarks = remarks;

            _logger.RecordCSV(recordItem);
        }
    }
}
