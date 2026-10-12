using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueuePolicyEpochRf3ReplayProof
{
    internal static async Task RequireAsync(NativeBlobWholeOwnerCut before, ReplicatedOperation operation,
        QueuePolicyEpochRf3Seed seed, QueuePolicyEpochRf3Known[] known, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.Delivery);
        await Assert.That(operation.PrincipalId).IsEqualTo(seed.Principal.Id);
        var choice = known.Single(value => value.Command.CommandId == operation.Id);
        var original = before.Entries.First(entry => entry.Operation?.Id == operation.Id).Operation!;
        await Assert.That(original.Kind).IsEqualTo(operation.Kind);
        await Assert.That(original.PrincipalId).IsEqualTo(operation.PrincipalId);
        await Assert.That(original.PayloadJson).IsEqualTo(operation.PayloadJson);
        var prior = NativeSerialization.Deserialize<NativeCommandPayload>(original.NativePayload.Span);
        var current = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        await Assert.That(current.Value.Span.SequenceEqual(prior.Value.Span)).IsTrue();
        await Assert.That(current.Error).IsEqualTo(prior.Error);
        await Assert.That(current.SafeDetail).IsEqualTo(prior.SafeDetail);
        if (current.Error is null)
        { await SqlRf3Protocol.EqualAsync(choice.Command, NativeCommandPayload.Read<DeliveryCommand>(operation)); }
        var key = Convert.ToHexString(KeySpace.PartitionOutcome(seed.Lane.Partition, operation.PrincipalId, operation.Id));
        var row = before.CanonicalRows.Single(value => value.StartsWith(key + ":", StringComparison.Ordinal));
        var retained = NativeSerialization.Deserialize<StoredOutcome>(Convert.FromHexString(row[(key.Length + 1)..]));
        await Assert.That(retained.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(operation));
        await Assert.That(retained.Incarnation).IsEqualTo(before.Owner.Incarnation);
        await Assert.That(retained.Partition).IsEqualTo(seed.Lane.Partition);
        await Assert.That(retained.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await Assert.That(retained.PolicyEpoch).IsEqualTo(choice.PolicyEpoch);
        await Assert.That(retained.Result.Error).IsEqualTo(choice.Error);
        if (choice.Receipt is { } receipt)
        { await SqlRf3Protocol.EqualAsync(receipt, retained.Result.Get<CommitReceipt>()); }
    }
}
