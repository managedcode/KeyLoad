using System.Collections.Immutable;
using System.Reflection;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Checks actual original receipt replay and complete canonical document/outbox conservation.</summary>
internal static class NativeActivationOriginalSessionConservation
{
    private const string ReferencesProperty = "CompositionReferences";
    private const string MissingReferencesContract = "The native receipt privacy contract is missing.";
    private static readonly PropertyInfo PrivateReferences = typeof(MutationReceipt)
        .GetProperty(ReferencesProperty, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(MissingReferencesContract);
    internal static async Task ObserveAsync(RequestCqrsRf3Callers administrator,
        NativeActivationOriginalSessionState state, long revision, string json,
        Func<Task> operation, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(
            state.Reference.Partition, token));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var expected = new DocumentReadResult(new(state.Reference, revision, json, false, []));
            var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.GetAsync(
                state.Reference, state.Minimum, token));
            await SqlRf3Protocol.EqualAsync(expected, actual);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(before,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(
                state.Reference.Partition, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await RequireReceiptAsync(state.ExpectedReceipt, state.Receipt), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RequireReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await Assert.That(actual.CommandId).IsEqualTo(expected.CommandId);
        await Assert.That(actual.Token).IsEqualTo(expected.Token);
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.Mutations.Length).IsEqualTo(expected.Mutations.Length);
        for (var index = 0; index < expected.Mutations.Length; index++)
        {
            var mutation = actual.Mutations[index];
            var literal = expected.Mutations[index];
            await Assert.That(mutation.Kind).IsEqualTo(literal.Kind);
            await Assert.That(mutation.Resource).IsEqualTo(literal.Resource);
            await Assert.That(mutation.Id).IsEqualTo(literal.Id);
            await Assert.That(mutation.Revision).IsEqualTo(literal.Revision);
            await Assert.That(PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true }).IsTrue();
        }
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers,
        NativeActivationOriginalSessionState state, int route, CancellationToken token)
    {
        var call = SqlRf3Protocol.Call(state.Command.Partition, McpCallerTools.DocumentsCommit,
            state.Command, state.Command.CommandId);
        var actual = route switch
        {
            0 => await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Command, token)),
            1 => (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, state.Command, token))).Value,
            2 => await SqlRf3Protocol.SdkAsync<CommitReceipt>(callers.Sdk, call, token),
            3 => await SqlRf3Protocol.McpAsync<CommitReceipt>(callers.Mcp, call, token),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };
        await RequireReceiptAsync(state.ExpectedReceipt, actual);
    }
}
