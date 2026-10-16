using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleFresh
{
    private const string FirstJson = "{\"id\":\"new-left\",\"title\":\"fresh-left\",\"amount\":31}";
    private const string SecondJson = "{\"id\":\"new-right\",\"title\":\"fresh-right\",\"amount\":47}";
    private const long Initial = 1;
    private const long Absent = 0;
    private const string PutKind = "putDocument";

    internal static async Task<DropTableLifecycleFreshState> CreateAsync(RequestCqrsRf3Callers administrator,
        DropTableLifecycleSeedState state, DropTableLifecycleTable table,
        Func<Task<ResourceOwnerInventoryResultV1>> captureInventory, CancellationToken token)
    {
        var created = await DropTableLifecycleSeed.RecreateAsync(administrator, table, token);
        await SqlRf3Protocol.EqualAsync(table.Definition, created.Definition);
        var inventory = await captureInventory();
        var lifetime = inventory.Control.Lifetime;
        await Assert.That(lifetime.TenantId).IsEqualTo(state.Partition.TenantId);
        await Assert.That(lifetime.DatabaseId).IsEqualTo(state.Partition.DatabaseId);
        await Assert.That(lifetime.ResourceName).IsEqualTo(table.Name);
        await Assert.That(lifetime.TransactionDomainId).IsEqualTo(state.Partition.TransactionDomainId);
        await Assert.That(lifetime.Kind).IsEqualTo(ResourceKind.Collection);
        await Assert.That(lifetime.Incarnation).IsNotEqualTo(table.Lifetime.Incarnation);
        await DropTableLifecycleFreshReads.OldAbsentAsync(administrator, table, token);
        EntityRef Reference(string id) => new(state.Partition, table.Name, id)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
        ImmutableArray<DocumentResult> rows =
        [
            new(Reference(DropTableLifecycleSeedProtocol.NewLeft), Initial, FirstJson, false, []),
            new(Reference(DropTableLifecycleSeedProtocol.NewRight), Initial, SecondJson, false, [])
        ];
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(table.Name, rows[0].Reference.Id, FirstJson, Absent),
                new PutDocument(table.Name, rows[1].Reference.Id, SecondJson, Absent)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await NativeSubmissionColdAssertions.PlacementAsync(administrator, command, receipt, token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(Absent);
        await NativeSubmissionColdAssertions.ReceiptAsync(receipt,
            new(command.CommandId, receipt.Token,
                [new(PutKind, table.Name, rows[0].Reference.Id, Initial),
                    new(PutKind, table.Name, rows[1].Reference.Id, Initial)], DurabilityProfile.QuorumProcessDurable));
        var populated = await captureInventory();
        await SqlRf3Protocol.EqualAsync(lifetime, populated.Control.Lifetime);
        var fresh = new DropTableLifecycleFreshState(created.Definition, command, receipt, rows, populated);
        await VerifyAsync(administrator, state, table, fresh, token);
        return fresh;
    }

    internal static async Task VerifyAsync(RequestCqrsRf3Callers administrator, DropTableLifecycleSeedState state,
        DropTableLifecycleTable table, DropTableLifecycleFreshState fresh, CancellationToken token)
    {
        await DropTableLifecycleSeedAssertions.ControlsAsync(administrator, state, token);
        await DropTableLifecycleSeedAssertions.RowsAsync(administrator, fresh.Rows, token);
        await DropTableLifecycleFreshReads.OldAbsentAsync(administrator, table, token);
        await DropTableLifecycleFreshReplay.VerifyAsync(administrator, fresh, token);
        await DropTableLifecycleSeedAssertions.RowsAsync(administrator, fresh.Rows, token);
        await DropTableLifecycleFreshReads.OldAbsentAsync(administrator, table, token);
    }
}
