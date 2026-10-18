using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventingRequiredClaimClusterCurrent
{
    internal static async Task<string> ImageAsync(RequestCqrsClusterCallers root, EventingRequiredClaimClusterState state, CancellationToken token)
    {
        var input = state.Input;
        var expected = state.Completed ? RequiredProcessingPolicyClusterValues.Expected(input, "first", 1, false)
            : new MessageInspection(new("first", MessageState.Ready, 0, 1L, 1L, null, null), input.Payload, input.Headers);
        var message = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(new(input.Lane, "first"), token));
        await SqlClusterProtocol.EqualAsync(new MessageInspectionResult(expected), message);
        var document = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(input.Reference("first"), token));
        await SqlClusterProtocol.EqualAsync(new DocumentReadResult(state.Completed
            ? new(input.Reference("first"), 1L, RequiredProcessingPolicyClusterState.EffectJson("first"), false, []) : null), document);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(input.Partition, token));
        var count = state.Completed ? 2L : 1L;
        await Assert.That(outbox.Head.Tail).IsEqualTo(count);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(count);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(outbox.Consumers).IsEmpty();
        return JsonSerializer.Serialize(new
        {
            message,
            document,
            outbox
        }, JsonDefaults.Options);
    }
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, EventingRequiredClaimClusterState state,
        Func<Task> operation, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(await ImageAsync(root, state, token)).IsEqualTo(before), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
