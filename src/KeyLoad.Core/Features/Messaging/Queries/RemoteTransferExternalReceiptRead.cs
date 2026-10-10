using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal QueueTransferReceiptInspection MintRemoteTransferReceiptInspection(string principalId,
        InspectQueueTransferReceiptRequest request, RemoteTransferSourceDispatch original,
        RegisteredPhysicalOwnerV1 sourceOwner, QueueTransferReceiptInspection receipt, ReadExecutionBudget work)
    {
        work.Check();
        work.ChargeBytes(NativeSerialization.Measure(receipt));
        return Store.Read(view => MintRemoteTransferReceiptAtCut(work.CreateView(view), principalId,
            request, original, sourceOwner, receipt, work));
    }

    private QueueTransferReceiptInspection MintRemoteTransferReceiptAtCut(IKeyValueView view, string principalId,
        InspectQueueTransferReceiptRequest request, RemoteTransferSourceDispatch original,
        RegisteredPhysicalOwnerV1 sourceOwner, QueueTransferReceiptInspection receipt, ReadExecutionBudget work)
    {
        var current = ReadRemoteTransferReceiptDispatch(view, principalId, request, work)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Unavailable);
        RequireRemoteTransferSameDispatch(original, current);
        ValidateRemoteTransferOriginalSourceCommit(view, current.Intent);
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        var registeredSource = directory.Owners.SingleOrDefault(entry =>
            entry.Owner.PhysicalShardId == directory.ControlOwner.PhysicalShardId);
        if (registeredSource is null || !PhysicalOwnerEntryValidation.Same(sourceOwner, registeredSource)
            || !PhysicalOwnerEntryValidation.Valid(sourceOwner)
            || sourceOwner.Owner.Incarnation != Store.Identity.Incarnation
            || receipt.SourceQueue != current.Claims.Source || receipt.DestinationQueue != current.Claims.Destination
            || receipt.TransferId != current.Claims.TransferId || string.IsNullOrWhiteSpace(receipt.ReceiptToken)
            || receipt.TargetCommit.Incarnation != current.Target.DestinationOwner.Owner.Incarnation
            || receipt.TargetCommit.AtomicPartitionId != current.Claims.Destination.Partition.AtomicPartitionId
            || receipt.TargetCommit.Position < RemoteTransferAttemptProtocol.MinimumNativePosition)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        var wrapper = new RemoteTransferExternalReceipt(RemoteTransferPeerProtocol.ExternalReceiptPurpose,
            sourceOwner, current.Target.DestinationOwner, current.Claims.Source, current.Claims.Destination,
            current.Claims.TransferId, principalId, current.Claims.Fingerprint, receipt.ReceiptToken,
            receipt.TargetCommit, current.Target.OriginalSourceCommit!,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt.ReceiptToken))));
        var token = Sign(wrapper);
        RequireTokenBound(token);
        if (Encoding.UTF8.GetByteCount(token) > current.Target.MaximumReservedReceiptBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteTransferPeerProtocol.Unavailable); }
        var result = receipt with { ReceiptToken = token };
        work.ChargeBytes(NativeSerialization.Measure(result));
        work.Check();
        return result;
    }

    internal static void RequireRemoteTransferSameDispatch(RemoteTransferSourceDispatch original,
        RemoteTransferSourceDispatch current)
    {
        if (original.PolicyEpoch != current.PolicyEpoch || original.FieldHeaderDigest != current.FieldHeaderDigest
            || original.Intent.IntentToken != current.Intent.IntentToken
            || original.Target.OriginalSourceCommit != current.Target.OriginalSourceCommit
            || !PhysicalOwnerEntryValidation.Same(original.Target.DestinationOwner, current.Target.DestinationOwner))
        { throw Errors.Fail(ErrorCode.PermissionDenied, RemoteTransferPeerProtocol.Unavailable); }
    }
}
