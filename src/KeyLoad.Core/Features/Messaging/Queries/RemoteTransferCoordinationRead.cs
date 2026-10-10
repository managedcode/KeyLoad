using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteTransferCoordinationHint? ReadTransferCoordinationHint(IKeyValueView view,
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, string subject)
    {
        var record = NativeSerialization.Deserialize<RemoteTransferIntentRecord>(value);
        if (record is null || record.Source is null || record.Source.Partition is null || record.TransferId == Guid.Empty
            || !key.SequenceEqual(RemoteTransferStorage.IntentKey(record.Source, record.TransferId)))
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCoordinationProtocol.InvalidHint); }
        ValidateIntentRecord(view, record, record.Source, record.TransferId);
        _ = RemoteTransferStorage.RequireSourceCounter(view, record.Source);
        if (record.State != QueueTransferState.OutputPending || record.PrincipalId != subject)
        { return null; }
        var principal = Principal(view, subject, Clock.GetUtcNow());
        if (principal.TenantId != record.Source.Partition.TenantId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteTransferCoordinationProtocol.InvalidHint); }
        RequireTransferAdministrator(principal);
        var resource = Resource(view, record.Source.Partition, record.Source.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, record.Source, resource);
        RequireTransferPublisher(principal, record.Source, resource);
        return new(record.Source, record.Destination, record.TransferId, subject, record.Fingerprint,
            RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken), Token(view, record.Source.Partition, Store.Position),
            record.Attempts?.Generation ?? RemoteTransferAttemptProtocol.FirstGeneration, record.Attempts?.Ceiling,
            record.Repairs?.AcceptPolicyGeneration ?? RemoteTransferRepairProtocol.InitialGeneration,
            record.Repairs?.CompleteGeneration ?? RemoteTransferRepairProtocol.InitialGeneration, record.Repairs?.Ceiling);
    }
}
