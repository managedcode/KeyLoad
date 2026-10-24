using KeyLoad.Diagnostics.ResourceExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record FunctionalGatePhysicalFileOwner(string Resource, string Directory,
    Guid NodeId, Guid Incarnation, ProcessEvidence Runtime);
