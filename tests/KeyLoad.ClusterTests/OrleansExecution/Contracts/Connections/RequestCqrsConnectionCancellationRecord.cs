using KeyLoad.Clustering;

namespace KeyLoad.ClusterTests.OrleansExecution;

internal sealed record RequestCqrsConnectionCancellationRecord(Guid WaveId, string Node, Guid RequestId,
    GrainFailureStage CallSiteStage, bool RequestActive, bool AdmissionClosed,
    bool OwnerCancellationRequested, bool OriginalTokenMatchesWriter);
