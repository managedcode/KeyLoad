using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteTargetInbox(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation, long position)
    {
        var request = Payload<CommitInboxRequest>(operation);
        if (request.CommandId != operation.Id)
        { throw Errors.Fail(ErrorCode.Validation, TargetInboxProtocol.Invalid); }
        return Result(CompleteTargetInbox(transaction, principal, request, operation.EvaluatedAt, position));
    }

    private CommitInboxResult CompleteTargetInbox(IAtomicTransaction transaction, PrincipalRecord principal,
        CommitInboxRequest request, DateTimeOffset now, long position)
    {
        AuthorizeInbox(transaction, principal, request);
        var resource = Resource(transaction, request.Target.Partition, request.Target.Queue, ResourceKind.WorkQueue);
        var key = TargetInboxStorage.Key(request);
        var identity = TargetInboxStorage.Identity(request);
        var fingerprint = JsonData.Fingerprint(request.Effects);
        if (transaction.GetRecord<TargetInboxRecord>(key) is { } existing)
        {
            _ = TargetInboxStorage.Capacity(transaction, request.Target, required: true);
            if (existing.IdentityFingerprint != identity || existing.Receipt is null || string.IsNullOrEmpty(existing.EffectsFingerprint))
            { throw Errors.Fail(ErrorCode.Corruption, TargetInboxProtocol.Corrupt); }
            if (existing.EffectsFingerprint != fingerprint)
            { throw Errors.Fail(ErrorCode.Conflict, TargetInboxProtocol.Conflict); }
            ReauthorizeEffects(transaction, principal, request.Target.Partition, request.Effects);
            return new(existing.Receipt, true, existing.Receipt.Token);
        }
        var effects = ApplyMutations(transaction, principal, request.Target.Partition, request.Effects, now, position);
        var receipt = new CommitReceipt(request.CommandId, Token(transaction, request.Target.Partition, position), effects, Durability);
        var record = NativeSerialization.Serialize(new TargetInboxRecord(identity, fingerprint, receipt));
        var bytes = checked(key.LongLength + record.LongLength);
        var capacity = TargetInboxStorage.Add(TargetInboxStorage.Capacity(transaction, request.Target, required: false),
            bytes, resource.InboxPolicy!);
        transaction.Put(key, record);
        transaction.PutRecord(TargetInboxStorage.CapacityKey(request.Target), capacity);
        return new(receipt, false, receipt.Token);
    }
}
