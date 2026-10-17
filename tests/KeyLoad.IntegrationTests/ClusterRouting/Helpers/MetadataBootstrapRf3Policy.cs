using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MetadataBootstrapRf3Policy
{
    internal static async Task RepairAsync(MetadataBootstrapRf3State state, CancellationToken token)
    {
        var request = new BindAtomicPartitionPlacementRequest(1, 2L, state.Third, state.Fixture.PhysicalShardId);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(state.Fixture, state.ActiveNode);
            var actor = new KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution());
            var sdk = await actor.BindAtomicPartitionPlacementAsync(Guid.NewGuid(), request, token);
            await Assert.That(sdk.IsSuccess).IsFalse();
            await Assert.That(sdk.Value).IsFalse();
            await MetadataBootstrapRf3Calls.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, "Cluster administration is required."), sdk.Problem);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await MetadataBootstrapRf3Calls.McpAsync(state, state.Identity.Secret, async mcp =>
            {
                var reply = await mcp.Client.InvokeKeyLoadToolAsync(AtomicPartitionPlacementPublicRf3Protocol.BindTool,
                    MetadataBootstrapRf3Calls.Arguments(Guid.NewGuid(), request), cancellationToken: token);
                await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
                await Assert.That(System.Text.Json.JsonElement.DeepEquals(System.Text.Json.JsonSerializer.SerializeToElement(
                    Errors.Problem(ErrorCode.PermissionDenied, "The authenticated principal is not permitted to perform this operation."), JsonDefaults.Options),
                    reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, MetadataBootstrapRf3State.Json);
                return true;
            }, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Metadata.AllAsync(state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Data.RequireAsync(state, token), failures);
        var repaired = state.Identity.Principal with
        {
            PolicyEpoch = 2L,
            ClusterAdministrator = true
        };
        await MetadataBootstrapRf3Calls.EqualAsync(repaired, await MetadataBootstrapRf3Calls.SdkAsync(state, state.Fixture.AdminKey,
            sdk => sdk.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        state.Identity = state.Identity with
        {
            Principal = repaired
        };
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Metadata.BindNextAsync(state, state.Third, state.Identity.Secret, token), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapRf3Data.CommitAsync(state, state.Third, "after-repair", token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
