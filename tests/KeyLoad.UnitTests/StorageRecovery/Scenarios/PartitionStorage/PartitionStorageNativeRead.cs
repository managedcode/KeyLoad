using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeRead
{
    internal static async Task RequireAsync(IServiceProvider source,
        CommitToken minimum, CancellationToken token)
    {
        var database = source.GetRequiredService<PartitionHostApplicationOwner>().Borrow().Database;
        var clock = source.GetRequiredService<TimeProvider>();
        var principal = database.Store.Read(view => database.Principal(view, P.Root, clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var signed = source.GetRequiredService<GrainRequestCodec>().CreateRead(requestId, principal.Id, GrainReadKind.Document,
            NativeSerialization.Serialize(new GetDocumentRequest(P.Reference(), minimum)));
        var connectionId = source.GetRequiredService<NativeConnectionOwnerIdentity>().Id;
        using var scope = new GrainRequestIdentityScope(source, principal, requestId, Guid.Empty,
            token, connectionId: connectionId);
        var connection = source.GetRequiredService<IGrainFactory>().GetGrain<IConnectionGrain>(connectionId);
        var reply = await GrainRequestStreamConsumer.DrainAsync(
            execution => connection.ExecuteStreamAsync(signed, execution),
            source.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            requestId, clock, source.GetRequiredService<IOptions<GrainRoutingOptions>>(), token).ConfigureAwait(false);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value as DocumentResult;
        var configured = database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.GlobalOutcome(P.Root, P.Configure)));
        await Assert.That(configured).IsNotNull();
        await Assert.That(configured!.PreparedCommand.CommandId).IsEqualTo(P.Configure);
        await Assert.That(configured.PreparedCommand.Value!.Kind).IsEqualTo(OperationKind.ConfigureResource);
        await Assert.That(Errors.Code(configured.Result.Problem)).IsNull();
        var definition = new ResourceDefinition(P.Collection, ResourceKind.Collection, P.Partition.TransactionDomainId);
        var expectedConfiguration = new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId, definition);
        var originalConfiguration = NativeSerialization.Deserialize<ConfigureResourceRequest>(configured.PreparedCommand.Value.Input.Span);
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
