using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed record StartedReadPolicyObservation(
    IReadOnlyList<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Frames,
    OperationCanceledException? OriginalCancellation);
