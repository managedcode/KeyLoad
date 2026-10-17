using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Stale
{
    internal static async Task RunAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        var request = new BindAtomicPartitionPlacementRequest(1, 0L, state.Second, state.Fixture.PhysicalShardId);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(state.Fixture, state.ActiveNode);
            var admin = new KeyLoadClient(http, state.Fixture.AdminKey, IntegrationClientOptions.Execution());
            var actual = await admin.BindAtomicPartitionPlacementAsync(Guid.NewGuid(), request, token);
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value).IsFalse();
            await MetadataBootstrapRf3Calls.EqualAsync(Errors.Problem(ErrorCode.Conflict,
                "The atomic partition placement revision is stale."), actual.Problem);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await MetadataBootstrapRf3Calls.McpAsync(state, state.Fixture.AdminKey, async mcp =>
            {
                var actual = await mcp.Client.InvokeKeyLoadToolAsync(AtomicPartitionPlacementPublicRf3Protocol.BindTool,
                    MetadataBootstrapRf3Calls.Arguments(Guid.NewGuid(), request), cancellationToken: token);
                await McpCallerAssertions.ErrorAsync(actual, ErrorCode.Conflict, dispatched: true);
                await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.Conflict,
                    "The database operation could not be completed."), JsonDefaults.Options),
                    actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
                await McpCallerAssertions.DoesNotDiscloseAsync(actual, state.Fixture.AdminKey, MetadataBootstrapRf3State.Json);
                return true;
            }, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Metadata.AllAsync(state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Data.RequireAsync(state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
