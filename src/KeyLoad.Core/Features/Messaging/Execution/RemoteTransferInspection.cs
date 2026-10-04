using System.Text;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string TransferLaneScopeMessage = "The transfer lane does not match its operation partition.";
    private const string TransferReceiptCorruptMessage = "Persisted destination transfer receipt is inconsistent.";

    /// <summary>Reads transfer state and protocol tokens after reloading persisted administrator and data authority.</summary>
    /// <param name="principalId">The authenticated persisted principal.</param>
    /// <param name="source">The complete source queue lane.</param>
    /// <param name="transferId">The caller-stable transfer identifier.</param>
    /// <param name="cancellationToken">Cancels bounded inspection work.</param>
    /// <returns>The current transfer view, or null when the source identity is not retained.</returns>
    public QueueTransferInspection? InspectQueueTransfer(string principalId, QueueLaneRef source, Guid transferId,
        CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        budget.Check();
        JsonData.Identifier(principalId);
        ValidateTransferLane(source);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }
        var request = new InspectQueueTransferRequest(source, transferId);
        budget.ChargeBytes(InspectionRequestBytes(principalId, request));
        return Store.Read(view =>
        {
            var result = InspectQueueTransfer(budget.CreateView(view), principalId, source, transferId);
            budget.CheckResult(result);
            return result;
        });
    }

    /// <summary>Reads the immutable destination receipt without asserting source completion.</summary>
    /// <param name="principalId">The authenticated persisted principal.</param>
    /// <param name="destination">The complete destination queue lane.</param>
    /// <param name="source">The complete source queue lane.</param>
    /// <param name="transferId">The caller-stable transfer identifier.</param>
    /// <param name="cancellationToken">Cancels bounded receipt inspection work.</param>
    /// <returns>The committed destination proof, or null when no receipt is retained.</returns>
    public QueueTransferReceiptInspection? InspectQueueTransferReceipt(string principalId,
        QueueLaneRef destination, QueueLaneRef source, Guid transferId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        budget.Check();
        JsonData.Identifier(principalId);
        ValidateTransferPair(source, destination);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }
        var request = new InspectQueueTransferReceiptRequest(destination, source, transferId);
        budget.ChargeBytes(InspectionRequestBytes(principalId, request));
        return Store.Read(view =>
        {
            var result = InspectQueueTransferReceipt(budget.CreateView(view), principalId, destination, source, transferId);
            budget.CheckResult(result);
            return result;
        });
    }

    private static long InspectionRequestBytes<T>(string principalId, T request)
        => checked(Encoding.UTF8.GetByteCount(principalId) + NativeSerialization.Serialize(request).LongLength);

    private QueueTransferInspection? InspectQueueTransfer(IKeyValueView view, string principalId,
        QueueLaneRef source, Guid transferId)
    {
        ValidateTransferLane(source);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        RequireTransferAdministrator(principal);
        var resource = Resource(view, source.Partition, source.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, source, resource);
        var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(source, transferId));
        if (record is null)
        {
            return null;
        }
        ValidateIntentRecord(record, source, transferId);
        _ = RemoteTransferStorage.RequireSourceCounter(view, source);
        return new(source, transferId, record.Destination, record.State, record.IntentToken, record.ReceiptToken);
    }

    private QueueTransferReceiptInspection? InspectQueueTransferReceipt(IKeyValueView view, string principalId,
        QueueLaneRef destination, QueueLaneRef source, Guid transferId)
    {
        ValidateTransferPair(source, destination);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        RequireTransferAdministrator(principal);
        var resource = Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, destination, resource);
        var record = view.GetRecord<RemoteTransferTargetReceiptRecord>(
            RemoteTransferStorage.TargetReceiptKey(source, transferId, destination));
        if (record is null)
        {
            return null;
        }
        ValidateTargetReceiptRecord(record, source, destination, transferId);
        _ = RemoteTransferStorage.RequireTargetCounter(view, destination);
        return new(destination, source, transferId, record.ReceiptToken, record.TargetCommit);
    }

    private void ValidateTargetReceiptRecord(RemoteTransferTargetReceiptRecord record,
        QueueLaneRef source, QueueLaneRef destination, Guid transferId)
    {
        if (record.Source != source || record.Destination != destination || record.TransferId != transferId
            || record.PrincipalId is null || record.Fingerprint is null || record.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferReceiptCorruptMessage);
        }
        var claims = Verify<RemoteTransferReceiptClaims>(record.ReceiptToken, Limits.MaxBatchBytes);
        if (claims is null || claims.Source is null || claims.Destination is null || claims.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferReceiptCorruptMessage);
        }
        if (claims.Purpose != RemoteTransferProtocol.ReceiptPurpose || claims.Incarnation != Store.Identity.Incarnation
            || claims.Source != source || claims.Destination != destination || claims.TransferId != transferId
            || claims.PrincipalId != record.PrincipalId || claims.Fingerprint != record.Fingerprint
            || claims.TargetCommit != record.TargetCommit || record.TargetCommit.Incarnation != Store.Identity.Incarnation
            || record.TargetCommit.AtomicPartitionId != destination.Partition.AtomicPartitionId
            || record.TargetCommit.Position < 1 || record.TargetCommit.OwnershipEpoch != 1)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferReceiptCorruptMessage);
        }
    }

    private static void ValidateTransferSource(QueueLaneRef source, Guid transferId, PartitionRef partition)
    {
        ValidateTransferLane(source);
        ValidatePartition(partition);
        if (source.Partition != partition || transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }
    }

    private static void ValidateTransferLane(QueueLaneRef lane)
    {
        ArgumentNullException.ThrowIfNull(lane);
        ValidatePartition(lane.Partition);
        JsonData.Identifier(lane.Queue);
    }
}
