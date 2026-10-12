using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeCommittedCut
{
    internal static RemoteTransferPostAwaitNativeCut Read(NativeCanonicalModelCutWitness witness,
        RemoteTransferColdSeed seed, CommandRequest accept, RemoteModelNativeRequest original)
    {
        RequireScope(witness, seed, accept);
        var outcomeBytes = Required(witness.TechnicalOutcome);
        var receiptBytes = Required(witness.TargetReceipt);
        var capacityBytes = Required(witness.TargetCounter);
        var outcome = NativeSerialization.Deserialize<StoredOutcome>(outcomeBytes.Span);
        var receipt = NativeSerialization.Deserialize<RemoteTransferTargetReceiptRecord>(receiptBytes.Span);
        var capacity = NativeSerialization.Deserialize<RemoteTransferCapacity>(capacityBytes.Span);
        if (capacity.StoredRecords != 1 || capacity.StoredBytes != NativeSerialization.Serialize(receipt).LongLength
            || outcome.Result.Error is not null || outcome.RemoteTransferAuthority is not { } stamp
            || stamp.LogicalPrincipalId != seed.Identity.Principal.Id
            || stamp.LogicalPolicyEpoch != seed.Identity.Principal.PolicyEpoch
            || stamp.TechnicalPrincipalId != RemoteTransferDistinctProtocol.TechnicalSubject
            || stamp.TechnicalPolicyEpoch != outcome.PolicyEpoch
            || !Exact(stamp.SourceOwner.Owner, original.SourceOwner)
            || !Exact(stamp.DestinationOwner.Owner, original.DestinationOwner)
            || outcome.Incarnation != original.DestinationOwner.Incarnation
            || witness.Owner.Incarnation != original.DestinationOwner.Incarnation
            || receipt.Source != seed.Scenario.SourceQueue || receipt.Destination != seed.Scenario.DestinationQueue
            || receipt.TransferId != seed.TransferId || receipt.RemoteOrigin?.LogicalPrincipalId != seed.Identity.Principal.Id)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        return new(Convert.ToHexString(outcomeBytes.Span), Convert.ToHexString(receiptBytes.Span),
            Convert.ToHexString(capacityBytes.Span), outcome.Result.Get<CommitReceipt>());
    }

    internal static string ReadSource(NativeCanonicalModelCutWitness witness,
        RemoteTransferColdSeed seed, CommandRequest accept)
    {
        RequireScope(witness, seed, accept);
        if (witness.LogicalOutcome is not null)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        return Convert.ToHexString(Required(witness.SourceIntent).Span)
            + Convert.ToHexString(Required(witness.SourceCounter).Span);
    }

    private static void RequireScope(NativeCanonicalModelCutWitness witness, RemoteTransferColdSeed seed,
        CommandRequest accept)
    {
        NativeCanonicalModelCutCodec.RequireEnvelope(witness, CancellationToken.None);
        if (witness.Request.SourcePartition != seed.Scenario.SourcePartition
            || witness.Request.DestinationPartition != seed.Scenario.DestinationPartition
            || witness.Request.AcceptCommandId != accept.CommandId
            || witness.Request.LogicalPrincipalId != seed.Identity.Principal.Id
            || witness.Request.TechnicalPrincipalId != RemoteTransferDistinctProtocol.TechnicalSubject
            || witness.Request.SourceQueue != seed.Scenario.SourceQueue
            || witness.Request.DestinationQueue != seed.Scenario.DestinationQueue
            || witness.Request.TransferId != seed.TransferId)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
    }

    private static ReadOnlyMemory<byte> Required(ReadOnlyMemory<byte>? value)
        => value is { IsEmpty: false } bytes ? bytes
            : throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);

    private static bool Exact<T>(T expected, T actual)
        => NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual));
}
