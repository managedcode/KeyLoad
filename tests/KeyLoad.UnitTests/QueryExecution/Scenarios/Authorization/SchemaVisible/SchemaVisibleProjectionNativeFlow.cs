using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.QueryExecution.BinderFieldLineageProtocol;
using P = KeyLoad.UnitTests.Features.QueryExecution.SchemaVisibleProjectionNativeProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionNativeFlow
{
    internal static Task RunAsync(CancellationToken token)
        => SafeProjectorOperationDatabase.RunAsync(owner => ExecuteAsync(owner, token), token);

    private static async Task ExecuteAsync(SafeProjectorOperationDatabase owner, CancellationToken token)
    {
        var fixture = owner.Source;
        var resource = fixture.Configure(Collection, ResourceKind.Collection, fields:
        [new(ArrayPolicy, Classification, SchemaReadGrant: P.SchemaGrant),
         new(DottedPolicy, Classification, SchemaReadGrant: P.SchemaGrant)]);
        SchemaVisibleProjectionNativeJoins.Configure(fixture);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new PutDocument(Collection, DocumentId, BinderFieldLineageRequests.OriginalJson)]);
        var seed = SafeProjectorOperationSeed.Issue(owner, OperationKind.Batch, command.CommandId, command);
        var receipt = seed.Result.Get<CommitReceipt>();
        await SafeProjectorOperationSeed.EqualAsync(receipt, new CommitReceipt(command.CommandId, seed.ExpectedToken,
            [new(PutDocumentKind, Collection, DocumentId, OriginalRevision)], DurabilityProfile.ProcessDurable));
        await Assert.That(receipt.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        await SchemaVisibleProjectionNativeAuthority.InvalidAsync(fixture, resource);
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        await PhaseAsync(fixture, engine, P.InitialEpoch, false, false, token);
        await PhaseAsync(fixture, engine, P.RawEpoch, true, false, token);
        await PhaseAsync(fixture, engine, P.SchemaEpoch, true, true, token);
        await PhaseAsync(fixture, engine, P.RevokedEpoch, true, false, token);
        var mixed = SchemaVisibleProjectionNativeAuthority.Mixed(resource);
        await BinderFieldLineagePages.EqualAsync(mixed, fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Tenant, Database, mixed) { ExpectedSchemaVersion = resource.SchemaVersion }).Get<ResourceDefinition>());
        await SchemaVisibleProjectionNativeAuthority.SetAsync(fixture, P.MixedEpoch, false, false);
        await SchemaVisibleProjectionNativeAssertions.WaveAsync(engine, fixture.Store, fixture.Partition, false, false, true, token);
        await SafeProjectorOperationReplay.AllAsync(owner, [seed], P.MixedEpoch);
        await SchemaVisibleProjectionNativeRoot.ConfigureAsync(fixture, mixed);
        await SchemaVisibleProjectionNativeRoot.WaveAsync(fixture.Database, engine, fixture.Store, fixture.Partition, token);
        await SchemaVisibleProjectionNativeCold.RunAsync(owner, seed, token);
    }

    private static async Task PhaseAsync(TestDatabase fixture, QueryEngine engine, long epoch, bool raw, bool visible,
        CancellationToken token)
    {
        await SchemaVisibleProjectionNativeAuthority.SetAsync(fixture, epoch, raw, visible);
        await SchemaVisibleProjectionNativeAssertions.WaveAsync(engine, fixture.Store, fixture.Partition, raw, visible, false, token);
    }
}
