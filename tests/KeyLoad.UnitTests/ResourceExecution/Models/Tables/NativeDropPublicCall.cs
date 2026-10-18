using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record NativeDropPublicCall(Guid RequestId, Guid CommandId,
    Task<GrainOperationReply> Original);
