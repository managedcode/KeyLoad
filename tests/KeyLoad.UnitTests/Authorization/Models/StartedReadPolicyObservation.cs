using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Authorization;

internal sealed record StartedReadPolicyObservation(
    IReadOnlyList<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Frames,
    OperationCanceledException? OriginalCancellation);
