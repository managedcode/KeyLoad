using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record NativeDropPublicCall(Guid RequestId, Guid CommandId, DateTimeOffset ExpiresAt,
    Task<GrainOperationReply> Original);
