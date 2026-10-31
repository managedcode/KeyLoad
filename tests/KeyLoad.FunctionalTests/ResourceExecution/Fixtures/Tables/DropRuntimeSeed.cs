using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class DropRuntimeSeed
{
    internal static async Task CreateAsync(DropRuntimeProfile profile, CancellationToken token,
        string table = DropRuntimeProtocol.Table)
    {
        var definition = new ResourceDefinition(table, ResourceKind.Collection, DropRuntimeProtocol.Domain)
        {
            RelationalSchema = new(DropRuntimeProtocol.IdField,
                [new(DropRuntimeProtocol.IdField, RelationalColumnType.Text),
                 new(DropRuntimeProtocol.ValueField, RelationalColumnType.Text)])
        };
        var created = await DropPublicSubmission.SubmitAsync(profile, OperationKind.CreateTable, Guid.NewGuid(),
            PartitionStoreProtocol.AdministratorId, new CreateTableRequest(
                DropRuntimeProtocol.Tenant, DropRuntimeProtocol.Database, definition), token).ConfigureAwait(false);
        _ = DropRuntimeAssertions.Value<ResourceDefinition>(created);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, DropRuntimeProtocol.Partition,
            [new PutDocument(table, DropRuntimeProtocol.Row, DropRuntimeProtocol.Json)]);
        var written = await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, id,
            PartitionStoreProtocol.AdministratorId, request, token).ConfigureAwait(false);
        await Assert.That(DropRuntimeAssertions.Value<CommitReceipt>(written).CommandId).IsEqualTo(id);
    }

    internal static async Task HealthyOtherAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, DropRuntimeProtocol.Partition,
            [new PutDocument(DropRuntimeProtocol.OtherTable, DropRuntimeProtocol.OtherRow,
                DropRuntimeProtocol.OtherJson)]);
        var reply = await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch, id,
            PartitionStoreProtocol.AdministratorId, request, token).ConfigureAwait(false);
        await Assert.That(DropRuntimeAssertions.Value<CommitReceipt>(reply).CommandId).IsEqualTo(id);
        var row = profile.Source.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(DropRuntimeProtocol.Partition, DropRuntimeProtocol.OtherTable, DropRuntimeProtocol.OtherRow),
            cancellationToken: token);
        await Assert.That(row!.Json).IsEqualTo(DropRuntimeProtocol.OtherJson);
    }
}
