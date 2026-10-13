using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsConnectionCancellationRecord(Guid WaveId, string Node, Guid RequestId,
    GrainFailureStage CallSiteStage, bool RequestActive, bool AdmissionClosed,
    bool OwnerCancellationRequested, bool OriginalTokenMatchesWriter);
