using NovaPointLibrary.Commands.DeviceManagement;
using NovaPointLibrary.Commands.Directory;
using NovaPointLibrary.Commands.Utilities.GraphModel;
using NovaPointLibrary.Core.Context;

namespace NovaPointLibrary.Solutions.DeviceManagement;

public class AddUsersToGroupInstalledApps : ISolution
{
    public static readonly string s_SolutionName = "Add users to groups by installed app";
    public static readonly string s_SolutionDocs = $"https://github.com/Barbarur/NovaPoint/wiki/Solution-{nameof(AddUsersToGroupInstalledApps)}";

    private readonly ContextSolution _ctx;
    private readonly AddUsersToGroupInstalledAppsParameters _param;

    // All three are keyed by group id so a group named by several pairs is only resolved once.
    // '_groupMembers' is the membership as it was when the run started
    // '_groupHandled' tracks the users this run has already added or reported during the solution
    private readonly Dictionary<Guid, HashSet<string>> _groupMembers = [];
    private readonly Dictionary<Guid, HashSet<string>> _groupHandled = [];
    private readonly Dictionary<Guid, string> _groupNames = [];

    // Keyed by pre-filter term, so two filters that reduce to the same term share one call.
    private readonly Dictionary<string, List<GraphDetectedApp>> _candidateApps = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<AddUsersToGroupInstalledAppsSummaryRecord> _summary = [];

    private AddUsersToGroupInstalledApps(ContextSolution context, AddUsersToGroupInstalledAppsParameters parameters)
    {
        _ctx = context;
        _param = parameters;

        Dictionary<Type, string> solutionReports = new()
        {
            { typeof(AddUsersToGroupInstalledAppsRecord), "Report" },
        };
        _ctx.DbHandler.AddSolutionReports(solutionReports);
    }

    public static ISolution Create(ContextSolution context, ISolutionParameters parameters)
    {
        return new AddUsersToGroupInstalledApps(context, (AddUsersToGroupInstalledAppsParameters)parameters);
    }

    public async Task RunAsync()
    {
        _ctx.AppClient.IsCancelled();

        var cmd = new MgDetectedApp(_ctx);

        /*
        '/detectedApps/{id}/managedDevices' answers with a projection where 'userId' and
        'userPrincipalName' are always null, whatever is asked for in $select, so the primary
        user has to come from the full managed device. Collecting every device once up front
        turns that into a lookup instead of a call per device.
        */
        Dictionary<string, GraphManagedDevice> devicesById = await GetDevicesByIdAsync();

        ProgressTracker progress = new(_ctx.Logger, _param.GroupAppPairs.Count);
        foreach (var pair in _param.GroupAppPairs)
        {
            _ctx.AppClient.IsCancelled();

            /*
            The summary row is created up front so a pair that fails part-way through
            still produces exactly one row, with whatever counts it reached.
            */
            AddUsersToGroupInstalledAppsSummaryRecord summary = new(pair);
            _summary.Add(summary);

            try
            {
                await ProcessPairAsync(cmd, devicesById, pair, summary, progress);
            }
            catch (Exception ex)
            {
                _ctx.Logger.Error(GetType().Name, "Group", pair.GroupId.ToString(), ex);

                summary.Errors++;
                AddRecord(new AddUsersToGroupInstalledAppsRecord(pair, summary.GroupName)
                {
                    Status = "Error",
                    Remarks = ex.Message,
                });
            }

            progress.ProgressUpdateReport();
        }

        _ctx.DbHandler.WriteToCsv(_summary, "Summary");
    }

    private async Task<Dictionary<string, GraphManagedDevice>> GetDevicesByIdAsync()
    {
        var collDevices = await new MgManagedDevice(_ctx)
            .GetAllAsync("?$select=id,deviceName,userId,userPrincipalName,emailAddress");

        Dictionary<string, GraphManagedDevice> devicesById = new(StringComparer.OrdinalIgnoreCase);
        foreach (var device in collDevices)
        {
            if (string.IsNullOrWhiteSpace(device.Id)) { continue; }

            devicesById.TryAdd(device.Id, device);
        }

        _ctx.Logger.Debug(GetType().Name, $"Collected {devicesById.Count} managed devices from Intune");

        return devicesById;
    }

    private async Task ProcessPairAsync(
        MgDetectedApp cmd,
        Dictionary<string, GraphManagedDevice> devicesById,
        GroupAppPair pair,
        AddUsersToGroupInstalledAppsSummaryRecord summary,
        ProgressTracker parentProgress)
    {
        HashSet<string> groupMembers = await GetGroupMembersAsync(pair.GroupId);
        string groupName = GetCachedGroupName(pair.GroupId);
        summary.GroupName = groupName;

        _ctx.Logger.Info(GetType().Name, $"Checking app '{pair.AppNameFilter}' against group '{groupName}' ({pair.GroupId})");

        List<GraphDetectedApp> candidateApps = await GetCandidateAppsAsync(cmd, pair.AppNameFilter);

        List<GraphDetectedApp> matchingApps = candidateApps
            .Where(a => a.DisplayName.Contains(pair.AppNameFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        summary.MatchingApps = matchingApps.Count;

        if (matchingApps.Count == 0)
        {
            _ctx.Logger.Debug(GetType().Name, $"No discovered app matches '{pair.AppNameFilter}'");

            AddRecord(new AddUsersToGroupInstalledAppsRecord(pair, groupName)
            {
                Status = "No match",
                Remarks = "No discovered app matches this filter",
            });
            return;
        }

        // The same user shows up once per device and per matching app version,
        // so collapse to a single entry per user before touching group membership.
        Dictionary<string, GraphManagedDevice> collUsers = [];
        Dictionary<string, GraphDetectedApp> collUserApps = [];

        // Only the id is worth asking the navigation for;
        // every other value comes from the full managed device it is joined to.
        HashSet<string> appDeviceIds = new(StringComparer.OrdinalIgnoreCase);
        ProgressTracker appProgress = new(parentProgress, matchingApps.Count);
        foreach (var app in matchingApps)
        {
            _ctx.AppClient.IsCancelled();

            var collAppDevices = await cmd.GetManagedDevicesAsync(app.Id, "?$select=id");
            foreach (var appDevice in collAppDevices)
            {
                if (string.IsNullOrWhiteSpace(appDevice.Id)) { continue; }
                if (!appDeviceIds.Add(appDevice.Id)) { continue; }

                if (!devicesById.TryGetValue(appDevice.Id, out GraphManagedDevice? device))
                {
                    summary.DevicesNotFound++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(device.UserId))
                {
                    summary.DevicesWithoutUser++;
                    continue;
                }

                if (collUsers.TryAdd(device.UserId, device))
                {
                    collUserApps.Add(device.UserId, app);
                }
            }

            appProgress.ProgressUpdateReport();
        }

        summary.DevicesWithApp = appDeviceIds.Count;
        summary.UsersWithApp = collUsers.Count;
        _ctx.Logger.Debug(GetType().Name, $"Found {collUsers.Count} unique users with an app matching '{pair.AppNameFilter}' installed");

        HashSet<string> groupHandled = _groupHandled[pair.GroupId];
        foreach (var user in collUsers)
        {
            _ctx.AppClient.IsCancelled();

            if (groupMembers.Contains(user.Key))
            {
                summary.AlreadyMembers++;
                continue;
            }

            // An earlier pair on this same group already dealt with this user.
            if (!groupHandled.Add(user.Key)) { continue; }

            AddUsersToGroupInstalledAppsRecord record = new(pair, groupName, user.Value, collUserApps[user.Key]);

            if (IsDeletedUser(user.Value))
            {
                summary.DeletedUsers++;
                record.Status = "Skipped - deleted user";
                record.Remarks = "The primary user of this device has been deleted";
                AddRecord(record);
                continue;
            }

            try
            {
                if (!_param.ReportMode)
                {
                    await new DirectoryGroupUser(_ctx.Logger, _ctx.AppClient).AddMemberAsync(pair.GroupId, user.Key);
                }

                summary.UsersAdded++;
                record.Status = _param.ReportMode ? "To be added" : "Added";
            }
            catch (Exception ex)
            {
                _ctx.Logger.Error(GetType().Name, "User", user.Key, ex);

                summary.Errors++;
                record.Status = "Error";
                record.Remarks = ex.Message;
            }

            AddRecord(record);
        }
    }

    private async Task<HashSet<string>> GetGroupMembersAsync(Guid groupId)
    {
        if (_groupMembers.TryGetValue(groupId, out HashSet<string>? knownMembers)) { return knownMembers; }

        var group = await new DirectoryGroup(_ctx.Logger, _ctx.AppClient).GetAsync(groupId.ToString(), "?$select=id,displayName");
        _groupNames.Add(groupId, group.DisplayName);

        var collMembers = await new DirectoryGroupUser(_ctx.Logger, _ctx.AppClient).GetMembersTransitiveAsync(groupId);
        HashSet<string> members = collMembers
            .Where(m => m.Type == "user")
            .Select(m => m.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _ctx.Logger.Info(GetType().Name, $"Resolved group '{groupId}' membership: {members.Count} user(s)");

        _groupMembers.Add(groupId, members);
        _groupHandled.Add(groupId, new(StringComparer.OrdinalIgnoreCase));
        return members;
    }
    
    private string GetCachedGroupName(Guid groupId)
    {
        return _groupNames.TryGetValue(groupId, out string? name) ? name : string.Empty;
    }
    
    private async Task<List<GraphDetectedApp>> GetCandidateAppsAsync(MgDetectedApp cmd, string appNameFilter)
    {
        string term = GetPrefilterTerm(appNameFilter);

        if (_candidateApps.TryGetValue(term, out List<GraphDetectedApp>? knownApps)) { return knownApps; }

        List<GraphDetectedApp> collApps;
        if (string.IsNullOrEmpty(term))
        {
            // A filter with no letters or digits leaves nothing safe to send.
            _ctx.Logger.Info(GetType().Name, $"'{appNameFilter}' cannot be pre-filtered; reading the whole app catalogue");

            collApps = (await cmd.GetAllAsync("?$select=id,displayName,version,publisher,platform,deviceCount")).ToList();
        }
        else
        {
            // A pure alphanumeric term needs no OData quoting and gives Graph nothing to mangle.
            collApps = (await cmd.GetAllAsync($"?$filter=contains(displayName,'{term}')")).ToList();
        }

        _ctx.Logger.Debug(GetType().Name, $"Collected {collApps.Count} candidate discovered apps for '{appNameFilter}' using '{term}'");

        _candidateApps.Add(term, collApps);
        return collApps;
    }
    
    /*
Graph's $filter on detectedApps silently drops every non-alphanumeric character bringing back unselected apps with it.
Sending the longest alphanumeric run of the filter makes that safe by construction.
*/
    private static string GetPrefilterTerm(string appNameFilter)
    {
        string longest = string.Empty;
        int start = -1;

        for (int i = 0; i <= appNameFilter.Length; i++)
        {
            if (i < appNameFilter.Length && char.IsLetterOrDigit(appNameFilter[i]))
            {
                if (start < 0) { start = i; }
                continue;
            }

            if (start >= 0)
            {
                if (i - start > longest.Length) { longest = appNameFilter[start..i]; }
                start = -1;
            }
        }

        return longest;
    }
    
    /*
    Entra ID rewrites a deleted user's UPN as its object id with the dashes removed followed
    by the original UPN — '20616a37f96a4b18adc4824e64b59f0bMegan.bowen@contoso.com'.
    */
    private static bool IsDeletedUser(GraphManagedDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.UserPrincipalName) || string.IsNullOrWhiteSpace(device.UserId)) { return false; }

        string deletedPrefix = device.UserId.Replace("-", string.Empty);

        return device.UserPrincipalName.StartsWith(deletedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    
    private void AddRecord(AddUsersToGroupInstalledAppsRecord record)
    {
        _ctx.DbHandler.WriteRecord(record);
    }

}


internal class AddUsersToGroupInstalledAppsRecord : ISolutionRecord
{
    // Target group
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string AppNameFilter { get; set; } = string.Empty;

    // Matched app
    public string MatchedAppName { get; set; } = string.Empty;
    public string MatchedAppVersion { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;

    // User
    public string UserId { get; set; } = string.Empty;
    public string UserPrincipalName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;

    public AddUsersToGroupInstalledAppsRecord() { }

    internal AddUsersToGroupInstalledAppsRecord(GroupAppPair pair, string groupName)
    {
        GroupId = pair.GroupId.ToString();
        GroupName = groupName;
        AppNameFilter = pair.AppNameFilter;
    }

    internal AddUsersToGroupInstalledAppsRecord(GroupAppPair pair, string groupName, GraphManagedDevice device, GraphDetectedApp app)
        : this(pair, groupName)
    {
        MatchedAppName = app.DisplayName;
        MatchedAppVersion = app.Version;
        DeviceName = device.DeviceName;

        UserId = device.UserId;
        // Reported exactly as Graph returns it. A UPN prefixed with the object id, dashes
        // removed, is how a deleted user surfaces — that is signal, so it is not cleaned up.
        UserPrincipalName = device.UserPrincipalName;
        EmailAddress = device.EmailAddress;
    }
}


public class AddUsersToGroupInstalledAppsParameters : ISolutionParameters
{
    public bool ReportMode { get; set; } = true;

    private string _groupAppPairsPath = string.Empty;
    public string GroupAppPairsPath
    {
        get { return _groupAppPairsPath; }
        set { _groupAppPairsPath = value.Trim(); }
    }

    internal List<GroupAppPair> GroupAppPairs { get; set; } = [];

    public void ParametersCheck()
    {
        if (string.IsNullOrWhiteSpace(GroupAppPairsPath))
        {
            throw new Exception("No file with the list of Group IDs and App names was selected.");
        }

        if (!File.Exists(GroupAppPairsPath))
        {
            throw new Exception("File with the list of Group IDs and App names doesn't exist.");
        }

        List<GroupAppPair> pairs = [];
        int lineNumber = 0;
        foreach (string line in File.ReadLines(@$"{GroupAppPairsPath}"))
        {
            lineNumber++;

            string trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#')) { continue; }

            // Split on the first comma only; app names are allowed to contain commas.
            string[] values = trimmedLine.Split(',', 2);
            if (values.Length != 2)
            {
                throw new Exception($"Line {lineNumber} of the file is not a 'GroupId,AppName' pair.");
            }

            if (!Guid.TryParse(values[0].Trim(), out Guid groupId))
            {
                throw new Exception($"Line {lineNumber} of the file doesn't start with a valid Group ID.");
            }

            string appNameFilter = values[1].Trim();
            if (string.IsNullOrWhiteSpace(appNameFilter))
            {
                throw new Exception($"Line {lineNumber} of the file has no App name.");
            }

            pairs.Add(new(groupId, appNameFilter));
        }

        if (pairs.Count == 0)
        {
            throw new Exception("File with the list of Group IDs and App names has no valid 'GroupId,AppName' pair.");
        }

        GroupAppPairs = pairs;
    }
}


internal sealed record GroupAppPair(Guid GroupId, string AppNameFilter);


internal sealed class AddUsersToGroupInstalledAppsSummaryRecord : ISolutionRecord
{
    public string GroupId            { get; set; } = string.Empty;
    public string GroupName          { get; set; } = string.Empty;
    public string AppNameFilter      { get; set; } = string.Empty;
    public int    MatchingApps       { get; set; }
    public int    DevicesWithApp     { get; set; }
    public int    DevicesWithoutUser { get; set; }
    public int    DevicesNotFound    { get; set; }
    public int    UsersWithApp       { get; set; }
    public int    AlreadyMembers     { get; set; }
    public int    DeletedUsers       { get; set; }
    public int    UsersAdded         { get; set; }
    public int    Errors             { get; set; }

    public AddUsersToGroupInstalledAppsSummaryRecord() { }

    internal AddUsersToGroupInstalledAppsSummaryRecord(GroupAppPair pair)
    {
        GroupId = pair.GroupId.ToString();
        AppNameFilter = pair.AppNameFilter;
    }
}
