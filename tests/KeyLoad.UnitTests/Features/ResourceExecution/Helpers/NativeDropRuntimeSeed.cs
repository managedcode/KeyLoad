using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropRuntimeSeed
{
    internal static async Task CreateAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        var source = profile.Source;
        var definition = new ResourceDefinition(NativeDropRuntimeProtocol.Table, ResourceKind.Collection,
            NativeDropRuntimeProtocol.Domain)
        {
            RelationalSchema = new(NativeDropRuntimeProtocol.IdField,
                [new(NativeDropRuntimeProtocol.IdField, RelationalColumnType.Text),
                 new(NativeDropRuntimeProtocol.ValueField, RelationalColumnType.Text)])
        };
        var created = await source.SubmitAsync(OperationKind.CreateTable, Guid.NewGuid(),
            PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(new CreateTableRequest(
                NativeDropRuntimeProtocol.Tenant, NativeDropRuntimeProtocol.Database, definition)), token).ConfigureAwait(false);
        _ = NativeDropRuntimeAssertions.Value<ResourceDefinition>(created);
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, NativeDropRuntimeProtocol.Partition,
            [new PutDocument(NativeDropRuntimeProtocol.Table, NativeDropRuntimeProtocol.Row, NativeDropRuntimeProtocol.Json)]);
        var written = await source.SubmitAsync(OperationKind.Batch, id, PartitionStoreProtocol.AdministratorId,
            NativeSerialization.Serialize(request), token).ConfigureAwait(false);
        var receipt = NativeDropRuntimeAssertions.Value<CommitReceipt>(written);
        await Assert.That(receipt.CommandId).IsEqualTo(id);
    }
}
