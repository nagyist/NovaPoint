using NovaPointLibrary.Commands.Utilities;
using NovaPointLibrary.Commands.Utilities.GraphModel;
using NovaPointLibrary.Core.Context;

namespace NovaPointLibrary.Commands.DeviceManagement;

internal class MgDetectedApp(IContextManager ctx)
{
    private IContextManager Ctx { get; init; } = ctx;

    internal async Task<IEnumerable<GraphDetectedApp>> GetAllAsync(string optionalQuery = "")
    {
        string endpointPath = $"/deviceManagement/detectedApps" + optionalQuery;
        return await new GraphAPIHandler(Ctx.Logger, Ctx.AppClient).GetCollectionAsync<GraphDetectedApp>(endpointPath);
    }

    internal async Task<IEnumerable<GraphManagedDevice>> GetManagedDevicesAsync(string detectedAppId, string optionalQuery = "")
    {
        string endpointPath = $"/deviceManagement/detectedApps/{detectedAppId}/managedDevices" + optionalQuery;
        return await new GraphAPIHandler(Ctx.Logger, Ctx.AppClient).GetCollectionAsync<GraphManagedDevice>(endpointPath);
    }
}
