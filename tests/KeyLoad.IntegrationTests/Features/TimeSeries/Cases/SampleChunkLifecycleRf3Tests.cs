using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = SampleChunkRf3Protocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleChunkLifecycleRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcChunk007009011NativeBackgroundMergePreservesLiteralRowsReceiptsAndCurrentPolicy()
    {
        fixture.RegisterNativeCoverageCase<SampleChunkLifecycleRf3Tests>(nameof(
            AcChunk007009011NativeBackgroundMergePreservesLiteralRowsReceiptsAndCurrentPolicy));
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
                    fixture.AdminKey, deadline.Token).ConfigureAwait(false);
                var continuation = await SampleChunkRf3Lifecycle.ExecuteAsync(fixture, sdk, mcp, deadline.Token).ConfigureAwait(false);
                await mcp.DisposeAsync().ConfigureAwait(false);
                mcp = null;
                http.Dispose();
                http = null;
                await SampleChunkRf3Cold.RunAsync(fixture, continuation, deadline.Token).ConfigureAwait(false);
            }, failures);
        }
        finally
        {
            if (mcp is not null)
            { await ServerFailureObserver.ObserveAsync(async () => await mcp.DisposeAsync(), failures); }
            if (http is not null)
            { ServerFailureObserver.Observe(http.Dispose, failures); }
            if (deadline is not null)
            { ServerFailureObserver.Observe(deadline.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
