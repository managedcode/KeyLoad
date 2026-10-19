using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGatePhysicalFileOwner(string Resource, string Directory,
    Guid NodeId, Guid Incarnation, ProcessEvidence Runtime);
