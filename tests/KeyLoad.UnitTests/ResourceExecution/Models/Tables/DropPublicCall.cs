using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record DropPublicCall(Guid RequestId, Guid CommandId,
    Task<GrainOperationReply> Original);
