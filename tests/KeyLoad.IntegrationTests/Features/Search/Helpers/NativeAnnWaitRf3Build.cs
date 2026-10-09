using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnWaitRf3Build
{
    internal static async Task<AnnMaintenanceResult> ExecuteAsync(KeyLoadClient sdk,
        AnnMaintenanceRequest request, NativeAnnWaitHttpObservation observation, CancellationToken token)
    {
        observation.Start(token);
        try
        {
            return await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(request, token));
        }
        catch (Exception original)
        {
            observation.Stop();
            var failures = new List<Exception> { original };
            observation.Write(failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        finally
        { observation.Stop(); }
    }
}

