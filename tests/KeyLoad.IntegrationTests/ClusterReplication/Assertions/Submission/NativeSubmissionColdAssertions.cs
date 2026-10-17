using System.Collections.Immutable;
using System.Reflection;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.ClusterReplication.NativeSubmissionColdProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionColdAssertions
{
    private const string ReferencesProperty = "CompositionReferences";
    private const string MissingReferencesContract = "The native receipt privacy contract is missing.";
    private static readonly PropertyInfo PrivateReferences = typeof(MutationReceipt)
        .GetProperty(ReferencesProperty, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(MissingReferencesContract);

    internal static async Task OriginalAsync(RequestCqrsRf3Callers caller, CommandRequest command,
        RequestCqrsFaultRetryResult original, CancellationToken token)
    {
        await ReceiptAsync(original.McpReceipt, original.SdkReceipt).ConfigureAwait(false);
        await ReplayAsync(caller, command, original.SdkReceipt, token).ConfigureAwait(false);
        await RequestCqrsFaultReplayContinuation.DocumentAsync(caller,
            new(command.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId),
            P.OriginalCurrentRevision, RequestCqrsRf3Protocol.DocumentJson, token).ConfigureAwait(false);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers caller, CommandRequest command,
        CommitReceipt original, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token).ConfigureAwait(false))
            .ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false)).ConfigureAwait(false);
        await ReceiptAsync(sdk, original).ConfigureAwait(false);
        await ReceiptAsync(mcp.Value, original).ConfigureAwait(false);
    }

    internal static async Task ReceiptAsync(CommitReceipt actual, CommitReceipt expected)
    {
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true }).IsTrue();
        }
        foreach (var mutation in expected.Mutations)
        {
            await Assert.That(PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true }).IsTrue();
        }
    }

    internal static async Task<AtomicPartitionPlacementResolution> PlacementAsync(RequestCqrsRf3Callers admin,
        CommandRequest original, CommitReceipt receipt, CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(P.PlacementVersion, original.Partition);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(request, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await admin.Mcp.CallAsync(
            McpCallerTools.AdminPartitionPlacementRead, request, token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(sdk).SequenceEqual(JsonDefaults.Serialize(mcp.Value))).IsTrue();
        await Assert.That(sdk.Partition).IsEqualTo(original.Partition);
        await Assert.That(sdk.Incarnation).IsEqualTo(receipt.Token.Incarnation);
        await Assert.That(sdk.PlacementEpoch).IsEqualTo(receipt.Token.OwnershipEpoch);
        await Assert.That(original.Partition.AtomicPartitionId).IsEqualTo(receipt.Token.AtomicPartitionId);
        return sdk;
    }

    internal static async Task CurrentAuthorityAsync(RequestCqrsRf3Callers caller, PartitionRef partition, CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(P.PlacementVersion, partition);
        var sdk = await caller.Sdk.ReadAtomicPartitionPlacementAsync(request, token).ConfigureAwait(false);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
            request, token).ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true).ConfigureAwait(false);
    }
}
