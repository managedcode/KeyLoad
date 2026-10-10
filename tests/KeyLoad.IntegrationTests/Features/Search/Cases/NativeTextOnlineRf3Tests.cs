using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class NativeTextOnlineRf3Tests(ClusterFixture fixture)
{
    [Test]
    [Arguments(NativeTextMaintenancePath.Sdk)]
    [Arguments(NativeTextMaintenancePath.Mcp)]
    [Arguments(NativeTextMaintenancePath.SdkSql)]
    [Arguments(NativeTextMaintenancePath.McpSql)]
    public async Task OnlinePublicationRefusalUpdateDeleteFullReplayAndColdContinuation(NativeTextMaintenancePath path)
    {
        fixture.RegisterNativeCoverageCase<NativeTextOnlineRf3Tests>(nameof(OnlinePublicationRefusalUpdateDeleteFullReplayAndColdContinuation));
        var failures = new List<Exception>();
        McpCallerDeadline? deadline = null;
        HttpClient? http = null;
        McpOfficialClient? mcp = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            deadline = McpCallerDeadline.Create();
            http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, deadline.Token);
            await NativeTextOnlineRf3Flow.RunAsync(fixture, sdk, mcp, path, deadline.Token);
        }, failures);
        if (mcp is not null)
        { await ServerFailureObserver.ObserveAsync(() => mcp.DisposeAsync().AsTask(), failures); }
        if (http is not null)
        { ServerFailureObserver.Observe(http.Dispose, failures); }
        if (deadline is not null)
        { ServerFailureObserver.Observe(deadline.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
