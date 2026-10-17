using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class NativeRemoteGraphRf3ReplayProof
{
    internal static async Task RequireAsync(NativeBlobWholeOwnerCut before, ReplicatedOperation operation,
        NativeRemoteGraphRf3Seed seed, NativeRemoteGraphRf3Operations originals, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var choices = Choices(seed, originals).Where(value => value.Receipt.CommandId == operation.Id
            && value.Kind == operation.Kind && value.Principal == operation.PrincipalId
            && value.Receipt.Token.Incarnation == before.Owner.Incarnation).ToArray();
        await Assert.That(choices.Length).IsEqualTo(1);
        var selected = choices.Single();
        var previous = before.Entries.First(entry => entry.Operation?.Id == operation.Id).Operation!;
        await Assert.That(previous.Kind).IsEqualTo(operation.Kind);
        await Assert.That(previous.PrincipalId).IsEqualTo(operation.PrincipalId);
        await Assert.That(previous.PayloadJson).IsEqualTo(operation.PayloadJson);
        var key = Convert.ToHexString(KeySpace.PartitionOutcome(selected.Partition, selected.Principal, operation.Id));
        var row = before.CanonicalRows.Single(value => value.StartsWith(key + ":", StringComparison.Ordinal));
        var retained = NativeSerialization.Deserialize<StoredOutcome>(Convert.FromHexString(row[(key.Length + 1)..]));
        await Assert.That(retained.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(operation));
        await Assert.That(retained.Result.Error).IsNull();
        await Assert.That(retained.Incarnation).IsEqualTo(before.Owner.Incarnation);
        await Assert.That(retained.Partition).IsEqualTo(selected.Partition);
        await Assert.That(retained.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        if (selected.Principal == seed.Principal.Id)
        {
            var node = before.Owner.Incarnation == seed.Directory.ControlOwner.Incarnation
                ? TwoRf3MembershipProtocol.Node1 : TwoRf3MembershipProtocol.Node4;
            var restored = seed.RestoredPrincipals[node];
            await Assert.That(restored.Id).IsEqualTo(selected.Principal);
            await Assert.That(retained.PolicyEpoch).IsEqualTo(restored.PolicyEpoch);
        }
        await RequirePayloadAsync(previous, operation, retained, selected.Receipt);
    }

    private static async Task RequirePayloadAsync(ReplicatedOperation previous, ReplicatedOperation operation,
        StoredOutcome retained, CommitReceipt receipt)
    {
        switch (operation.Kind)
        {
            case OperationKind.RemoteGraphSource:
            {
                var original = retained.Result.Get<RemoteGraphSourceResultV1>();
                var actual = NativeCommandPayload.Read<RemoteGraphSourceMutationV1>(operation);
                await SqlRf3Protocol.EqualAsync(original.Receipt, receipt);
                await SqlRf3Protocol.EqualAsync(original.Original.Original, actual.Original);
                break;
            }
            case OperationKind.RemoteGraphReceive:
            {
                var original = retained.Result.Get<RemoteGraphReceiverResultV1>();
                var actual = NativeCommandPayload.Read<RemoteGraphReceiverMutationV1>(operation);
                await SqlRf3Protocol.EqualAsync(original.Receipt.TargetReceipt, receipt);
                await SqlRf3Protocol.EqualAsync(original.Original.SourceProof.Intent, actual.SourceProof.Intent);
                await SqlRf3Protocol.EqualAsync(original.Original.SourceProof.Original, actual.SourceProof.Original);
                await Assert.That(actual.CommandId).IsEqualTo(receipt.CommandId);
                break;
            }
            case OperationKind.RemoteGraphComplete:
            {
                var original = retained.Result.Get<RemoteGraphCompletionResultV1>();
                var actual = NativeCommandPayload.Read<RemoteGraphCompletionMutationV1>(operation);
                await SqlRf3Protocol.EqualAsync(original.Receipt, receipt);
                await SqlRf3Protocol.EqualAsync(original.Original.Receipt, actual.Receipt);
                await Assert.That(actual.CommandId).IsEqualTo(receipt.CommandId);
                break;
            }
            case OperationKind.Batch:
            {
                await SqlRf3Protocol.EqualAsync(retained.Result.Get<CommitReceipt>(), receipt);
                var original = NativeSerialization.Deserialize<NativeCommandPayload>(previous.NativePayload.Span);
                var actual = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
                await Assert.That(original.Value.Span.SequenceEqual(actual.Value.Span)).IsTrue();
                break;
            }
            default:
                throw new InvalidOperationException("An unrelated command is outside the original graph replay catalog.");
        }
    }

    private static IEnumerable<(CommitReceipt Receipt, PartitionRef Partition, OperationKind Kind, string Principal)> Choices(
        NativeRemoteGraphRf3Seed seed, NativeRemoteGraphRf3Operations originals)
    {
        yield return (seed.SourceReceipt, seed.SourcePartition, OperationKind.Batch, PartitionStoreProtocol.AdministratorId);
        yield return (seed.TargetReceipt, seed.TargetPartition, OperationKind.Batch, PartitionStoreProtocol.AdministratorId);
        foreach (var original in new[] { originals.Upsert, originals.Delete })
        {
            yield return (original.SourceReceipt, seed.SourcePartition, OperationKind.RemoteGraphSource, seed.Principal.Id);
            foreach (var delivery in original.Deliveries)
            {
                yield return (delivery.TargetReceipt, seed.TargetPartition, OperationKind.RemoteGraphReceive, seed.Principal.Id);
                yield return (delivery.CompletionReceipt, seed.SourcePartition, OperationKind.RemoteGraphComplete, seed.Principal.Id);
            }
        }
    }
}
