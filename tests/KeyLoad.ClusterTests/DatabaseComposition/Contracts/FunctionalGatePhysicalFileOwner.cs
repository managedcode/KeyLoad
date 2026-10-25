using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGatePhysicalFileOwner(string Resource, string Directory,
    Guid NodeId, Guid Incarnation, ProcessEvidence Runtime);
