using KeyLoad.Query;
using KeyLoad.UnitTests.Authorization;
using static KeyLoad.UnitTests.QueryExecution.BinderFieldLineageProtocol;
using P = KeyLoad.UnitTests.QueryExecution.SchemaVisibleProjectionProtocol;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SchemaVisibleProjectionFlow
{
    internal static Task RunAsync(CancellationToken token)
        => SafeProjectorOperationDatabase.RunAsync(owner => ExecuteAsync(owner, token), token);

    private static async Task ExecuteAsync(SafeProjectorOperationDatabase owner, CancellationToken token)
    {
        var fixture = owner.Source;
        var resource = fixture.Configure(Collection, ResourceKind.Collection, fields:
        [new(ArrayPolicy, Classification, SchemaReadGrant: P.SchemaGrant),
         new(DottedPolicy, Classification, SchemaReadGrant: P.SchemaGrant)]);
        SchemaVisibleProjectionJoins.Configure(fixture);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new PutDocument(Collection, DocumentId, BinderFieldLineageRequests.OriginalJson)]);
        var seed = SafeProjectorOperationSeed.Issue(owner, OperationKind.Batch, command.CommandId, command);
        var receipt = seed.Result.Get<CommitReceipt>();
        await SafeProjectorOperationSeed.EqualAsync(receipt, new CommitReceipt(command.CommandId, seed.ExpectedToken, [new(PutDocumentKind, Collection, DocumentId, OriginalRevision)], DurabilityProfile.ProcessDurable, 1));
        await Assert.That(receipt.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        await SchemaVisibleProjectionAuthority.InvalidAsync(fixture, resource);
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        await PhaseAsync(fixture, engine, P.InitialEpoch, false, false, token);
        await PhaseAsync(fixture, engine, P.RawEpoch, true, false, token);
        await PhaseAsync(fixture, engine, P.SchemaEpoch, true, true, token);
        await PhaseAsync(fixture, engine, P.RevokedEpoch, true, false, token);
        var mixed = SchemaVisibleProjectionAuthority.Mixed(resource);
        await BinderFieldLineagePages.EqualAsync(mixed, fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Tenant, Database, mixed) { ExpectedSchemaVersion = resource.SchemaVersion }).Get<ResourceDefinition>());
        await SchemaVisibleProjectionAuthority.SetAsync(fixture, P.MixedEpoch, false, false);
        await SchemaVisibleProjectionAssertions.WaveAsync(engine, fixture.Store, fixture.Partition, false, false, true, token);
        await SafeProjectorOperationReplay.AllAsync(owner, [seed], P.MixedEpoch);
        await SchemaVisibleProjectionRoot.ConfigureAsync(fixture, mixed);
        await SchemaVisibleProjectionRoot.WaveAsync(fixture, fixture.Database, engine, fixture.Store, fixture.Partition, token);
        await SchemaVisibleProjectionCold.RunAsync(owner, seed, token);
    }

    private static async Task PhaseAsync(TestDatabase fixture, QueryEngine engine, long epoch, bool raw, bool visible,
        CancellationToken token)
    {
        await SchemaVisibleProjectionAuthority.SetAsync(fixture, epoch, raw, visible);
        await SchemaVisibleProjectionAssertions.WaveAsync(engine, fixture.Store, fixture.Partition, raw, visible, false, token);
    }
}
