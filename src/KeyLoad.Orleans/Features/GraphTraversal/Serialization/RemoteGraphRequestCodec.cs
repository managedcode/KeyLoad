using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal readonly struct RemoteGraphRequestCodec(GrainRequestCodec owner, DatabaseEngine database,
    TimeProvider clock, TimeSpan requestLifetime)
{
    internal string CreateRemoteGraphRead(Guid requestId, string principalId, GrainReadKind kind,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
        => owner.Issue(RemoteGraphRequestEnvelopes.Read(database, clock, requestLifetime,
            requestId, principalId, kind, payload, expiry));

    internal string CreateRemoteGraphPhase(Guid requestId, ReplicatedOperation operation, DateTimeOffset expiry)
        => owner.Issue(RemoteGraphRequestEnvelopes.Phase(database, clock.GetUtcNow(), requestLifetime,
            requestId, operation, expiry));

    internal string CreateRemoteGraphParentCommand(Guid requestId, string principalId, OperationKind kind,
        Guid commandId, ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
        => owner.Issue(RemoteGraphRequestEnvelopes.ParentCommand(database, clock.GetUtcNow(), requestLifetime,
            requestId, principalId, kind, commandId, payload, expiry));

    internal string CreateRemoteGraphParentRead(Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiry)
        => owner.Issue(RemoteGraphRequestEnvelopes.ParentRead(database, clock.GetUtcNow(), requestLifetime,
            requestId, principalId, payload, expiry));
}
