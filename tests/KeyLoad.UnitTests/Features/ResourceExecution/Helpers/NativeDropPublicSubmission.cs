using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPublicSubmission
{
    internal static NativeDropPublicCall Start(NativeDropRuntimeProfile profile, DropTableRequest body,
        Guid commandId, CancellationToken token)
    {
        var source = profile.Source;
        var principal = source.Store.Read(view => source.Database.Principal(view,
            PartitionStoreProtocol.AdministratorId, source.Clock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var codec = source.Silo.CatalogRequestCodec();
        var signed = codec.CreateCommand(requestId, principal.Id, OperationKind.DropTable, commandId,
            NativeSerialization.Serialize(body));
        var expiry = codec.Verify(signed).Envelope.ExpiresAt;
        var original = ExecuteAsync(profile, principal, requestId, commandId, signed, token);
        return new(requestId, commandId, expiry, profile.Track(original));
    }

    private static async Task<GrainOperationReply> ExecuteAsync(NativeDropRuntimeProfile profile,
        PrincipalRecord principal, Guid requestId, Guid commandId, string signed, CancellationToken token)
    {
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = profile.Source.Silo.OpenRequestContext(principal, requestId, commandId, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await profile.Source.Silo.ExecuteAsync(requestId, signed, command: true, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.RecoveryRequired, NativeDropRuntimeProtocol.MissingReply);
    }
}
