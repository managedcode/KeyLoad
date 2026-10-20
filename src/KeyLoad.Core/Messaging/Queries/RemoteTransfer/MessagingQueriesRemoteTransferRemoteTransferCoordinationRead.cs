using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingQueriesRemoteTransferRemoteTransferCoordinationRead operations against the caller's database context.</summary>
public static class MessagingQueriesRemoteTransferRemoteTransferCoordinationRead
{
    internal static RemoteTransferCoordinationHint? ReadTransferCoordinationHint(this DatabaseEngine engine, IKeyValueView view, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, string subject)
    {
        var record = OrleansSerialization.Deserialize<RemoteTransferIntentRecord>(value);
        if (record is null || record.Source is null || record.Source.Partition is null || record.TransferId == Guid.Empty || !key.SequenceEqual(RemoteTransferStorage.IntentKey(record.Source, record.TransferId)))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCoordinationProtocol.InvalidHint);
        }

        engine.ValidateIntentRecord(view, record, record.Source, record.TransferId);
        _ = RemoteTransferStorage.RequireSourceCounter(view, record.Source);
        if (record.State != QueueTransferState.OutputPending || record.PrincipalId != subject)
        {
            return null;
        }

        var principal = engine.Principal(view, subject, engine.Clock.GetUtcNow());
        if (principal.TenantId != record.Source.Partition.TenantId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, RemoteTransferCoordinationProtocol.InvalidHint);
        }

        MessagingExecutionRemoteTransferRemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, record.Source, Capability.QueueInspect);
        engine.RequireTransferCapabilities(principal, record.Source, Capability.QueuePublish);
        var resource = engine.Resource(view, record.Source.Partition, record.Source.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferInspector(principal, record.Source, resource);
        engine.RequireTransferPublisher(principal, record.Source, resource);
        return new(record.Source, record.Destination, record.TransferId, subject, record.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken), engine.Token(view, record.Source.Partition, engine.Store.Position), record.Attempts?.Generation ?? RemoteTransferAttemptProtocol.FirstGeneration, record.Attempts?.Ceiling, record.Repairs?.AcceptPolicyGeneration ?? RemoteTransferRepairProtocol.InitialGeneration, record.Repairs?.CompleteGeneration ?? RemoteTransferRepairProtocol.InitialGeneration, record.Repairs?.Ceiling);
    }
}
