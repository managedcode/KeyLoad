using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class EventReferenceSelectSetup
{
    internal static async Task<EventReferenceSelectState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin,
        CancellationToken token)
    {
        var partition = new PartitionRef("event-select-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        foreach (var streamSet in new[] { EventReferenceSelectProtocol.StreamSet, EventReferenceSelectProtocol.InvalidStreamSet, EventReferenceSelectProtocol.EmptyStreamSet })
        {
            await ConfigureAsync(admin, partition, new(streamSet, ResourceKind.StreamSet, partition.TransactionDomainId)
            {
                FieldPolicies = [new("/to", "reference", RawReadGrant: EventReferenceSelectProtocol.RefRead, RawUseGrant: EventReferenceSelectProtocol.RefUse)]
            }, token);
        }
        await ConfigureAsync(admin, partition, new(EventReferenceSelectProtocol.Target, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(EventReferenceSelectProtocol.PrimaryKeyColumn, [new(EventReferenceSelectProtocol.PrimaryKeyColumn, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]),
            FieldPolicies = [new("/name", "private", RawReadGrant: EventReferenceSelectProtocol.NameRead)]
        }, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition,
            EventReferenceSelectProtocol.StreamSet, Capability.Query | Capability.EventsRead, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(EventReferenceSelectProtocol.Target, "c1", EventReferenceSelectProtocol.FirstJson, Access: new(OwnerId: identity.Principal.Id)),
            new PutDocument(EventReferenceSelectProtocol.Target, "c2", EventReferenceSelectProtocol.HiddenJson, Access: new(OwnerId: "other-owner")),
            new AppendEvents(EventReferenceSelectProtocol.StreamSet, EventReferenceSelectProtocol.Stream,
                [new("z", "linked", EventReferenceSelectProtocol.Payload(partition, "c1", 7)),
                 new("a", "linked", EventReferenceSelectProtocol.Payload(partition, "m", 7)),
                 new("m", "linked", EventReferenceSelectProtocol.Payload(partition, "c2", 7)),
                 new("b", "linked", EventReferenceSelectProtocol.Payload(partition, "c1", 2)),
                 new("n", "unlinked", "{\"score\":7}")], ExpectedStreamRevision.NoStream),
            new AppendEvents(EventReferenceSelectProtocol.InvalidStreamSet, EventReferenceSelectProtocol.Stream,
                [new("wrong", "linked", EventReferenceSelectProtocol.Payload(partition with { PartitionKey = "foreign" }, "c1", 7))],
                ExpectedStreamRevision.NoStream)
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        var source = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(
            new(new(partition, EventReferenceSelectProtocol.StreamSet, EventReferenceSelectProtocol.Stream, 1)), token));
        var invalid = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(
            new(new(partition, EventReferenceSelectProtocol.InvalidStreamSet, EventReferenceSelectProtocol.Stream, 1)), token));
        var state = new EventReferenceSelectState(partition, identity, command, receipt, source, invalid);
        await PolicyAsync(admin, state, 2, allow: false, names: false, token);
        await EventReferenceSelectSources.VerifyAsync(admin, state, token);
        return state;
    }

    internal static async Task PolicyAsync(RequestCqrsClusterCallers admin, EventReferenceSelectState state,
        long epoch, bool allow, bool names, CancellationToken token)
    {
        var fields = new List<string> { EventReferenceSelectProtocol.RefRead };
        if (allow)
        {
            fields.Add(EventReferenceSelectProtocol.RefUse);
        }
        if (names)
        {
            fields.Add(EventReferenceSelectProtocol.NameRead);
        }
        var principal = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            RestrictRows = true,
            OwnerId = state.Identity.Principal.Id,
            Grants = [new(state.Partition.DatabaseId, EventReferenceSelectProtocol.StreamSet, Capability.Query | Capability.EventsRead),
                new(state.Partition.DatabaseId, EventReferenceSelectProtocol.EmptyStreamSet, Capability.Query | Capability.EventsRead),
                new(state.Partition.DatabaseId, EventReferenceSelectProtocol.Target, Capability.Query | Capability.DocumentsRead)],
            FieldGrants = [.. fields]
        };
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
    }

    private static async Task ConfigureAsync(RequestCqrsClusterCallers admin, PartitionRef partition,
        ResourceDefinition definition, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition), token));
}
