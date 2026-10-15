using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeRead
{
    internal static async Task RequireAsync(ControlledPartitionMovementNativeHost source,
        CommitToken minimum, CancellationToken token)
    {
        var principal = source.Store.Read(view => source.Database.Principal(view, P.Root, source.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var signed = source.Silo.CatalogRequestCodec().CreateRead(requestId, principal.Id, GrainReadKind.Document,
            NativeSerialization.Serialize(new GetDocumentRequest(P.Reference(), minimum)));
        GrainOperationReply? reply = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var scope = source.Silo.OpenRequestContext(principal, requestId, Guid.Empty, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await source.Silo.ExecuteAsync(requestId, signed, command: false, token)
                    .ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(reply is not null).IsTrue();
        await Assert.That(reply!.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as DocumentResult;
        var expected = new DocumentResult(P.Reference(), P.InitialRevision, P.Json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
            .IsTrue();
    }
}
