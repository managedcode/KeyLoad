using KeyLoad.Clustering;

namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationRecord(Guid WaveId, string Node, Guid RequestId,
    GrainFailureStage CallSiteStage, bool RequestActive, bool AdmissionClosed,
    bool OwnerCancellationRequested, bool OriginalTokenMatchesWriter);
