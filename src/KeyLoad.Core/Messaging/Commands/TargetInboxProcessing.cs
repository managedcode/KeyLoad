using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes TargetInboxProcessing operations against the caller's database context.</summary>
public static class TargetInboxProcessing
{
    internal static Result<object> ExecuteTargetInbox(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, Command<DatabaseCommand> operation, long position)
    {
        var request = OperationProtocol.Payload<CommitInboxRequest>(operation);
        if (request.CommandId != operation.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, TargetInboxProtocol.Invalid);
        }

        return OperationProtocol.Result(engine.CompleteTargetInbox(transaction, principal, request, operation.Timestamp, position));
    }

    internal static CommitInboxResult CompleteTargetInbox(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord principal, CommitInboxRequest request, DateTimeOffset now, long position)
    {
        engine.AuthorizeInbox(transaction, principal, request);
        var resource = engine.Resource(transaction, request.Target.Partition, request.Target.Queue, ResourceKind.WorkQueue);
        var key = TargetInboxStorage.Key(request);
        var identity = TargetInboxStorage.Identity(request);
        var fingerprint = JsonData.Fingerprint(request.Effects);
        var retained = transaction.ReadOwnedValue(key);
        var currentCapacity = TargetInboxStorage.Capacity(transaction, request.Target, required: retained is not null);
        if (retained is not null)
        {
            var existing = OrleansSerialization.Deserialize<TargetInboxRecord>(retained);
            TargetInboxStorage.RequireSelectedCapacity(currentCapacity, checked(key.LongLength + retained.LongLength));
            if (existing.IdentityFingerprint != identity || existing.Receipt is null || string.IsNullOrEmpty(existing.EffectsFingerprint))
            {
                throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt);
            }

            if (existing.EffectsFingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, TargetInboxProtocol.Conflict);
            }

            engine.ReauthorizeEffects(transaction, principal, request.Target.Partition, request.Effects);
            return new(existing.Receipt, true, existing.Receipt.Token);
        }

        var effects = engine.ApplyMutations(transaction, principal, request.Target.Partition, request.Effects, now, position);
        var receipt = new CommitReceipt(request.CommandId, engine.Token(transaction, request.Target.Partition, position), effects, engine.Durability, engine.AcknowledgedPersistedCopies);
        var record = OrleansSerialization.Serialize(new TargetInboxRecord(identity, fingerprint, receipt));
        var bytes = checked(key.LongLength + record.LongLength);
        var capacity = TargetInboxStorage.Add(currentCapacity, bytes, resource.InboxPolicy!);
        transaction.Put(key, record);
        transaction.PutRecord(TargetInboxStorage.CapacityKey(request.Target), capacity);
        return new(receipt, false, receipt.Token);
    }
}
