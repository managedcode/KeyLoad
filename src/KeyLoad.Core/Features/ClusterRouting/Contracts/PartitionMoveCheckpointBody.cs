namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveCheckpointActionAlias)]
internal enum PartitionMoveCheckpointAction { Admit = 1, Observe = 2, CompactTerminal = 3, CancelUnprepared = 4, ObserveCancellation = 5, ObserveRetireCancellation = 6, AdmitRetireCancellation = 7 }

/// <summary>Persists only original issued authority through an independently acknowledged control effect.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveParentContractNames.PartitionMoveCheckpointBodyAlias)]
internal sealed record PartitionMoveCheckpointBody(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] PartitionMoveCheckpointAction Action,
    [property: Orleans.Id(2)] string OperatorPrincipalId,
    [property: Orleans.Id(3)] PartitionMoveRequest OriginalTransferRequest,
    [property: Orleans.Id(4)] long ExpectedGeneration,
    [property: Orleans.Id(5)] Guid OriginalPhaseCommandId,
    [property: Orleans.Id(6)] PartitionMovePhaseCommand? OriginalPhase,
    [property: Orleans.Id(7)] PartitionMoveJournalReceipt? OriginalAuthorization,
    [property: Orleans.Id(8)] OperationResult? ObservedOriginalResult,
    [property: Orleans.Id(9)] PartitionMoveImageDescriptor? OriginalDescriptor,
    [property: Orleans.Id(10)] PartitionMoveSourceFenceRecord? OriginalFence,
    [property: Orleans.Id(11)] Guid? NextOriginalPhaseCommandId = null,
    [property: Orleans.Id(12)] PartitionMovePhaseCommand? NextOriginalPhase = null,
    [property: Orleans.Id(13)] PartitionMoveJournalReceipt? NextOriginalAuthorization = null,
    [property: Orleans.Id(14)] DateTimeOffset OriginalExpiresAt = default,
    [property: Orleans.Id(15)] DateTimeOffset NextOriginalExpiresAt = default,
    [property: Orleans.Id(16)] PartitionMoveCaptureWitness? OriginalCaptureWitness = null,
    [property: Orleans.Id(17)] Guid OriginalRequestNonce = default,
    [property: Orleans.Id(18)] Guid NextOriginalRequestNonce = default,
    [property: Orleans.Id(19)] PartitionMoveAuthenticatedOutcomeWitness? OriginalOutcomeWitness = null,
    [property: Orleans.Id(20)] Guid OriginalCaptureReleaseNonce = default,
    [property: Orleans.Id(21)] Guid NextOriginalCaptureReleaseNonce = default,
    [property: Orleans.Id(22)] PartitionMoveReceiverIssuanceWitness? OriginalReceiverIssuanceWitness = null,
    [property: Orleans.Id(23)] PartitionMoveReceiverSourceWitness? OriginalReceiverSourceWitness = null,
    [property: Orleans.Id(24)] PartitionMoveReceiverIssuePacket? OriginalReceiverIssuePacket = null,
    [property: Orleans.Id(25)] long CleanupGeneration = PartitionMoveParentContractNames.InitialCleanupGeneration,
    [property: Orleans.Id(26)] PartitionMoveRetireCancellationWitness? RetireCancellation = null,
    [property: Orleans.Id(27)] PartitionMoveRetireCancellationAttempt? RetireCancellationAttempt = null);
