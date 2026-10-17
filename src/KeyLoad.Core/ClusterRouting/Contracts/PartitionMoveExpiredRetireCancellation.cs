namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Actual source-committed cancellation of one immutable expired first-issued Retire.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveExpiredRetireCancellationAlias)]
internal sealed record PartitionMoveExpiredRetireCancellation(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] string OriginalPhaseIdentityDigest,
    [property: Orleans.Id(3)] Guid OriginalRequestNonce,
    [property: Orleans.Id(4)] DateTimeOffset OriginalExpiresAt,
    [property: Orleans.Id(5)] Guid CancellationCommandId,
    [property: Orleans.Id(6)] PartitionMoveJournalReceipt CancellationReceipt,
    [property: Orleans.Id(7)] string CancellationPrincipalId,
    [property: Orleans.Id(8)] long CancellationPolicyEpoch,
    [property: Orleans.Id(9)] long CleanupGeneration,
    [property: Orleans.Id(10)] int FamilyOrdinal,
    [property: Orleans.Id(11)] int BatchOrdinal,
    [property: Orleans.Id(12)] PartitionMovePhaseCommand CancellationPhase);

/// <summary>Original signed cancellation request; current receiver identity is assigned by native admission.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationBodyAlias)]
internal sealed record PartitionMoveRetireCancellationBody(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid CancellationCommandId,
    [property: Orleans.Id(2)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(3)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: Orleans.Id(4)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: Orleans.Id(5)] long CleanupGeneration,
    [property: Orleans.Id(6)] ReadOnlyMemory<byte> OriginalRequestBytes,
    [property: Orleans.Id(7)] string OriginalRequestSignature,
    [property: Orleans.Id(8)] DateTimeOffset CancellationExpiresAt,
    [property: Orleans.Id(9)] string? ActualReceiverPrincipalId = null,
    [property: Orleans.Id(10)] long ActualReceiverPolicyEpoch = PartitionMoveParentContractNames.UnissuedPolicyEpoch,
    [property: Orleans.Id(11)] PartitionMoveReceiverSourceWitness? OriginalSourceWitness = null);

/// <summary>Bounded original source reply; verifying its native MAC and actual outcome precedes control observation.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationWitnessAlias)]
internal sealed record PartitionMoveRetireCancellationWitness(
    [property: Orleans.Id(0)] PartitionMoveExpiredRetireCancellation Cancellation,
    [property: Orleans.Id(1)] ReadOnlyMemory<byte> OriginalReplyBytes,
    [property: Orleans.Id(2)] string OriginalReplySignature);

/// <summary>Fresh authenticated observation scope; it never authorizes another cancellation effect.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationReadAlias)]
internal sealed record PartitionMoveRetireCancellationReadBody(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(2)] Guid CancellationCommandId,
    [property: Orleans.Id(3)] PartitionMovePeerEnvelope OriginalEnvelope,
    [property: Orleans.Id(4)] PartitionMoveJournalReceipt OriginalAuthorization,
    [property: Orleans.Id(5)] ReadOnlyMemory<byte> OriginalRequestBytes,
    [property: Orleans.Id(6)] string OriginalRequestSignature,
    [property: Orleans.Id(7)] DateTimeOffset QueryExpiresAt);

/// <summary>Only an actual native cancellation outcome plus its retained barrier may advance cleanup.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationSnapshotAlias)]
internal sealed record PartitionMoveRetireCancellationSnapshot(
    [property: Orleans.Id(0)] PartitionMoveExpiredRetireCancellation? Cancellation,
    [property: Orleans.Id(1)] OperationResult? NativeCancellationResult,
    [property: Orleans.Id(2)] long CurrentReadCut);

/// <summary>Immutable first signed transport attempt, retained before send without inventing receiver issuance.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveRetireCancellationAttemptAlias)]
internal sealed record PartitionMoveRetireCancellationAttempt(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid CancellationCommandId,
    [property: Orleans.Id(2)] Guid CancellationNonce,
    [property: Orleans.Id(3)] DateTimeOffset CancellationExpiresAt,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> OriginalRequestBytes,
    [property: Orleans.Id(5)] string OriginalRequestSignature);
