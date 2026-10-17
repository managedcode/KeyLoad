using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementCleanupNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Only structural negative-validator input; these records are not D3 receipts or deployment authority.</summary>
internal static class ResourceRetirementCleanupCheckpointShape
{
    private const string Intent = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string[] SourceEndpoints = ["http://127.0.0.1:31001/", "http://127.0.0.1:31002/", "http://127.0.0.1:31003/"];
    private static readonly string[] OtherEndpoints = ["http://127.0.0.1:31004/", "http://127.0.0.1:31005/", "http://127.0.0.1:31006/"];
    private static readonly string[] ShapeVoters = ["shape-other-a", "shape-other-b", "shape-other-c"];

    internal static ResourceRetirementCleanupCheckpointV1 Create(TestDatabase fixture, ResourceRetirementCleanupNativeState state)
    {
        var catalog = fixture.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view))!;
        var receiver = new RegisteredPhysicalOwnerV1(catalog.DefaultShard, [.. SourceEndpoints]);
        var other = new RegisteredPhysicalOwnerV1(new(Guid.NewGuid(), Guid.NewGuid(), [.. ShapeVoters], Generation), [.. OtherEndpoints]);
        var directory = new PhysicalOwnerDirectoryV1(One, Generation, catalog.DefaultShard, [receiver, other]);
        var request = new ResourceRetirementRequestV1(One, Guid.NewGuid(), state.Lifetime.TenantId,
            state.Lifetime.DatabaseId, state.Lifetime.ResourceName, state.Lifetime.Incarnation);
        var definition = fixture.Store.Read(view => fixture.Database.Resource(view, fixture.Partition, Table));
        // A synthetic receipt shape is used only to reach the negative validator branch. No phase is issued or applied.
        var begin = new ResourceRetirementPhaseReceiptV1(One, request.RetirementId, ResourceRetirementPhase.Begin,
            request.RetirementId, state.Lifetime, catalog.DefaultShard, Generation, Intent);
        var control = new ResourceRetirementControlV1(request, definition, state.Lifetime, catalog, directory,
            Zero, Principal, Generation, begin, Intent)
        {
            WorkOrigins = []
        };
        var partition = state.Partitions[FirstIndex];
        var attemptId = Guid.NewGuid(); // Structural validator input only; no source attempt is issued.
        var pageId = ResourceRetirementCleanupPageIdentity.For(control, receiver, partition, Generation, attemptId);
        var receipt = new ResourceRetirementPhaseReceiptV1(One, request.RetirementId, ResourceRetirementPhase.CleanupPage,
            pageId, state.Lifetime, catalog.DefaultShard, Generation, Intent);
        return new(control, receiver, partition, Generation, FamilyCount, ReadOnlyMemory<byte>.Empty, true, receipt)
        {
            OutcomeIdentity = new(Principal, Generation, Intent),
            AttemptId = attemptId
        };
    }
}
