using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextMaintenanceRf3Tests(ClusterFixture fixture)
{
    [Test]
    [Arguments(NativeTextMaintenancePath.Sdk)]
    [Arguments(NativeTextMaintenancePath.Mcp)]
    [Arguments(NativeTextMaintenancePath.SdkSql)]
    [Arguments(NativeTextMaintenancePath.McpSql)]
    public async Task ProtectedNativeTextMaintenanceReplaysBilingualUpdateDeleteAcrossAllPublicPaths(
        NativeTextMaintenancePath path)
    {
        fixture.RegisterNativeCoverageCase<NativeTextMaintenanceRf3Tests>(
            nameof(ProtectedNativeTextMaintenanceReplaysBilingualUpdateDeleteAcrossAllPublicPaths));
        var failures = new List<Exception>();
        McpCallerDeadline? deadline = null;
        HttpClient? http = null;
        McpOfficialClient? mcp = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                deadline = McpCallerDeadline.Create();
                http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
                var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
                    fixture.AdminKey, deadline.Token);
                await NativeTextMaintenanceRf3Flow.RunAsync(sdk, mcp, path, deadline.Token);
            }, failures);
        }
        finally
        {
            if (mcp is not null)
            { await ServerFailureObserver.ObserveAsync(() => mcp.DisposeAsync().AsTask(), failures); }
            if (http is not null)
            { ServerFailureObserver.Observe(http.Dispose, failures); }
            if (deadline is not null)
            { ServerFailureObserver.Observe(deadline.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
