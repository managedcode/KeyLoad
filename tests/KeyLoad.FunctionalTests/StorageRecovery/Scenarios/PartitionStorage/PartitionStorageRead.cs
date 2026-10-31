using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.FunctionalTests.StorageRecovery.PartitionStorageProtocol;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PartitionStorageRead
{
    internal static async Task RequireAsync(IServiceProvider source,
        CommitToken minimum, CancellationToken token)
    {
        var database = source.GetRequiredService<PartitionHostApplicationOwner>().Borrow().Database;
        var clock = source.GetRequiredService<TimeProvider>();
        var principal = database.Store.Read(view => database.Principal(view, P.Root, clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var envelope = source.GetRequiredService<GrainRequestCodec>().CreateRead(requestId, principal.Id, GrainReadKind.Document,
            new GetDocumentRequest(P.Reference(), minimum));
        var connectionId = source.GetRequiredService<ConnectionOwnerIdentity>().Id;
        using var scope = new GrainRequestIdentityScope(source, principal, requestId, Guid.Empty,
            token, connectionId: connectionId);
        var connection = source.GetRequiredService<IGrainFactory>().GetGrain<IConnectionGrain>(connectionId);
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            execution => connection.ExecuteStreamAsync(envelope, execution),
            requestId, token).ConfigureAwait(false);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = GrainReplyFactory.RequireValue(reply).Value as DocumentResult;
        var configured = database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.GlobalOutcome(P.Root, P.Configure)));
        await Assert.That(configured).IsNotNull();
        await Assert.That(configured!.PreparedCommand.CommandId).IsEqualTo(P.Configure);
        await Assert.That(configured.PreparedCommand.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(Errors.Code(configured.Result.Problem).Value).IsNull();
        var definition = new ResourceDefinition(P.Collection, ResourceKind.Collection, P.Partition.TransactionDomainId);
        var expectedConfiguration = new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId, definition);
        var originalConfiguration = ((ConfigureResourceRequest)configured.PreparedCommand.Value.Input);
        await Assert.That(JsonDefaults.Serialize(originalConfiguration).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expectedConfiguration))).IsTrue();
        var creation = ResourceLifetimeIdentity.Create(configured.PreparedCommand,
            P.Partition.TenantId, P.Partition.DatabaseId, definition);
        var reference = P.Reference() with { Lifetime = new(creation.Incarnation.Value) };
        var expected = new DocumentResult(reference, P.InitialRevision, P.Json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
            .IsTrue();
    }
}
