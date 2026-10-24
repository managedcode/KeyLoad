using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class EventProjectionLineageR41Seed
{
    internal static async Task<EventProjectionLineageR41State> CreateAsync(ClusterFixture fixture,
     RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var p = new PartitionRef("lineage-r41-" + Guid.NewGuid().ToString("N"), "lineage-db", "lineage-domain", Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(EventProjectionLineageR41Protocol.Collection, ResourceKind.Collection, p.TransactionDomainId)
        {
            FieldPolicies = [new(EventProjectionLineageR41Protocol.Input, EventProjectionLineageR41Protocol.Class,
     EventProjectionLineageR41Protocol.InputRead, EventProjectionLineageR41Protocol.InputUse, EventProjectionLineageR41Protocol.InputWrite),
    new(EventProjectionLineageR41Protocol.Vector, EventProjectionLineageR41Protocol.Class,
     RawUseGrant: EventProjectionLineageR41Protocol.VectorUse, WriteGrant: EventProjectionLineageR41Protocol.VectorWrite)]
        };
        await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(
         Guid.NewGuid(), new(p.TenantId, p.DatabaseId, resource), token)));
        var events = new ResourceDefinition(EventProjectionLineageR41Protocol.Events, ResourceKind.StreamSet, p.TransactionDomainId);
        await SqlClusterProtocol.EqualAsync(events, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(
         Guid.NewGuid(), new(p.TenantId, p.DatabaseId, events), token)));
        var worker = await TextClusterScenario.ConfigureIdentityAsync(fixture, new("lineage-worker-" + Guid.NewGuid().ToString("N"), p.TenantId,
         [new(p.DatabaseId, resource.Name, Capability.DocumentsRead | Capability.DocumentsWrite), new(p.DatabaseId, events.Name, Capability.EventsRead)],
         [EventProjectionLineageR41Protocol.InputUse, EventProjectionLineageR41Protocol.VectorUse, EventProjectionLineageR41Protocol.VectorWrite])
        {
            PolicyEpoch = 1L
        }, token);
        var reader = await TextClusterScenario.ConfigureIdentityAsync(fixture, new("lineage-reader-" + Guid.NewGuid().ToString("N"), p.TenantId,
         [new(p.DatabaseId, resource.Name, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)],
         [EventProjectionLineageR41Protocol.InputUse, EventProjectionLineageR41Protocol.VectorUse])
        {
            PolicyEpoch = 1L
        }, token);
        var state = new EventProjectionLineageR41State(p, worker, reader);
        var seed = Seed(p);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(seed, token));
        await EventProjectionLineageR41Operations.ReceiptAsync(state, seed, receipt,
         [new(EventProjectionLineageR41Protocol.Put,resource.Name,EventProjectionLineageR41Protocol.Source,1L),
    new(EventProjectionLineageR41Protocol.Put,resource.Name,EventProjectionLineageR41Protocol.Target,1L),
    new(EventProjectionLineageR41Protocol.Put,resource.Name,EventProjectionLineageR41Protocol.Baseline,1L),
    new(EventProjectionLineageR41Protocol.PutVector,resource.Name,EventProjectionLineageR41Protocol.Baseline,1L),
    new(EventProjectionLineageR41Protocol.Append,events.Name,EventProjectionLineageR41Protocol.Stream,1L)]);
        state.Successes.Add((seed, receipt));
        var effect = new ApplyVectorProjection(state.Stream, 1L, EventProjectionLineageR41Protocol.Event,
         new(p, resource.Name, EventProjectionLineageR41Protocol.Source), 1L, EventProjectionLineageR41Protocol.Input,
         "lineage-r41-reducer", "v1", 1L, new(resource.Name, EventProjectionLineageR41Protocol.Target,
          EventProjectionLineageR41Protocol.Vector, [1f, 0f], EventProjectionLineageR41Protocol.Space, 1L));
        state.Projection = new(Guid.NewGuid(), p, [effect]);
        state.FreshProjection = state.Projection with
        {
            CommandId = Guid.NewGuid()
        };
        await EventProjectionLineageR41Canonical.CaptureEventAsync(admin, state, token);
        return state;
    }
    private static CommandRequest Seed(PartitionRef p) => new(Guid.NewGuid(), p,
     [new PutDocument(EventProjectionLineageR41Protocol.Collection,EventProjectionLineageR41Protocol.Source,EventProjectionLineageR41Protocol.SourceJson),
   new PutDocument(EventProjectionLineageR41Protocol.Collection,EventProjectionLineageR41Protocol.Target,EventProjectionLineageR41Protocol.TargetJson),
   new PutDocument(EventProjectionLineageR41Protocol.Collection,EventProjectionLineageR41Protocol.Baseline,EventProjectionLineageR41Protocol.BaselineJson),
   new PutVector(EventProjectionLineageR41Protocol.Collection,EventProjectionLineageR41Protocol.Baseline,EventProjectionLineageR41Protocol.Vector,[0f,1f],EventProjectionLineageR41Protocol.Space,1L),
   new AppendEvents(EventProjectionLineageR41Protocol.Events,EventProjectionLineageR41Protocol.Stream,
    [new(EventProjectionLineageR41Protocol.Event,EventProjectionLineageR41Protocol.EventType,EventProjectionLineageR41Protocol.EventJson)],ExpectedStreamRevision.NoStream,1L)]);
}
