using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed record DropPublicCall(Guid RequestId, Guid CommandId,
    Task<GrainOperationReply> Original);
