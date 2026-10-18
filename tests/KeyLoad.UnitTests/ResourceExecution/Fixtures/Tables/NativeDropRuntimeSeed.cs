using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropRuntimeSeed
{
    internal static async Task CreateAsync(NativeDropRuntimeProfile profile, CancellationToken token,
        string table = NativeDropRuntimeProtocol.Table)
    {
        var definition = new ResourceDefinition(table, ResourceKind.Collection, NativeDropRuntimeProtocol.Domain)
        {
            RelationalSchema = new(NativeDropRuntimeProtocol.IdField,
                [new(NativeDropRuntimeProtocol.IdField, RelationalColumnType.Text),
                 new(NativeDropRuntimeProtocol.ValueField, RelationalColumnType.Text)])
        };
        var created = await NativeDropPublicSubmission.SubmitAsync(profile, OperationKind.CreateTable, Guid.NewGuid(),
            PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(new CreateTableRequest(
                NativeDropRuntimeProtocol.Tenant, NativeDropRuntimeProtocol.Database, definition)), token).ConfigureAwait(false);
        _ = NativeDropRuntimeAssertions.Value<ResourceDefinition>(created);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, NativeDropRuntimeProtocol.Partition,
            [new PutDocument(table, NativeDropRuntimeProtocol.Row, NativeDropRuntimeProtocol.Json)]);
        var written = await NativeDropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, id,
            PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(request), token).ConfigureAwait(false);
        await Assert.That(NativeDropRuntimeAssertions.Value<CommitReceipt>(written).CommandId).IsEqualTo(id);
    }

    internal static async Task HealthyOtherAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, NativeDropRuntimeProtocol.Partition,
            [new PutDocument(NativeDropRuntimeProtocol.OtherTable, NativeDropRuntimeProtocol.OtherRow,
                NativeDropRuntimeProtocol.OtherJson)]);
        var reply = await NativeDropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, id,
            PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(request), token).ConfigureAwait(false);
        await Assert.That(NativeDropRuntimeAssertions.Value<CommitReceipt>(reply).CommandId).IsEqualTo(id);
        var row = profile.Source.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(NativeDropRuntimeProtocol.Partition, NativeDropRuntimeProtocol.OtherTable, NativeDropRuntimeProtocol.OtherRow),
            cancellationToken: token);
        await Assert.That(row!.Json).IsEqualTo(NativeDropRuntimeProtocol.OtherJson);
    }
}
