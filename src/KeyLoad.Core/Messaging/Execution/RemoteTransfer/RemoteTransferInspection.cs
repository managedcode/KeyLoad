using System.Text;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferInspection operations against the caller's database context.</summary>
public static class RemoteTransferInspection
{
    internal const string TransferLaneScopeMessage = "The transfer lane does not match its operation partition.";
    internal const string TransferReceiptCorruptMessage = "Persisted destination transfer receipt is inconsistent.";
    /// <summary>Reads transfer state and protocol tokens after reloading persisted administrator and data authority.</summary>
    /// <param name = "principalId">The authenticated persisted principal.</param>
    /// <param name = "source">The complete source queue lane.</param>
    /// <param name = "transferId">The caller-stable transfer identifier.</param>
    /// <param name = "cancellationToken">Cancels bounded inspection work.</param>
    /// <returns>The current transfer view, or null when the source identity is not retained.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static QueueTransferInspection? InspectQueueTransfer(this DatabaseEngine engine, string principalId, QueueLaneRef source, Guid transferId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.Clock, cancellationToken);
        return engine.InspectQueueTransferRead(principalId, source, transferId, budget);
    }

    internal static QueueTransferInspection? InspectQueueTransferRead(this DatabaseEngine engine, string principalId, QueueLaneRef source, Guid transferId, ReadExecutionBudget budget)
    {
        budget.Check();
        JsonData.Identifier(principalId);
        ValidateTransferLane(source);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }

        var request = new InspectQueueTransferRequest(source, transferId);
        budget.ChargeBytes(InspectionRequestBytes(principalId, request));
        return engine.Store.Read(view =>
        {
            var result = engine.InspectQueueTransfer(budget.CreateView(view), principalId, source, transferId);
            budget.CheckResult(result);
            return result;
        });
    }

    /// <summary>Reads the immutable destination receipt without asserting source completion.</summary>
    /// <param name = "principalId">The authenticated persisted principal.</param>
    /// <param name = "destination">The complete destination queue lane.</param>
    /// <param name = "source">The complete source queue lane.</param>
    /// <param name = "transferId">The caller-stable transfer identifier.</param>
    /// <param name = "cancellationToken">Cancels bounded receipt inspection work.</param>
    /// <returns>The committed destination proof, or null when no receipt is retained.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static QueueTransferReceiptInspection? InspectQueueTransferReceipt(this DatabaseEngine engine, string principalId, QueueLaneRef destination, QueueLaneRef source, Guid transferId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.Clock, cancellationToken);
        return engine.InspectQueueTransferReceiptRead(principalId, destination, source, transferId, budget);
    }

    internal static QueueTransferReceiptInspection? InspectQueueTransferReceiptRead(this DatabaseEngine engine, string principalId, QueueLaneRef destination, QueueLaneRef source, Guid transferId, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        budget.Check();
        JsonData.Identifier(principalId);
        RemoteTransferValidation.ValidateTransferPair(source, destination);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }

        var request = new InspectQueueTransferReceiptRequest(destination, source, transferId);
        budget.ChargeBytes(InspectionRequestBytes(principalId, request));
        return engine.Store.Read(view =>
        {
            var result = engine.InspectQueueTransferReceipt(budget.CreateView(view), principalId, destination, source, transferId);
            budget.CheckResult(result);
            return result;
        });
    }

    internal static long InspectionRequestBytes<T>(string principalId, T request) => checked(Encoding.UTF8.GetByteCount(principalId) + OrleansSerialization.Serialize(request).LongLength);
    internal static QueueTransferInspection? InspectQueueTransfer(this DatabaseEngine engine, IKeyValueView view, string principalId, QueueLaneRef source, Guid transferId)
    {
        ValidateTransferLane(source);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }

        var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, source, Capability.QueueInspect);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, source.Partition, source.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferInspector(principal, source, resource);
        var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(source, transferId));
        if (record is null)
        {
            return null;
        }

        engine.ValidateIntentRecord(view, record, source, transferId);
        _ = RemoteTransferStorage.RequireSourceCounter(view, source);
        return new(source, transferId, record.Destination, record.State, record.IntentToken, record.ReceiptToken);
    }

    internal static QueueTransferReceiptInspection? InspectQueueTransferReceipt(this DatabaseEngine engine, IKeyValueView view, string principalId, QueueLaneRef destination, QueueLaneRef source, Guid transferId)
    {
        RemoteTransferValidation.ValidateTransferPair(source, destination);
        if (transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }

        var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, destination, Capability.QueueInspect);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferInspector(principal, destination, resource);
        var record = view.GetRecord<RemoteTransferTargetReceiptRecord>(RemoteTransferStorage.TargetReceiptKey(source, transferId, destination));
        if (record is null)
        {
            return null;
        }

        engine.ValidateTargetReceiptRecord(view, record, source, destination, transferId);
        _ = RemoteTransferStorage.RequireTargetCounter(view, destination);
        return new(destination, source, transferId, record.ReceiptToken, record.TargetCommit);
    }

    internal static void ValidateTargetReceiptRecord(this DatabaseEngine engine, IKeyValueView view, RemoteTransferTargetReceiptRecord record, QueueLaneRef source, QueueLaneRef destination, Guid transferId)
    {
        if (record.Source != source || record.Destination != destination || record.TransferId != transferId || record.PrincipalId is null || record.Fingerprint is null || record.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferReceiptCorruptMessage);
        }

        var claims = engine.VerifyRetained<RemoteTransferReceiptClaims>(record.ReceiptToken);
        if (claims is null || claims.Source is null || claims.Destination is null || claims.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferReceiptCorruptMessage);
        }

        if (claims.Purpose != RemoteTransferProtocol.ReceiptPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Source != source || claims.Destination != destination || claims.TransferId != transferId || claims.PrincipalId != record.PrincipalId || claims.Fingerprint != record.Fingerprint || claims.TargetCommit != record.TargetCommit)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferReceiptCorruptMessage);
        }

        engine.ValidateCommitToken(view, destination.Partition, record.TargetCommit, ErrorCode.Corruption, TransferReceiptCorruptMessage);
    }

    internal static void ValidateTransferSource(QueueLaneRef source, Guid transferId, PartitionRef partition)
    {
        ValidateTransferLane(source);
        EngineResourceAccess.ValidatePartition(partition);
        if (source.Partition != partition || transferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferLaneScopeMessage);
        }
    }

    internal static void ValidateTransferLane(QueueLaneRef lane)
    {
        ArgumentNullException.ThrowIfNull(lane);
        EngineResourceAccess.ValidatePartition(lane.Partition);
        JsonData.Identifier(lane.Queue);
    }
}
