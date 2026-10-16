using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using C = KeyLoad.UnitTests.Features.DocumentStorage.RemoteDocumentNativeFixture;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentLifetimeNativeRead
{
    internal static RemoteDocumentReadFrameV2 Capture(RemoteDocumentNativeFixture fixture)
    {
        var budget = Work(fixture.Source.Database);
        return fixture.Source.Database.CaptureRemoteDocumentReadV2(C.Reader, C.Reference,
            fixture.Source.Database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(fixture.Source.Database.Limits.QueryDeadlineSeconds), budget)
            ?? throw new InvalidOperationException(Missing);
    }
    private const string Missing = "The actual document lifetime frame is absent.";

    internal static ReadExecutionBudget Work(DatabaseEngine database, CancellationToken token = default)
        => new(UnitExecutionOptions.DatabaseLimits(database.Limits), database.EvaluationClock, token);

    internal static OwnedDocumentReadResultV1 Read(RemoteDocumentNativeFixture fixture,
        RemoteDocumentReadFrameV2 frame, CancellationToken token = default)
    {
        var limits = fixture.Destination.Database.Limits;
        var request = new OwnedDocumentReadRequestV2(frame, new(C.Reference, fixture.Receipt.Token),
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, limits.MaxQueryReadBytes, limits.MaxScanRecords, limits.MaxBatchBytes);
        var budget = Work(fixture.Destination.Database, token);
        var grant = budget.CreateReadGrant(request.MaximumReadBytes, request.MaximumExaminedRecords);
        return fixture.Destination.Database.ReadOwnedDocumentV2(C.Reader, request, budget, grant);
    }

    internal static ResourceLifetimeV1 TargetLifetime(RemoteDocumentNativeFixture fixture)
        => fixture.Destination.Store.Read(view => ResourceLifetimeCatalog.Read(view, C.Partition.TenantId,
            C.Partition.DatabaseId, C.Collection, view.GetRecord<ResourceDefinition>(KeySpace.Resource(
                C.Partition.TenantId, C.Partition.DatabaseId, C.Collection)))) ?? throw new InvalidOperationException(Missing);

    internal static PrincipalRecord Definition(long epoch, bool raw)
        => new(C.Reader, C.Tenant, [new(C.Partition.DatabaseId, C.Collection, Capability.DocumentsRead)], raw ? ["*"] : [C.Title])
        {
            PolicyEpoch = epoch
        };
}
