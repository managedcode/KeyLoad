using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = SampleChunkRetentionRf3Protocol.FixtureKey)]
[NotInParallel]
internal sealed class SampleChunkRetentionRollupRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcChunk011012EnrolledChunkRetentionRollupWatermarksRefusalsAndReceiptsRemainExactAfterCold()
    {
        fixture.RegisterNativeCoverageCase<SampleChunkRetentionRollupRf3Tests>(nameof(
            AcChunk011012EnrolledChunkRetentionRollupWatermarksRefusalsAndReceiptsRemainExactAfterCold));
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
                var original = await SampleChunkRetentionRf3Seed.RunAsync(fixture, sdk, mcp, deadline.Token);
                await SampleChunkRetentionRf3Partial.RunAsync(original.Scenario, sdk, mcp, deadline.Token);
                var continuation = await SampleChunkRetentionRf3Full.RunAsync(original, sdk, mcp, deadline.Token);
                await mcp.DisposeAsync().ConfigureAwait(false);
                mcp = null;
                http.Dispose();
                http = null;
                await SampleChunkRetentionRf3Cold.RunAsync(fixture, continuation, deadline.Token).ConfigureAwait(false);
            }, failures);
        }
        finally
        {
            if (mcp is not null) { await ServerFailureObserver.ObserveAsync(async () => await mcp.DisposeAsync(), failures); }
            if (http is not null) { ServerFailureObserver.Observe(http.Dispose, failures); }
            if (deadline is not null) { ServerFailureObserver.Observe(deadline.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
