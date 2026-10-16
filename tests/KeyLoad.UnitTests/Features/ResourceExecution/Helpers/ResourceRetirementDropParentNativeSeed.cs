using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementDropParentNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementDropParentNativeSeed
{
    internal static ResourceRetirementDropParentNativeState Create(TestDatabase fixture)
    {
        var partition = fixture.Partition;
        var definition = new ResourceDefinition(Table, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(Id, [new(Id, RelationalColumnType.Text), new(Value, RelationalColumnType.Text)])
        };
        fixture.Submit(OperationKind.CreateTable, new CreateTableRequest(partition.TenantId,
            partition.DatabaseId, definition)).Get<ResourceDefinition>();
        fixture.Commit(new PutDocument(Table, Row, InitialJson));
        var database = fixture.CreateConfiguredDatabase(fixture.Store);
        var lifetime = fixture.Store.Read(view => ResourceLifetimeCatalog.Read(view,
            partition.TenantId, partition.DatabaseId, Table, definition))!;
        var lookup = new ResourceRetirementDropParentLookupV1(Version, Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, Table));
        var request = new ResourceRetirementRequestV1(Version, lookup.CommandId,
            partition.TenantId, partition.DatabaseId, Table, lifetime.Incarnation);
        var id = ResourceRetirementDropParentIdentity.CommandId(lookup, ResourceRetirementPhase.PersistDropParent);
        var phase = new ResourceRetirementPhaseCommandV1(Version, lookup.CommandId,
            ResourceRetirementPhase.PersistDropParent, NativeSerialization.Serialize(new ResourceRetirementPersistDropParentBodyV1(lookup, request)));
        var original = database.CreateResourceRetirementOperation(id, Root, database.EvaluationClock.GetUtcNow(), phase);
        var owner = fixture.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)!.DefaultShard);
        var epoch = fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeyLoad.Core.KeySpace.Principal(Root))!.PolicyEpoch);
        var receipt = new ResourceRetirementPhaseReceiptV1(Version, lookup.CommandId,
            ResourceRetirementPhase.PersistDropParent, id, lifetime, owner,
            checked(fixture.Store.Position + First), NativeOperationFingerprint.Compute(original));
        var fingerprint = NativeOperationFingerprint.Compute(lookup.CommandId, OperationKind.DropTable, Root,
            JsonSerializer.SerializeToUtf8Bytes(lookup.Request, JsonDefaults.Options));
        var expected = new ResourceRetirementDropParentV1(Version, lookup, Root, epoch, fingerprint,
            definition, lifetime, request, original, receipt, null, null, null, null);
        var result = new OperationResult(null) { NativeValue = new ResourceRetirementPhaseResultV1(receipt, null, null) { DropParent = expected } };
        return new(database, lookup, request, definition, lifetime, original, expected, result, []);
    }
}
