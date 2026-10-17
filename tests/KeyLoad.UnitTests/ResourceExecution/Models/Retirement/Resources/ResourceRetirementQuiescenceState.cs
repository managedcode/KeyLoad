using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record ResourceRetirementQuiescenceState(
    ControlledPartitionMovementNode Source,
    ControlledPartitionMovementNode Target,
    ControlledPartitionMovementLoopbackListeners Listeners,
    ControlledPartitionMovementLoopbackCorpus Corpus,
    PartitionMovePhaseResult Accepted,
    DateTimeOffset ExpiresAt,
    ResourceRetirementRequestV1 Request,
    byte[] OriginalReceipt,
    BlobMetadata OriginalBlob,
    DateTimeOffset RecordedAt,
    ControlledPartitionMovementOutcomeAuthority OriginalAuthority)
{
    internal ResourceRetirementPreparationControlV1 Control { get; set; } = null!;
    internal List<(ControlledPartitionMovementNode Owner, ReplicatedOperation Original, OperationResult Result)> Originals { get; } = [];
    internal List<Exception> ExpectedFailures { get; } = [];
}
